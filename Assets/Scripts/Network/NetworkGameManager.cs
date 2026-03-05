using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

public class NetworkGameManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────
    public static NetworkGameManager Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────
    [Header("Session Settings")]
    [SerializeField] private int maxPlayers = 4;

    // ── Public state ──────────────────────────────────────────────────────
    public ISession CurrentSession  { get; private set; }
    public bool     IsHost          { get; private set; }
    public string   LocalPlayerId   { get; private set; }
    public string   LocalPlayerName { get; set; } = "Player";

    private Dictionary<string, int> characterSelections = new Dictionary<string, int>();
    private int  localCharacterIndex = 0;
    private bool localIsReady        = false;

    // ── Events ─────────────────────────────────────────────────────────────
    public event Action         OnSignedIn;
    public event Action<string> OnSignInFailed;
    public event Action         OnSessionCreated;
    public event Action         OnSessionJoined;
    public event Action         OnGameStarting;
    public event Action         OnSessionLeft;
    public event Action<string> OnSessionError;
    public event Action<List<SessionPlayerInfo>> OnPlayersUpdated;

    private List<SessionPlayerInfo> cachedPlayerList = new List<SessionPlayerInfo>();

    // ─────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start() => _ = InitializeAsync();

    private void OnDestroy() => _ = LeaveSessionAsync();

    // ─────────────────────────────────────────────────────────────────────
    // Init & sign-in
    // ─────────────────────────────────────────────────────────────────────

    private async Task InitializeAsync()
    {
        try
        {
            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            LocalPlayerId = AuthenticationService.Instance.PlayerId;
            Debug.Log($"[NetworkGameManager] Signed in as {LocalPlayerId}");
            OnSignedIn?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkGameManager] Sign-in failed: {e.Message}");
            OnSignInFailed?.Invoke(e.Message);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Create session
    // ─────────────────────────────────────────────────────────────────────

    public async Task CreateSessionAsync()
    {
        try
        {
            var options = new SessionOptions
            {
                MaxPlayers = maxPlayers,
                Name       = $"{LocalPlayerName}'s Game"
            }.WithRelayNetwork();

            CurrentSession = await MultiplayerService.Instance.CreateSessionAsync(options);
            IsHost = true;

            SubscribeToSessionEvents();
            characterSelections[LocalPlayerId] = localCharacterIndex;

            Debug.Log($"[NetworkGameManager] Session created. Code: {CurrentSession.Code}");
            OnSessionCreated?.Invoke();
            RefreshPlayerList();
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkGameManager] CreateSession failed: {e.Message}");
            OnSessionError?.Invoke($"Failed to create session: {e.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Join session
    // ─────────────────────────────────────────────────────────────────────

    public async Task JoinSessionAsync(string joinCode)
    {
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            OnSessionError?.Invoke("Please enter a join code.");
            return;
        }

        try
        {
            CurrentSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode.Trim().ToUpper());
            IsHost = false;

            SubscribeToSessionEvents();
            characterSelections[LocalPlayerId] = localCharacterIndex;

            Debug.Log($"[NetworkGameManager] Joined session: {CurrentSession.Id}");
            OnSessionJoined?.Invoke();
            RefreshPlayerList();
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkGameManager] JoinSession failed: {e.Message}");
            OnSessionError?.Invoke($"Failed to join: {e.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Start game
    // ─────────────────────────────────────────────────────────────────────

    public void StartGame()
    {
        if (!IsHost)
        {
            Debug.LogWarning("[NetworkGameManager] Only host can start the game.");
            return;
        }

        OnGameStarting?.Invoke();
        NetworkManager.Singleton.SceneManager.LoadScene(
            "GameScene",
            UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Character selection
    // FIX: Replaced UpdatePlayerOptionsAsync (wrong API) with
    //      CurrentPlayer.SetProperty() + SaveCurrentPlayerDataAsync()
    // ─────────────────────────────────────────────────────────────────────

    public void SetLocalCharacterSelection(int index)
    {
        localCharacterIndex = index;
        characterSelections[LocalPlayerId] = index;
        _ = SyncPlayerDataAsync();
        RefreshPlayerList();
    }

    public void SetLocalReadyState(bool ready)
    {
        localIsReady = ready;
        _ = SyncPlayerDataAsync();
        RefreshPlayerList();
    }

    private async Task SyncPlayerDataAsync()
    {
        if (CurrentSession == null) return;
        try
        {
            // CORRECT API: set properties on CurrentPlayer then save
            CurrentSession.CurrentPlayer.SetProperty("CharIdx", new PlayerProperty(localCharacterIndex.ToString()));
            CurrentSession.CurrentPlayer.SetProperty("IsReady", new PlayerProperty(localIsReady.ToString()));
            CurrentSession.CurrentPlayer.SetProperty("Name",    new PlayerProperty(LocalPlayerName));
            await CurrentSession.SaveCurrentPlayerDataAsync();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[NetworkGameManager] SyncPlayerData failed: {e.Message}");
        }
    }

    public Dictionary<string, int> GetCharacterSelections() => new Dictionary<string, int>(characterSelections);

    public void RegisterCharacterSelection(string playerId, int characterIndex)
    {
        characterSelections[playerId] = characterIndex;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Player list
    // FIX: Replaced player.Data with player.Properties (correct API)
    //      Replaced CurrentSession.HostId with IsHost (correct API)
    // ─────────────────────────────────────────────────────────────────────

    public List<SessionPlayerInfo> GetPlayerList() => cachedPlayerList;
    public string GetJoinCode()     => CurrentSession?.Code ?? "N/A";
    public int    GetMaxPlayers()   => maxPlayers;
    public bool   AllPlayersReady() => localIsReady;

    private void RefreshPlayerList()
    {
        cachedPlayerList.Clear();
        if (CurrentSession == null) { OnPlayersUpdated?.Invoke(cachedPlayerList); return; }

        foreach (var player in CurrentSession.Players)
        {
            bool isLocal = player.Id == LocalPlayerId;

            int    charIdx = isLocal ? localCharacterIndex : 0;
            bool   ready   = isLocal ? localIsReady : false;
            string name    = isLocal ? LocalPlayerName : "Player";

            // CORRECT API: player.Properties not player.Data
            if (player.Properties != null)
            {
                if (player.Properties.TryGetValue("CharIdx", out var charProp))
                    int.TryParse(charProp.Value, out charIdx);

                if (player.Properties.TryGetValue("IsReady", out var readyProp))
                    bool.TryParse(readyProp.Value, out ready);

                if (player.Properties.TryGetValue("Name", out var nameProp) && !string.IsNullOrEmpty(nameProp.Value))
                    name = nameProp.Value;
            }

            // CORRECT API: IsHost is a bool on ISession meaning "am I the host"
            // There is no per-player HostId — only the local player can be identified as host
            bool isHostPlayer = isLocal && IsHost;

            cachedPlayerList.Add(new SessionPlayerInfo(player.Id, name, charIdx, ready, isLocal, isHostPlayer));
        }

        OnPlayersUpdated?.Invoke(cachedPlayerList);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Leave
    // ─────────────────────────────────────────────────────────────────────

    public async Task LeaveSessionAsync()
    {
        if (CurrentSession == null) return;

        UnsubscribeFromSessionEvents();

        try { await CurrentSession.LeaveAsync(); }
        catch (Exception e) { Debug.LogWarning($"[NetworkGameManager] Leave error: {e.Message}"); }

        CurrentSession = null;
        NetworkManager.Singleton?.Shutdown();
        OnSessionLeft?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Session events
    // ─────────────────────────────────────────────────────────────────────

    private void SubscribeToSessionEvents()
    {
        if (CurrentSession == null) return;
        CurrentSession.PlayerJoined  += HandlePlayerJoined;
        CurrentSession.PlayerLeaving += HandlePlayerLeft;
        CurrentSession.Changed       += HandleSessionChanged;
    }

    private void UnsubscribeFromSessionEvents()
    {
        if (CurrentSession == null) return;
        CurrentSession.PlayerJoined  -= HandlePlayerJoined;
        CurrentSession.PlayerLeaving -= HandlePlayerLeft;
        CurrentSession.Changed       -= HandleSessionChanged;
    }

    private void HandlePlayerJoined(string playerId)
    {
        Debug.Log($"[NetworkGameManager] Player joined: {playerId}");
        RefreshPlayerList();
    }

    private void HandlePlayerLeft(string playerId)
    {
        Debug.Log($"[NetworkGameManager] Player left: {playerId}");
        characterSelections.Remove(playerId);
        RefreshPlayerList();
    }

    private void HandleSessionChanged()
    {
        RefreshPlayerList();
    }

    public void SetLocalPlayerName(string name)
    {
        LocalPlayerName = string.IsNullOrWhiteSpace(name) ? "Player" : name;
        _ = SyncPlayerDataAsync();
    }
}

// ── Player info struct ────────────────────────────────────────────────────────
[Serializable]
public struct SessionPlayerInfo
{
    public string PlayerUgsId;
    public string DisplayName;
    public int    CharacterIndex;
    public bool   IsReady;
    public bool   IsLocalPlayer;
    public bool   IsHost;

    public SessionPlayerInfo(string id, string name, int charIdx, bool ready, bool isLocal, bool isHost)
    {
        PlayerUgsId    = id;
        DisplayName    = name;
        CharacterIndex = charIdx;
        IsReady        = ready;
        IsLocalPlayer  = isLocal;
        IsHost         = isHost;
    }
}
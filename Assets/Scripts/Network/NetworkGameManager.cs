using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobby;
using Unity.Services.Lobby.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

/// <summary>
/// Central manager for Unity Gaming Services + Netcode for GameObjects.
///
/// Responsibilities:
///   1. Anonymous sign-in via Authentication service
///   2. Create / join a Lobby (player list, ready state, character choices)
///   3. Allocate / join a Relay server so players don't need port forwarding
///   4. Start NGO as Host or Client
///   5. Expose simple events that UI listens to
///
/// Setup:
///   - Attach to a persistent GameObject in your Main Menu scene
///   - Assign the NetworkManager reference in the Inspector
///   - Call CreateLobby() or JoinLobby() from your UI buttons
/// </summary>
public class NetworkGameManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────
    public static NetworkGameManager Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────
    [Header("References")]
    [SerializeField] private NetworkManager networkManager;

    [Header("Lobby Settings")]
    [Tooltip("Lobby name shown in the browser (overridden by host input at runtime).")]
    [SerializeField] private string defaultLobbyName = "Dungeon Run";
    [SerializeField] private int maxPlayers = 4;

    [Tooltip("How often (seconds) the host heartbeats the lobby to keep it alive.")]
    [SerializeField] private float lobbyHeartbeatInterval = 15f;

    [Tooltip("How often (seconds) all clients poll lobby data for updates.")]
    [SerializeField] private float lobbyPollInterval = 2f;

    // ── Lobby data keys ───────────────────────────────────────────────────
    // These string constants are written into Lobby custom data so all players
    // can read them without a direct RPC before NGO is started.
    private const string KEY_RELAY_CODE     = "RelayCode";
    private const string KEY_GAME_STARTED   = "GameStarted";
    // Per-player data keys
    public const string KEY_PLAYER_NAME     = "PlayerName";
    public const string KEY_CHARACTER_INDEX = "CharacterIndex";
    public const string KEY_IS_READY        = "IsReady";

    // ── Public state ──────────────────────────────────────────────────────
    public Lobby    CurrentLobby    { get; private set; }
    public Player   LocalPlayer     { get; private set; }
    public bool     IsHost          { get; private set; }
    public string   LocalPlayerId   { get; private set; }
    public string   LocalPlayerName { get; private set; } = "Player";

    // ── Events ─────────────────────────────────────────────────────────────
    public event Action                OnSignedIn;
    public event Action<string>        OnSignInFailed;
    public event Action<Lobby>         OnLobbyCreated;
    public event Action<Lobby>         OnLobbyJoined;
    public event Action<List<Player>>  OnLobbyPlayersUpdated;
    public event Action                OnGameStarting;
    public event Action<string>        OnLobbyError;
    public event Action                OnLobbyLeft;
    public event Action<string>        OnConnectionError;

    // ── Private runtime ───────────────────────────────────────────────────
    private Coroutine heartbeatCoroutine;
    private Coroutine pollCoroutine;
    private bool      servicesInitialized = false;

    // ─────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (networkManager == null)
            networkManager = FindFirstObjectByType<NetworkManager>();
    }

    private void Start()
    {
        _ = InitializeServicesAsync();
    }

    private void OnDestroy()
    {
        StopAllCoroutines();

        // Cleanly leave lobby on quit
        _ = LeaveLobbyAsync();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Initialization
    // ─────────────────────────────────────────────────────────────────────

    private async Task InitializeServicesAsync()
    {
        try
        {
            await UnityServices.InitializeAsync();
            Debug.Log("[NetworkGameManager] Unity Services initialized.");

            await SignInAnonymouslyAsync();
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkGameManager] Service init failed: {e.Message}");
            OnSignInFailed?.Invoke(e.Message);
        }
    }

    private async Task SignInAnonymouslyAsync()
    {
        if (AuthenticationService.Instance.IsSignedIn)
        {
            LocalPlayerId   = AuthenticationService.Instance.PlayerId;
            servicesInitialized = true;
            OnSignedIn?.Invoke();
            return;
        }

        await AuthenticationService.Instance.SignInAnonymouslyAsync();
        LocalPlayerId       = AuthenticationService.Instance.PlayerId;
        servicesInitialized = true;

        Debug.Log($"[NetworkGameManager] Signed in as {LocalPlayerId}");
        OnSignedIn?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Lobby — Create
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Creates a lobby and a Relay allocation, then starts NGO as Host.</summary>
    public async Task CreateLobbyAsync(string lobbyName = null)
    {
        if (!servicesInitialized)
        {
            OnLobbyError?.Invoke("Services not ready yet. Please wait.");
            return;
        }

        try
        {
            // 1. Allocate Relay
            Allocation relayAllocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(relayAllocation.AllocationId);
            Debug.Log($"[NetworkGameManager] Relay join code: {joinCode}");

            // 2. Create Lobby with relay code stored as custom data
            var lobbyOptions = new CreateLobbyOptions
            {
                IsPrivate = false,
                Player    = BuildLocalPlayer(),
                Data = new Dictionary<string, DataObject>
                {
                    { KEY_RELAY_CODE,   new DataObject(DataObject.VisibilityOptions.Member, joinCode) },
                    { KEY_GAME_STARTED, new DataObject(DataObject.VisibilityOptions.Member, "false") }
                }
            };

            string name = string.IsNullOrEmpty(lobbyName) ? defaultLobbyName : lobbyName;
            CurrentLobby = await LobbyService.Instance.CreateLobbyAsync(name, maxPlayers, lobbyOptions);
            IsHost       = true;

            Debug.Log($"[NetworkGameManager] Lobby created: {CurrentLobby.Id}");

            // 3. Start NGO as Host using Relay
            SetRelayHostData(relayAllocation);
            networkManager.StartHost();

            // 4. Start lobby maintenance
            StartLobbyCoroutines();

            OnLobbyCreated?.Invoke(CurrentLobby);
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkGameManager] CreateLobby failed: {e.Message}");
            OnLobbyError?.Invoke($"Failed to create lobby: {e.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Lobby — Join by code
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Joins a lobby by its short join code (shown in the lobby browser).</summary>
    public async Task JoinLobbyByCodeAsync(string lobbyCode)
    {
        if (!servicesInitialized)
        {
            OnLobbyError?.Invoke("Services not ready yet.");
            return;
        }

        try
        {
            var joinOptions = new JoinLobbyByCodeOptions { Player = BuildLocalPlayer() };
            CurrentLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode.ToUpper(), joinOptions);
            IsHost       = false;

            Debug.Log($"[NetworkGameManager] Joined lobby: {CurrentLobby.Id}");

            // Get relay join code from lobby data and connect via NGO
            string relayCode = CurrentLobby.Data[KEY_RELAY_CODE].Value;
            await JoinRelayAsync(relayCode);

            StartLobbyCoroutines();
            OnLobbyJoined?.Invoke(CurrentLobby);
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkGameManager] JoinLobby failed: {e.Message}");
            OnLobbyError?.Invoke($"Failed to join lobby: {e.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Lobby — Quick join (for testing)
    // ─────────────────────────────────────────────────────────────────────

    public async Task QuickJoinAsync()
    {
        if (!servicesInitialized)
        {
            OnLobbyError?.Invoke("Services not ready yet.");
            return;
        }

        try
        {
            var quickJoinOptions = new QuickJoinLobbyOptions { Player = BuildLocalPlayer() };
            CurrentLobby = await LobbyService.Instance.QuickJoinLobbyAsync(quickJoinOptions);
            IsHost       = false;

            string relayCode = CurrentLobby.Data[KEY_RELAY_CODE].Value;
            await JoinRelayAsync(relayCode);

            StartLobbyCoroutines();
            OnLobbyJoined?.Invoke(CurrentLobby);
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkGameManager] QuickJoin failed: {e.Message}");
            OnLobbyError?.Invoke($"Could not find a lobby to join.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Relay — Join as Client
    // ─────────────────────────────────────────────────────────────────────

    private async Task JoinRelayAsync(string joinCode)
    {
        JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
        SetRelayClientData(joinAllocation);
        networkManager.StartClient();
        Debug.Log("[NetworkGameManager] NGO Client started via Relay.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Game start — Host triggers this
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Host calls this once all players are ready. Loads the game scene.</summary>
    public async Task StartGameAsync()
    {
        if (!IsHost)
        {
            Debug.LogWarning("[NetworkGameManager] Only the host can start the game.");
            return;
        }

        try
        {
            // Mark game as started in lobby so late joiners know
            await LobbyService.Instance.UpdateLobbyAsync(CurrentLobby.Id, new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    { KEY_GAME_STARTED, new DataObject(DataObject.VisibilityOptions.Member, "true") }
                }
            });

            OnGameStarting?.Invoke();

            // NGO scene load — all clients load the same scene
            networkManager.SceneManager.LoadScene("GameScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkGameManager] StartGame failed: {e.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Player data — update character selection or ready state
    // ─────────────────────────────────────────────────────────────────────

    public async Task UpdatePlayerDataAsync(int characterIndex, bool isReady)
    {
        if (CurrentLobby == null) return;

        try
        {
            await LobbyService.Instance.UpdatePlayerAsync(CurrentLobby.Id, LocalPlayerId,
                new UpdatePlayerOptions
                {
                    Data = new Dictionary<string, PlayerDataObject>
                    {
                        { KEY_CHARACTER_INDEX, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, characterIndex.ToString()) },
                        { KEY_IS_READY,        new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, isReady.ToString()) },
                        { KEY_PLAYER_NAME,     new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, LocalPlayerName) }
                    }
                });
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkGameManager] UpdatePlayerData failed: {e.Message}");
        }
    }

    public void SetLocalPlayerName(string name)
    {
        LocalPlayerName = string.IsNullOrWhiteSpace(name) ? "Player" : name;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Lobby maintenance
    // ─────────────────────────────────────────────────────────────────────

    private void StartLobbyCoroutines()
    {
        StopLobbyCoroutines();

        if (IsHost)
            heartbeatCoroutine = StartCoroutine(HeartbeatLobbyRoutine());

        pollCoroutine = StartCoroutine(PollLobbyRoutine());
    }

    private void StopLobbyCoroutines()
    {
        if (heartbeatCoroutine != null) StopCoroutine(heartbeatCoroutine);
        if (pollCoroutine      != null) StopCoroutine(pollCoroutine);
    }

    private IEnumerator HeartbeatLobbyRoutine()
    {
        var wait = new WaitForSeconds(lobbyHeartbeatInterval);
        while (true)
        {
            yield return wait;
            if (CurrentLobby == null) yield break;
            _ = LobbyService.Instance.SendHeartbeatPingAsync(CurrentLobby.Id);
        }
    }

    private IEnumerator PollLobbyRoutine()
    {
        var wait = new WaitForSeconds(lobbyPollInterval);
        while (true)
        {
            yield return wait;
            if (CurrentLobby == null) yield break;
            _ = PollLobbyAsync();
        }
    }

    private async Task PollLobbyAsync()
    {
        try
        {
            CurrentLobby = await LobbyService.Instance.GetLobbyAsync(CurrentLobby.Id);
            OnLobbyPlayersUpdated?.Invoke(CurrentLobby.Players);

            // Check if host started the game (non-host clients pick this up here)
            if (!IsHost && CurrentLobby.Data.TryGetValue(KEY_GAME_STARTED, out var started))
            {
                if (started.Value == "true")
                {
                    StopLobbyCoroutines();
                    OnGameStarting?.Invoke();
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[NetworkGameManager] Poll failed: {e.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Leave lobby
    // ─────────────────────────────────────────────────────────────────────

    public async Task LeaveLobbyAsync()
    {
        if (CurrentLobby == null) return;

        StopLobbyCoroutines();

        try
        {
            if (IsHost)
                await LobbyService.Instance.DeleteLobbyAsync(CurrentLobby.Id);
            else
                await LobbyService.Instance.RemovePlayerAsync(CurrentLobby.Id, LocalPlayerId);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[NetworkGameManager] LeaveLobby error: {e.Message}");
        }

        CurrentLobby = null;
        networkManager?.Shutdown();
        OnLobbyLeft?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers — Relay transport setup
    // ─────────────────────────────────────────────────────────────────────

    private void SetRelayHostData(Allocation allocation)
    {
        var relayServerData = new RelayServerData(allocation, "dtls");
        networkManager.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);
    }

    private void SetRelayClientData(JoinAllocation allocation)
    {
        var relayServerData = new RelayServerData(allocation, "dtls");
        networkManager.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);
    }

    private Player BuildLocalPlayer()
    {
        return new Player
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                { KEY_PLAYER_NAME,     new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, LocalPlayerName) },
                { KEY_CHARACTER_INDEX, new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, "0") },
                { KEY_IS_READY,        new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, "false") }
            }
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    // Utility — read lobby player list into a friendlier struct
    // ─────────────────────────────────────────────────────────────────────

    public List<LobbyPlayerInfo> GetLobbyPlayerInfos()
    {
        var list = new List<LobbyPlayerInfo>();
        if (CurrentLobby == null) return list;

        foreach (Player p in CurrentLobby.Players)
        {
            string name    = p.Data.TryGetValue(KEY_PLAYER_NAME,     out var n) ? n.Value : "Player";
            int    charIdx = p.Data.TryGetValue(KEY_CHARACTER_INDEX,  out var c) ? int.Parse(c.Value) : 0;
            bool   ready   = p.Data.TryGetValue(KEY_IS_READY,         out var r) && r.Value == "True";
            bool   isLocal = p.Id == LocalPlayerId;

            list.Add(new LobbyPlayerInfo(p.Id, name, charIdx, ready, isLocal, p.Id == CurrentLobby.HostId));
        }

        return list;
    }

    public bool AllPlayersReady()
    {
        if (CurrentLobby == null) return false;
        foreach (Player p in CurrentLobby.Players)
        {
            if (!p.Data.TryGetValue(KEY_IS_READY, out var r) || r.Value != "True")
                return false;
        }
        return CurrentLobby.Players.Count >= 1; // at least 1 player needed
    }

    /// <summary>Returns the character index chosen by each player, keyed by their UGS Player ID.</summary>
    public Dictionary<string, int> GetCharacterSelections()
    {
        var dict = new Dictionary<string, int>();
        if (CurrentLobby == null) return dict;
        foreach (Player p in CurrentLobby.Players)
        {
            int idx = p.Data.TryGetValue(KEY_CHARACTER_INDEX, out var c) ? int.Parse(c.Value) : 0;
            dict[p.Id] = idx;
        }
        return dict;
    }
}

// ── Simple data struct ─────────────────────────────────────────────────────────
[Serializable]
public struct LobbyPlayerInfo
{
    public string PlayerUgsId;
    public string DisplayName;
    public int    CharacterIndex;
    public bool   IsReady;
    public bool   IsLocalPlayer;
    public bool   IsHost;

    public LobbyPlayerInfo(string id, string name, int charIdx, bool ready, bool isLocal, bool isHost)
    {
        PlayerUgsId    = id;
        DisplayName    = name;
        CharacterIndex = charIdx;
        IsReady        = ready;
        IsLocalPlayer  = isLocal;
        IsHost         = isHost;
    }
}
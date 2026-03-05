using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lobby room UI — shown after creating or joining a lobby, before the game starts.
///
/// SCENE SETUP:
///   LobbyPanel
///     - LobbyCodeText        (shows the lobby ID so players can share it)
///     - PlayerListContainer  (vertical layout group, populated at runtime)
///     - PlayerSlotPrefab     (prefab with PlayerSlotUI component)
///     - CharacterSelectPanel
///         - CharacterButton0..3 (one per character)
///     - ReadyButton
///     - StartButton          (host only — visible only when all players ready)
///     - LeaveLobbyButton
///
/// Wire all references in Inspector.
/// </summary>
public class LobbyRoomUI : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────
    [Header("Lobby Info")]
    [SerializeField] private TextMeshProUGUI lobbyCodeText;
    [SerializeField] private TextMeshProUGUI lobbyStatusText;

    [Header("Player List")]
    [SerializeField] private Transform      playerListContainer;
    [SerializeField] private GameObject     playerSlotPrefab;   // must have PlayerSlotUI component

    [Header("Character Select")]
    [SerializeField] private List<Button>   characterButtons;   // index = character index
    [SerializeField] private List<Image>    characterButtonHighlights; // shown on selected char

    [Header("Buttons")]
    [SerializeField] private Button         readyButton;
    [SerializeField] private Button         startButton;        // host only
    [SerializeField] private Button         leaveLobbyButton;

    [Header("Character Names (for display)")]
    [SerializeField] private List<string>   characterNames = new List<string>
        { "Knight", "Rogue", "Mage", "Cleric" };

    // ── Private runtime ───────────────────────────────────────────────────
    private int              selectedCharacterIndex = 0;
    private bool             isReady               = false;
    private List<GameObject> playerSlots           = new List<GameObject>();

    // ─────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        readyButton     ?.onClick.AddListener(OnReadyClicked);
        startButton     ?.onClick.AddListener(OnStartClicked);
        leaveLobbyButton?.onClick.AddListener(OnLeaveClicked);

        for (int i = 0; i < characterButtons.Count; i++)
        {
            int idx = i; // capture for lambda
            characterButtons[i]?.onClick.AddListener(() => SelectCharacter(idx));
        }
    }

    private void OnEnable()
    {
        if (NetworkGameManager.Instance == null) return;

        NetworkGameManager.Instance.OnLobbyPlayersUpdated += HandlePlayersUpdated;
        NetworkGameManager.Instance.OnGameStarting        += HandleGameStarting;
        NetworkGameManager.Instance.OnLobbyLeft           += HandleLobbyLeft;

        RefreshUI();
    }

    private void OnDisable()
    {
        if (NetworkGameManager.Instance == null) return;

        NetworkGameManager.Instance.OnLobbyPlayersUpdated -= HandlePlayersUpdated;
        NetworkGameManager.Instance.OnGameStarting        -= HandleGameStarting;
        NetworkGameManager.Instance.OnLobbyLeft           -= HandleLobbyLeft;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Refresh
    // ─────────────────────────────────────────────────────────────────────

    private void RefreshUI()
    {
        if (NetworkGameManager.Instance?.CurrentLobby == null) return;

        var lobby = NetworkGameManager.Instance.CurrentLobby;

        // Show lobby code
        if (lobbyCodeText != null)
            lobbyCodeText.text = $"Lobby Code: {lobby.LobbyCode}";

        // Host controls
        bool isHost = NetworkGameManager.Instance.IsHost;
        if (startButton != null)
            startButton.gameObject.SetActive(isHost);

        RefreshPlayerList();
        RefreshCharacterHighlights();
        RefreshStartButton();
    }

    private void RefreshPlayerList()
    {
        // Clear old slots
        foreach (var slot in playerSlots)
            Destroy(slot);
        playerSlots.Clear();

        if (playerSlotPrefab == null || playerListContainer == null) return;

        List<LobbyPlayerInfo> players = NetworkGameManager.Instance.GetLobbyPlayerInfos();

        foreach (var info in players)
        {
            GameObject slotGO = Instantiate(playerSlotPrefab, playerListContainer);
            PlayerSlotUI slotUI = slotGO.GetComponent<PlayerSlotUI>();

            if (slotUI != null)
                slotUI.SetData(info, GetCharacterName(info.CharacterIndex));

            playerSlots.Add(slotGO);
        }

        // Update status text
        if (lobbyStatusText != null)
        {
            int maxPlayers = NetworkGameManager.Instance.CurrentLobby.MaxPlayers;
            lobbyStatusText.text = $"{players.Count}/{maxPlayers} players";
        }
    }

    private void RefreshCharacterHighlights()
    {
        for (int i = 0; i < characterButtonHighlights.Count; i++)
        {
            if (characterButtonHighlights[i] != null)
                characterButtonHighlights[i].enabled = (i == selectedCharacterIndex);
        }
    }

    private void RefreshStartButton()
    {
        if (startButton == null || !NetworkGameManager.Instance.IsHost) return;

        bool allReady = NetworkGameManager.Instance.AllPlayersReady();
        startButton.interactable = allReady;

        TextMeshProUGUI startText = startButton.GetComponentInChildren<TextMeshProUGUI>();
        if (startText != null)
            startText.text = allReady ? "Start Game!" : "Waiting for players...";
    }

    // ─────────────────────────────────────────────────────────────────────
    // Button handlers
    // ─────────────────────────────────────────────────────────────────────

    private void SelectCharacter(int index)
    {
        selectedCharacterIndex = index;
        RefreshCharacterHighlights();

        // Update lobby data so other players see our selection
        _ = NetworkGameManager.Instance?.UpdatePlayerDataAsync(selectedCharacterIndex, isReady);
    }

    private void OnReadyClicked()
    {
        isReady = !isReady;

        // Update button text
        TextMeshProUGUI btnText = readyButton?.GetComponentInChildren<TextMeshProUGUI>();
        if (btnText != null)
            btnText.text = isReady ? "Not Ready" : "Ready!";

        // Update lobby
        _ = NetworkGameManager.Instance?.UpdatePlayerDataAsync(selectedCharacterIndex, isReady);
    }

    private void OnStartClicked()
    {
        if (!NetworkGameManager.Instance.IsHost) return;
        if (!NetworkGameManager.Instance.AllPlayersReady())
        {
            Debug.Log("[LobbyRoomUI] Not all players are ready yet.");
            return;
        }
        _ = NetworkGameManager.Instance.StartGameAsync();
    }

    private void OnLeaveClicked()
    {
        _ = NetworkGameManager.Instance?.LeaveLobbyAsync();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Event handlers
    // ─────────────────────────────────────────────────────────────────────

    private void HandlePlayersUpdated(List<Unity.Services.Lobby.Models.Player> players)
    {
        RefreshPlayerList();
        RefreshStartButton();
    }

    private void HandleGameStarting()
    {
        Debug.Log("[LobbyRoomUI] Game is starting — hiding lobby UI.");
        gameObject.SetActive(false);
    }

    private void HandleLobbyLeft()
    {
        // Return to main menu
        gameObject.SetActive(false);
        // You may want to load the main menu scene here
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private string GetCharacterName(int index)
    {
        if (index >= 0 && index < characterNames.Count)
            return characterNames[index];
        return "Unknown";
    }
}
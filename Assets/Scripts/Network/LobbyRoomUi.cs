using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lobby room panel — activated automatically when a session becomes active.
///
/// The Multiplayer Widgets handle create/join/leave — this script handles:
///   - Character selection with visual highlights
///   - Ready toggle
///   - Player list display (uses LobbyPlayerEntry prefab)
///   - Start Game button (host only, enabled when local player is ready)
///
/// SCENE HIERARCHY (inside LobbyPanel GameObject):
///   [Widget] Show Session Code         ← join code display, auto-updated by widget
///   PlayerCountText (TMP)              ← "2 / 4 players"
///   PlayerListContainer                ← Vertical Layout Group, holds LobbyPlayerEntry prefabs
///   CharacterSelectPanel
///       CharacterButton0..3            ← one Button per class
///       CharacterHighlight0..3         ← one Image per class (enabled = selected)
///   ReadyButton
///   StartButton                        ← host only
///   [Widget] Leave Session
/// </summary>
public class LobbyRoomUI : MonoBehaviour
{
    [Header("Player List")]
    [SerializeField] private Transform           playerListContainer;
    [SerializeField] private GameObject          playerEntryPrefab;     // has LobbyPlayerEntry component
    [SerializeField] private TextMeshProUGUI     playerCountText;

    [Header("Character Select")]
    [SerializeField] private List<Button>        characterButtons;       // one per class
    [SerializeField] private List<Image>         characterHighlights;    // one Image per class

    [Header("Buttons")]
    [SerializeField] private Button              readyButton;
    [SerializeField] private Button              startButton;            // host only

    private int  selectedCharacterIndex = 0;
    private bool isReady               = false;

    // ─────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Wire character buttons
        for (int i = 0; i < characterButtons.Count; i++)
        {
            int idx = i;
            characterButtons[i]?.onClick.AddListener(() => SelectCharacter(idx));
        }

        readyButton ?.onClick.AddListener(OnReadyClicked);
        startButton ?.onClick.AddListener(OnStartClicked);

        // Hidden until session is active
        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (NetworkGameManager.Instance == null) return;
        NetworkGameManager.Instance.OnSessionCreated += HandleSessionActive;
        NetworkGameManager.Instance.OnSessionJoined  += HandleSessionActive;
        NetworkGameManager.Instance.OnSessionLeft    += HandleSessionEnded;
        NetworkGameManager.Instance.OnPlayersUpdated += HandlePlayersUpdated;
        NetworkGameManager.Instance.OnGameStarting   += HandleGameStarting;
    }

    private void OnDisable()
    {
        if (NetworkGameManager.Instance == null) return;
        NetworkGameManager.Instance.OnSessionCreated -= HandleSessionActive;
        NetworkGameManager.Instance.OnSessionJoined  -= HandleSessionActive;
        NetworkGameManager.Instance.OnSessionLeft    -= HandleSessionEnded;
        NetworkGameManager.Instance.OnPlayersUpdated -= HandlePlayersUpdated;
        NetworkGameManager.Instance.OnGameStarting   -= HandleGameStarting;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Session events
    // ─────────────────────────────────────────────────────────────────────

    private void HandleSessionActive()
    {
        gameObject.SetActive(true);
        startButton?.gameObject.SetActive(NetworkGameManager.Instance.IsHost);
        RefreshCharacterHighlights();
        RefreshStartButton();
    }

    private void HandleSessionEnded()
    {
        isReady = false;
        UpdateReadyButtonText();
        gameObject.SetActive(false);
    }

    private void HandleGameStarting()
    {
        gameObject.SetActive(false);
    }

    private void HandlePlayersUpdated(List<SessionPlayerInfo> players)
    {
        // Rebuild player list
        if (playerListContainer != null)
            foreach (Transform child in playerListContainer)
                Destroy(child.gameObject);

        if (playerEntryPrefab != null && playerListContainer != null)
            foreach (var player in players)
            {
                var entry = Instantiate(playerEntryPrefab, playerListContainer);
                entry.GetComponent<LobbyPlayerEntry>()?.Setup(player);
            }

        if (playerCountText != null)
            playerCountText.text = $"{players.Count} / {NetworkGameManager.Instance?.GetMaxPlayers() ?? 4} players";

        RefreshStartButton();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Character select
    // ─────────────────────────────────────────────────────────────────────

    private void SelectCharacter(int index)
    {
        selectedCharacterIndex = index;
        NetworkGameManager.Instance?.SetLocalCharacterSelection(index);
        RefreshCharacterHighlights();
    }

    private void RefreshCharacterHighlights()
    {
        for (int i = 0; i < characterHighlights.Count; i++)
            if (characterHighlights[i] != null)
                characterHighlights[i].enabled = (i == selectedCharacterIndex);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Ready & Start
    // ─────────────────────────────────────────────────────────────────────

    private void OnReadyClicked()
    {
        isReady = !isReady;
        NetworkGameManager.Instance?.SetLocalReadyState(isReady);
        UpdateReadyButtonText();
        RefreshStartButton();
    }

    private void UpdateReadyButtonText()
    {
        var txt = readyButton?.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null) txt.text = isReady ? "Not Ready" : "Ready!";
    }

    private void OnStartClicked()
    {
        if (NetworkGameManager.Instance == null || !NetworkGameManager.Instance.IsHost) return;
        if (!NetworkGameManager.Instance.AllPlayersReady()) return;
        NetworkGameManager.Instance.StartGame();
    }

    private void RefreshStartButton()
    {
        if (startButton == null || NetworkGameManager.Instance == null) return;
        if (!NetworkGameManager.Instance.IsHost) return;

        bool allReady = NetworkGameManager.Instance.AllPlayersReady();
        startButton.interactable = allReady;

        var txt = startButton.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null)
        {
            txt.text  = allReady ? "START ADVENTURE" : "WAITING FOR TEAM...";
            txt.color = allReady ? Color.white : new Color(1f, 1f, 1f, 0.5f);
        }
    }
}
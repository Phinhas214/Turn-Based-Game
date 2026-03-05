using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Main Menu Controller — handles all panel flow + multiplayer lobby.
///
/// LOBBY PANEL HIERARCHY:
///   LobbyPanel
///     ShowSessionCode          ← Widget (no wiring needed)
///     PlayerListContainer      ← Transform with Vertical Layout Group
///     CharacterSelectPanel
///       KnightButton           ← Button with Image child (your sprite)
///       RogueButton            ← Button with Image child
///       MageButton             ← Button with Image child
///       ClericButton           ← Button with Image child
///     SelectedCharacterName    ← TextMeshProUGUI ("Knight", "Rogue" etc)
///     ReadyButton
///     StartButton              ← host only, hidden for clients
///     LeaveLobbyButton
///
/// CHARACTER SELECTION:
///   Each character button highlights when selected (colored border / tint).
///   Calling SetLocalCharacterSelection(0-3) feeds into NetworkGameManager
///   which syncs it to the session so NetworkedLevelGenerator picks the right prefab.
///   Index order MUST match NetworkedLevelGenerator.playerPrefabs:
///     0 = Knight, 1 = Rogue, 2 = Mage, 3 = Cleric
/// </summary>
public class MainMenuController : MonoBehaviour
{
    // ── Panels ────────────────────────────────────────────────────────────
    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject modePanel;
    [SerializeField] private GameObject multiplayerSelectPanel;
    [SerializeField] private GameObject hostPanel;
    [SerializeField] private GameObject joinPanel;
    [SerializeField] private GameObject joinByCodePanel;
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private GameObject loadingPanel;

    // ── Main & Mode buttons ───────────────────────────────────────────────
    [Header("Buttons: Main & Mode")]
    [SerializeField] private Button newGameButton;
    [SerializeField] private Button multiplayerButton;
    [SerializeField] private Button backToMainButton;
    [SerializeField] private Button startSinglePlayerButton;
    [SerializeField] private string gameSceneName = "SinglePlayer";

    // ── Multiplayer select ────────────────────────────────────────────────
    [Header("Buttons: Multiplayer Selection")]
    [SerializeField] private Button         selectHostButton;
    [SerializeField] private Button         selectJoinButton;
    [SerializeField] private TMP_InputField playerNameInput;
    [SerializeField] private Button         backToModeButton;

    // ── Host / Join sub-panels ────────────────────────────────────────────
    [Header("Buttons: Host/Join Sub-Panels")]
    [SerializeField] private Button joinByCodeButton;
    [SerializeField] private Button backFromHostButton;
    [SerializeField] private Button backFromJoinButton;
    [SerializeField] private Button backFromCodeButton;

    // ── Lobby ─────────────────────────────────────────────────────────────
    [Header("Lobby Panel")]
    [SerializeField] private Transform  playerListContainer;
    [SerializeField] private GameObject playerSlotPrefab;
    [SerializeField] private Button     readyButton;
    [SerializeField] private Button     startButton;
    [SerializeField] private Button     leaveLobbyButton;

    // ── Character Selection ───────────────────────────────────────────────
    [Header("Character Selection")]
    [Tooltip("Four buttons in order: Knight(0), Rogue(1), Mage(2), Cleric(3)")]
    [SerializeField] private List<Button> characterButtons;

    [Tooltip("Sprites shown on each character button — same order as above")]
    [SerializeField] private List<Sprite> characterSprites;

    [Tooltip("Name shown below the selected character portrait")]
    [SerializeField] private TextMeshProUGUI selectedCharacterNameText;

    [Tooltip("Color applied to the selected button to highlight it")]
    [SerializeField] private Color selectedColor   = new Color(1f, 0.85f, 0.2f, 1f);  // gold
    [SerializeField] private Color deselectedColor = new Color(1f, 1f, 1f, 0.45f);     // dim

    private static readonly string[] CharacterNames = { "Knight", "Rogue", "Mage", "Cleric" };
    private int selectedCharacterIndex = 0;

    // ── Runtime ───────────────────────────────────────────────────────────
    private bool isReady = false;

    // ─────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Main / Mode
        newGameButton           ?.onClick.AddListener(() => ShowPanel(modePanel));
        multiplayerButton       ?.onClick.AddListener(() => ShowPanel(multiplayerSelectPanel));
        startSinglePlayerButton ?.onClick.AddListener(StartSinglePlayerGame);

        // Multiplayer select
        selectHostButton?.onClick.AddListener(() => ShowPanel(hostPanel));
        selectJoinButton?.onClick.AddListener(() => ShowPanel(joinPanel));
        playerNameInput ?.onEndEdit.AddListener(OnPlayerNameChanged);

        // Sub-panel navigation
        joinByCodeButton  ?.onClick.AddListener(() => ShowPanel(joinByCodePanel));
        backToMainButton  ?.onClick.AddListener(() => ShowPanel(mainMenuPanel));
        backToModeButton  ?.onClick.AddListener(() => ShowPanel(modePanel));
        backFromHostButton?.onClick.AddListener(() => ShowPanel(multiplayerSelectPanel));
        backFromJoinButton?.onClick.AddListener(() => ShowPanel(multiplayerSelectPanel));
        backFromCodeButton?.onClick.AddListener(() => ShowPanel(joinPanel));

        // Lobby buttons
        readyButton     ?.onClick.AddListener(OnReadyClicked);
        startButton     ?.onClick.AddListener(OnStartGameClicked);
        leaveLobbyButton?.onClick.AddListener(OnLeaveLobbyClicked);

        // Character select buttons
        for (int i = 0; i < characterButtons.Count; i++)
        {
            int index = i; // capture for lambda
            characterButtons[i]?.onClick.AddListener(() => SelectCharacter(index));

            // Apply sprite if provided
            if (i < characterSprites.Count && characterSprites[i] != null)
            {
                var img = characterButtons[i]?.GetComponentInChildren<Image>();
                if (img != null) img.sprite = characterSprites[i];
            }
        }
    }

    private void OnEnable()
    {
        if (NetworkGameManager.Instance == null) return;
        NetworkGameManager.Instance.OnSessionCreated += HandleSessionActive;
        NetworkGameManager.Instance.OnSessionJoined  += HandleSessionActive;
        NetworkGameManager.Instance.OnSessionLeft    += HandleSessionLeft;
        NetworkGameManager.Instance.OnPlayersUpdated += HandlePlayersUpdated;
    }

    private void OnDisable()
    {
        if (NetworkGameManager.Instance == null) return;
        NetworkGameManager.Instance.OnSessionCreated -= HandleSessionActive;
        NetworkGameManager.Instance.OnSessionJoined  -= HandleSessionActive;
        NetworkGameManager.Instance.OnSessionLeft    -= HandleSessionLeft;
        NetworkGameManager.Instance.OnPlayersUpdated -= HandlePlayersUpdated;
    }

    private void Start()
    {
        string savedName = PlayerPrefs.GetString("PlayerName", "Player" + Random.Range(100, 999));
        if (playerNameInput != null) playerNameInput.text = savedName;

        // Default to Knight selected
        RefreshCharacterButtons();

        ShowPanel(mainMenuPanel);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Single Player
    // ─────────────────────────────────────────────────────────────────────

    private void StartSinglePlayerGame()
    {
        loadingPanel?.SetActive(true);
        SceneManager.LoadScene(2);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Player name
    // ─────────────────────────────────────────────────────────────────────

    private void OnPlayerNameChanged(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        NetworkGameManager.Instance?.SetLocalPlayerName(newName);
        PlayerPrefs.SetString("PlayerName", newName);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Character selection
    // ─────────────────────────────────────────────────────────────────────

    private void SelectCharacter(int index)
    {
        selectedCharacterIndex = index;
        NetworkGameManager.Instance?.SetLocalCharacterSelection(index);
        RefreshCharacterButtons();
    }

    private void RefreshCharacterButtons()
    {
        for (int i = 0; i < characterButtons.Count; i++)
        {
            if (characterButtons[i] == null) continue;

            bool isSelected = (i == selectedCharacterIndex);

            // Tint the whole button
            var btn = characterButtons[i].GetComponent<Image>();
            if (btn != null) btn.color = isSelected ? selectedColor : deselectedColor;

            // Scale up the selected button slightly
            characterButtons[i].transform.localScale = isSelected
                ? new Vector3(1.1f, 1.1f, 1f)
                : Vector3.one;
        }

        // Update name label
        if (selectedCharacterNameText != null)
            selectedCharacterNameText.text = selectedCharacterIndex < CharacterNames.Length
                ? CharacterNames[selectedCharacterIndex]
                : "";
    }

    // ─────────────────────────────────────────────────────────────────────
    // Lobby — Ready & Start
    // ─────────────────────────────────────────────────────────────────────

    public void OnReadyClicked()
    {
        isReady = !isReady;
        NetworkGameManager.Instance?.SetLocalReadyState(isReady);

        var txt = readyButton?.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null) txt.text = isReady ? "Not Ready" : "Ready";

        // Tint ready button to give clear feedback
        var img = readyButton?.GetComponent<Image>();
        if (img != null) img.color = isReady
            ? new Color(0.2f, 0.85f, 0.3f, 1f)   // green when ready
            : Color.white;
    }

    public void OnStartGameClicked()
    {
        if (NetworkGameManager.Instance == null || !NetworkGameManager.Instance.IsHost) return;
        if (!NetworkGameManager.Instance.AllPlayersReady())
        {
            Debug.Log("[MainMenuController] Not all players are ready.");
            return;
        }
        NetworkGameManager.Instance.StartGame();
    }

    public void OnLeaveLobbyClicked()
    {
        isReady = false;
        selectedCharacterIndex = 0;
        _ = NetworkGameManager.Instance?.LeaveSessionAsync();
    }

    // ─────────────────────────────────────────────────────────────────────
    // NetworkGameManager events
    // ─────────────────────────────────────────────────────────────────────

    private void HandleSessionActive()
    {
        isReady = false;
        selectedCharacterIndex = 0;

        ShowPanel(lobbyPanel);
        RefreshCharacterButtons();

        // Reset ready button appearance
        var txt = readyButton?.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null) txt.text = "Ready";
        var img = readyButton?.GetComponent<Image>();
        if (img != null) img.color = Color.white;

        // Start button — host only
        bool isHost = NetworkGameManager.Instance?.IsHost ?? false;
        startButton?.gameObject.SetActive(isHost);
        if (startButton != null) startButton.interactable = false; // disabled until all ready
    }

    private void HandleSessionLeft()
    {
        isReady = false;
        ShowPanel(mainMenuPanel);
    }

    private void HandlePlayersUpdated(List<SessionPlayerInfo> players)
    {
        // Rebuild player list
        if (playerListContainer != null)
        {
            foreach (Transform child in playerListContainer)
                Destroy(child.gameObject);

            if (playerSlotPrefab != null)
                foreach (var player in players)
                {
                    var entry = Instantiate(playerSlotPrefab, playerListContainer);
                    entry.GetComponent<LobbyPlayerEntry>()?.Setup(player);
                }
        }

        // Host: enable Start only when all players are ready
        if (startButton != null && (NetworkGameManager.Instance?.IsHost ?? false))
        {
            bool allReady = NetworkGameManager.Instance.AllPlayersReady();
            startButton.interactable = allReady;

            var txt = startButton.GetComponentInChildren<TextMeshProUGUI>();
            if (txt != null)
            {
                txt.text  = allReady ? "Start Game!" : "Waiting for players...";
                txt.color = allReady ? Color.white : new Color(1f, 1f, 1f, 0.4f);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Panel helper
    // ─────────────────────────────────────────────────────────────────────

    private void ShowPanel(GameObject target)
    {
        mainMenuPanel          ?.SetActive(false);
        modePanel              ?.SetActive(false);
        multiplayerSelectPanel ?.SetActive(false);
        hostPanel              ?.SetActive(false);
        joinPanel              ?.SetActive(false);
        joinByCodePanel        ?.SetActive(false);
        lobbyPanel             ?.SetActive(false);

        target?.SetActive(true);
    }
}
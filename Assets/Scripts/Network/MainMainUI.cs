using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// FULL FLOW:
///   MainMenuPanel → ModePanel
///     → SinglePlayer → loads game
///     → Multiplayer  → HostPanel (Create Session widget)   ─┐
///                    → JoinPanel (Session List widget)      ├→ WaitingLobbyContent appears
///                    → JoinByCodePanel (Join By Code widget)┘
///
///   WaitingLobbyContent (all players wait here, see each other's names)
///     Host sees "Begin Character Select" button
///     → host clicks it → CharacterSelectContent appears for everyone
///
///   CharacterSelectContent
///     All players pick a character (Knight/Rogue/Mage/Cleric) and click Ready
///     Host sees Start button when all are ready
///     → Start → loads multiplayer scene, NetworkedLevelGenerator spawns correct prefabs
///
/// KEY DESIGN:
///   WaitingLobbyPanel and CharacterSelectPanel are ALWAYS ACTIVE GameObjects.
///   Their child "Content" objects start INACTIVE and get shown/hidden.
///   This ensures Awake() runs and events get subscribed properly.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    // ── Navigation Panels ─────────────────────────────────────────────────
    [Header("Navigation Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject modePanel;
    [SerializeField] private GameObject multiplayerPanel; // has BOTH Create Session + Join By Code widgets
    [SerializeField] private GameObject loadingPanel;

    // ── Navigation Buttons ────────────────────────────────────────────────
    [Header("Navigation Buttons")]
    [SerializeField] private Button newGameButton;
    [SerializeField] private Button multiplayerButton;
    [SerializeField] private Button startSinglePlayerButton;
    [SerializeField] private Button backToMainButton;
    [SerializeField] private Button backToModeButton;

    [Header("Player Name")]
    [SerializeField] private TMP_InputField playerNameInput;

    [Header("Enter Lobby")]
    [SerializeField] private Button enterLobbyButton; // everyone clicks this after connecting



    // ── Phase 1: Waiting Lobby ────────────────────────────────────────────
    // WaitingLobbyPanel = always active parent
    // WaitingLobbyContent = inactive at start, shown when session is active
    [Header("Phase 1 — Waiting Lobby")]
    [SerializeField] private GameObject      waitingLobbyPanel;    // always active
    [SerializeField] private GameObject      waitingLobbyContent;  // inactive at start

    [SerializeField] private TextMeshProUGUI waitingPlayerCount;   // "2 / 4 players"
    [SerializeField] private Transform       waitingPlayerList;    // spawns PlayerSlot prefabs
    [SerializeField] private GameObject      playerSlotPrefab;     // your Slot prefab
    [SerializeField] private Button          beginCharSelectButton; // host only
    [SerializeField] private Button          waitingLeaveButton;

    // ── Phase 2: Character Select ─────────────────────────────────────────
    [Header("Phase 2 — Character Select")]
    [SerializeField] private GameObject      characterSelectPanel;
    [SerializeField] private GameObject      characterSelectContent;
    [SerializeField] private Transform       charSelectPlayerList;
    [SerializeField] private List<Button> characterButtons;  // assign buttons in order
    [SerializeField] private List<string> characterNames;   // type names to match each button e.g. "SmokeStack"
    [SerializeField] private List<Sprite> characterSprites; // portraits in same order
    [SerializeField] private TextMeshProUGUI selectedCharacterName;
    [SerializeField] private Color           selectedTint   = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField] private Color           deselectedTint = new Color(1f, 1f, 1f, 0.4f);
    [SerializeField] private Button          readyButton;
    [SerializeField] private Button          startButton;
    [SerializeField] private Button          charSelectLeaveButton;

    // ── Runtime state ─────────────────────────────────────────────────────
    private int  selectedCharIndex  = 0;
    private bool isReady            = false;
    private bool inCharSelectPhase  = false;
    private bool isSinglePlayer     = false;

    // ─────────────────────────────────────────────────────────────────────
    // Awake — wire all buttons
    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Navigation
        newGameButton          ?.onClick.AddListener(() => ShowNavPanel(modePanel));
        multiplayerButton      ?.onClick.AddListener(() => ShowNavPanel(multiplayerPanel));
        startSinglePlayerButton?.onClick.AddListener(GoToSinglePlayerCharSelect);
        backToMainButton       ?.onClick.AddListener(() => ShowNavPanel(mainMenuPanel));
        backToModeButton       ?.onClick.AddListener(() => ShowNavPanel(modePanel));

        playerNameInput?.onEndEdit.AddListener(OnPlayerNameChanged);
        enterLobbyButton?.onClick.AddListener(OnEnterLobbyClicked);

        // Waiting lobby
        beginCharSelectButton?.onClick.AddListener(OnBeginCharSelectClicked);
        waitingLeaveButton   ?.onClick.AddListener(OnLeaveClicked);

        // Character select buttons
        for (int i = 0; i < characterButtons.Count; i++)
        {
            int idx = i;
            characterButtons[i]?.onClick.AddListener(() => SelectCharacter(idx));

            if (i < characterSprites.Count && characterSprites[i] != null)
            {
                var imgs = characterButtons[i]?.GetComponentsInChildren<Image>();
                if (imgs != null && imgs.Length > 1)
                    imgs[1].sprite = characterSprites[i];
            }
        }

        readyButton         ?.onClick.AddListener(OnReadyClicked);
        startButton         ?.onClick.AddListener(OnStartClicked);
        charSelectLeaveButton?.onClick.AddListener(OnLeaveClicked);

        // Hide content at startup
        waitingLobbyContent   ?.SetActive(false);
        characterSelectContent?.SetActive(false);
    }

    private void OnDestroy()
    {
        if (NetworkGameManager.Instance == null) return;
        NetworkGameManager.Instance.OnSessionCreated -= HandleSessionActive;
        NetworkGameManager.Instance.OnSessionJoined  -= HandleSessionActive;
        NetworkGameManager.Instance.OnSessionLeft    -= HandleSessionLeft;
        NetworkGameManager.Instance.OnPlayersUpdated -= HandlePlayersUpdated;
        NetworkGameManager.Instance.OnGameStarting   -= HandleGameStarting;
    }

    private void Start()
    {
        string saved = PlayerPrefs.GetString("PlayerName", "Player" + Random.Range(100, 999));
        if (playerNameInput != null) playerNameInput.text = saved;

        // Force clean state regardless of how the scene was saved in the editor
        waitingLobbyPanel     ?.SetActive(false);
        waitingLobbyContent   ?.SetActive(false);
        characterSelectPanel  ?.SetActive(false);
        characterSelectContent?.SetActive(false);
        loadingPanel          ?.SetActive(false);
        startButton           ?.gameObject.SetActive(false);
        beginCharSelectButton ?.gameObject.SetActive(false);
        enterLobbyButton      ?.gameObject.SetActive(false);

        RefreshCharacterButtons();
        ShowNavPanel(mainMenuPanel);

        // Start a coroutine to wait for NetworkGameManager to be ready then subscribe
        StartCoroutine(SubscribeWhenReady());
    }

    private System.Collections.IEnumerator SubscribeWhenReady()
    {
        // Wait until NetworkGameManager exists — it might initialize after us
        while (NetworkGameManager.Instance == null)
            yield return null;

        NetworkGameManager.Instance.OnSessionCreated += HandleSessionActive;
        NetworkGameManager.Instance.OnSessionJoined  += HandleSessionActive;
        NetworkGameManager.Instance.OnSessionLeft    += HandleSessionLeft;
        NetworkGameManager.Instance.OnPlayersUpdated += HandlePlayersUpdated;
        NetworkGameManager.Instance.OnGameStarting   += HandleGameStarting;

        Debug.Log("[MainMenuController] Subscribed to NetworkGameManager events.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Navigation helpers
    // ─────────────────────────────────────────────────────────────────────

    private void ShowNavPanel(GameObject target)
    {
        mainMenuPanel   ?.SetActive(false);
        modePanel       ?.SetActive(false);
        multiplayerPanel?.SetActive(false);
        target          ?.SetActive(true);
    }

    private void HideAllNavPanels()
    {
        mainMenuPanel   ?.SetActive(false);
        modePanel       ?.SetActive(false);
        multiplayerPanel?.SetActive(false);
        waitingLobbyPanel    ?.SetActive(false);
        characterSelectPanel ?.SetActive(false);
    }

    public void GoToSinglePlayerCharSelect()
    {
        isSinglePlayer = true;
        SwitchToCharSelectPhase();
    }

    private void StartSinglePlayer()
    {
        CharacterSelection.Index = selectedCharIndex;
        loadingPanel?.SetActive(true);
        SceneManager.LoadScene(2);
    }



    private void Update()
    {
        // Show Enter Lobby whenever MultiplayerPanel is active
        if (enterLobbyButton != null)
            enterLobbyButton.gameObject.SetActive(multiplayerPanel != null && multiplayerPanel.activeSelf);
    }

    private void OnEnterLobbyClicked()
    {
        // Host clicked — show lobby locally, sync phase so clients follow
        if (NetworkGameManager.Instance != null)
            NetworkGameManager.Instance.SetLobbyPhase(true);
        HandleSessionActive();
    }

    private void OnPlayerNameChanged(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        NetworkGameManager.Instance?.SetLocalPlayerName(name);
        PlayerPrefs.SetString("PlayerName", name);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Session events
    // ─────────────────────────────────────────────────────────────────────

    private void HandleSessionActive()
    {
        inCharSelectPhase = false;
        isReady           = false;

        HideAllNavPanels();

        // Show lobby — hide character select
        waitingLobbyPanel     ?.SetActive(true);
        waitingLobbyContent   ?.SetActive(true);
        characterSelectPanel  ?.SetActive(false);
        characterSelectContent?.SetActive(false);

        // Show BeginCharSelect for everyone for now
        // (host-only logic can be added later once networking is confirmed working)
        beginCharSelectButton?.gameObject.SetActive(true);
    }

    private void HandleSessionLeft()
    {
        inCharSelectPhase = false;
        isReady           = false;

        waitingLobbyPanel     ?.SetActive(false);
        waitingLobbyContent   ?.SetActive(false);
        characterSelectPanel  ?.SetActive(false);
        characterSelectContent?.SetActive(false);

        ShowNavPanel(mainMenuPanel);
    }

    private void HandleGameStarting()
    {
        loadingPanel          ?.SetActive(true);
        waitingLobbyPanel     ?.SetActive(false);
        waitingLobbyContent   ?.SetActive(false);
        characterSelectPanel  ?.SetActive(false);
        characterSelectContent?.SetActive(false);
    }

    private void HandlePlayersUpdated(List<SessionPlayerInfo> players)
    {
        if (NetworkGameManager.Instance != null)
        {
            // Client detects host moved to lobby
            if (!waitingLobbyContent.activeSelf && NetworkGameManager.Instance.IsLobbyPhase())
                HandleSessionActive();

            // Client detects host moved to char select
            if (!inCharSelectPhase && NetworkGameManager.Instance.IsCharSelectPhase())
                SwitchToCharSelectPhase();
        }

        // Update the active player list
        Transform list = inCharSelectPhase ? charSelectPlayerList : waitingPlayerList;

        if (list != null)
        {
            foreach (Transform child in list)
                Destroy(child.gameObject);

            if (playerSlotPrefab != null)
                foreach (var p in players)
                {
                    var go = Instantiate(playerSlotPrefab, list);
                    string charName = (p.CharacterIndex >= 0 && p.CharacterIndex < characterNames.Count)
                        ? characterNames[p.CharacterIndex]
                        : "Selecting...";
                    go.GetComponent<PlayerSlotUI>()?.Setup(p, charName);
                }
        }

        if (waitingPlayerCount != null && !inCharSelectPhase)
            waitingPlayerCount.text = $"{players.Count} / {NetworkGameManager.Instance?.GetMaxPlayers() ?? 4} players";

        if (inCharSelectPhase)
            RefreshStartButton();
    }

    private void SwitchToCharSelectPhase()
    {
        inCharSelectPhase = true;
        isReady           = false;
        selectedCharIndex = 0;

        HideAllNavPanels();
        waitingLobbyPanel     ?.SetActive(false);
        waitingLobbyContent   ?.SetActive(false);
        characterSelectPanel  ?.SetActive(true);
        characterSelectContent?.SetActive(true);

        RefreshCharacterButtons();
        UpdateReadyVisual();

        if (isSinglePlayer)
        {
            // Single player: hide Ready (no one to wait for), show Start immediately
            readyButton?.gameObject.SetActive(false);
            startButton?.gameObject.SetActive(true);
            if (startButton != null) startButton.interactable = true;
        }
        else
        {
            // Multiplayer: show Ready, show Start for host
            readyButton?.gameObject.SetActive(true);
            startButton?.gameObject.SetActive(true);
            if (startButton != null) startButton.interactable = true;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Phase 1 → Phase 2 transition
    // ─────────────────────────────────────────────────────────────────────

    private void OnBeginCharSelectClicked()
    {
        SwitchToCharSelectPhase();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Character selection
    // ─────────────────────────────────────────────────────────────────────

    private void SelectCharacter(int index)
    {
        selectedCharIndex = index;
        // Only sync if NetworkGameManager has an active session
        if (NetworkGameManager.Instance?.CurrentSession != null)
            NetworkGameManager.Instance.SetLocalCharacterSelection(index);
        RefreshCharacterButtons();
    }

    private void RefreshCharacterButtons()
    {
        for (int i = 0; i < characterButtons.Count; i++)
        {
            if (characterButtons[i] == null) continue;
            bool sel = (i == selectedCharIndex);
            var img = characterButtons[i].GetComponent<Image>();
            if (img != null) img.color = sel ? selectedTint : deselectedTint;
            characterButtons[i].transform.localScale = sel ? new Vector3(1.1f, 1.1f, 1f) : Vector3.one;
        }

        if (selectedCharacterName != null)
            selectedCharacterName.text = (selectedCharIndex < characterNames.Count)
                ? characterNames[selectedCharIndex] : "";
    }

    // ─────────────────────────────────────────────────────────────────────
    // Ready & Start
    // ─────────────────────────────────────────────────────────────────────

    private void OnReadyClicked()
    {
        isReady = !isReady;
        if (NetworkGameManager.Instance?.CurrentSession != null)
            NetworkGameManager.Instance.SetLocalReadyState(isReady);
        UpdateReadyVisual();
        RefreshStartButton();
    }

    private void UpdateReadyVisual()
    {
        var txt = readyButton?.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null) txt.text = isReady ? "Not Ready" : "Ready!";

        var img = readyButton?.GetComponent<Image>();
        if (img != null) img.color = isReady
            ? new Color(0.2f, 0.85f, 0.3f, 1f)
            : Color.white;
    }

    private void OnStartClicked()
    {
        if (isSinglePlayer)
        {
            StartSinglePlayer();
            return;
        }

        // Multiplayer — load scene for all clients
        NetworkGameManager.Instance?.StartGame();
    }

    private void RefreshStartButton()
    {
        if (startButton == null || !(NetworkGameManager.Instance?.IsHost ?? false)) return;

        bool allReady = NetworkGameManager.Instance.AllPlayersReady();
        startButton.interactable = allReady;

        var txt = startButton.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null)
        {
            txt.text  = allReady ? "START ADVENTURE" : "Waiting for players...";
            txt.color = allReady ? Color.white : new Color(1f, 1f, 1f, 0.4f);
        }
    }

    private void OnLeaveClicked()
    {
        isReady           = false;
        inCharSelectPhase = false;
        readyButton?.gameObject.SetActive(true); // restore for next time

        if (isSinglePlayer)
        {
            isSinglePlayer = false;
            ShowNavPanel(modePanel);
        }
        else
        {
            _ = NetworkGameManager.Instance?.LeaveSessionAsync();
        }
    }
}
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
    [SerializeField] private Button enterLobbyButton; // click after widget connects



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
        if (LobbySync.Instance == null) return;
        LobbySync.Instance.OnCharSelectPhaseStarted -= SwitchToCharSelectPhase;
        LobbySync.Instance.OnPlayerDataUpdated      -= HandlePlayerDataUpdated;
    }

    private void Start()
    {
        // Wait for NetworkGameManager to sign in so we have a PlayerId-scoped name
        StartCoroutine(LoadPlayerName());

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

    private System.Collections.IEnumerator LoadPlayerName()
    {
        // Wait until NetworkGameManager has signed in and has a PlayerId
        while (NetworkGameManager.Instance == null || string.IsNullOrEmpty(NetworkGameManager.Instance.LocalPlayerId))
            yield return null;

        string nameKey = $"PlayerName_{NetworkGameManager.Instance.LocalPlayerId}";
        string saved   = PlayerPrefs.GetString(nameKey, NetworkGameManager.Instance.LocalPlayerName);
        if (playerNameInput != null) playerNameInput.text = saved;
    }

    private System.Collections.IEnumerator SubscribeWhenReady()
    {
        // Wait for LobbySync to spawn (requires NGO to be running)
        while (LobbySync.Instance == null)
            yield return null;

        LobbySync.Instance.OnCharSelectPhaseStarted += SwitchToCharSelectPhase;
        LobbySync.Instance.OnPlayerDataUpdated      += HandlePlayerDataUpdated;

        Debug.Log("[MainMenuController] Subscribed to LobbySync events.");
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

    private void GoToSinglePlayerCharSelect()
    {
        isSinglePlayer = true;
        SwitchToCharSelectPhase();
    }

    private void StartSinglePlayer()
    {
        CharacterSelection.Index = selectedCharIndex;
        loadingPanel?.SetActive(true);
        SceneManager.LoadScene(1);
    }



    private void Update()
    {
        if (enterLobbyButton != null && multiplayerPanel != null && multiplayerPanel.activeSelf)
        {
            // Show Enter Lobby once NGO is connected (widget has done its job)
            bool connected = Unity.Netcode.NetworkManager.Singleton != null
                          && Unity.Netcode.NetworkManager.Singleton.IsListening;
            enterLobbyButton.gameObject.SetActive(connected);
        }
    }

    private void OnEnterLobbyClicked()
    {
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
        isSinglePlayer    = false;
        HideAllNavPanels();

        // Show lobby — hide character select
        waitingLobbyPanel     ?.SetActive(true);
        waitingLobbyContent   ?.SetActive(true);
        characterSelectPanel  ?.SetActive(false);
        characterSelectContent?.SetActive(false);

        // Only host can begin character select
        bool isHost = Unity.Netcode.NetworkManager.Singleton?.IsHost ?? false;
        beginCharSelectButton?.gameObject.SetActive(isHost);
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

    private void HandlePlayerDataUpdated(ulong[] clientIds)
    {
        // Rebuild the player list in whichever panel is active
        Transform list = inCharSelectPhase ? charSelectPlayerList : waitingPlayerList;

        if (list != null && playerSlotPrefab != null && LobbySync.Instance != null)
        {
            foreach (Transform child in list) Destroy(child.gameObject);

            foreach (ulong id in clientIds)
            {
                int    charIdx  = LobbySync.Instance.GetCharacterIndex(id);
                bool   ready    = LobbySync.Instance.IsReady(id);
                bool   isLocal  = id == LobbySync.Instance.LocalClientId;
                bool   isHost   = id == 0; // host is always client 0 in NGO

                string charName = (charIdx >= 0 && charIdx < characterNames.Count)
                    ? characterNames[charIdx] : "Selecting...";

                var info = new SessionPlayerInfo($"{id}", $"Player {id}", charIdx, ready, isLocal, isHost);
                var go   = Instantiate(playerSlotPrefab, list);
                go.GetComponent<PlayerSlotUI>()?.Setup(info, charName);
            }

            if (waitingPlayerCount != null && !inCharSelectPhase)
                waitingPlayerCount.text = $"{clientIds.Length} / 4 players";
        }

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
            // Multiplayer: everyone sees Ready, only host sees Start
            readyButton?.gameObject.SetActive(true);
            bool isHost = Unity.Netcode.NetworkManager.Singleton?.IsHost ?? false;
            startButton?.gameObject.SetActive(isHost);
            if (startButton != null) startButton.interactable = false; // enabled when all ready
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Phase 1 → Phase 2 transition
    // ─────────────────────────────────────────────────────────────────────

    private void OnBeginCharSelectClicked()
    {
        // Host tells everyone (including self) to switch via NetworkVariable
        LobbySync.Instance?.BeginCharSelectPhase();
        // Host also calls locally in case LobbySync fires before subscription
        SwitchToCharSelectPhase();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Character selection
    // ─────────────────────────────────────────────────────────────────────

    private void SelectCharacter(int index)
    {
        selectedCharIndex = index;
        if (!isSinglePlayer)
            LobbySync.Instance?.SetMyCharacter(index);
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
        LobbySync.Instance?.SetMyReady(isReady);
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

        // Multiplayer — host loads the scene for everyone via NGO
        if (!(Unity.Netcode.NetworkManager.Singleton?.IsHost ?? false)) return;
        // Store local selection so NetworkedLevelGenerator can read it
        CharacterSelection.Index = selectedCharIndex;
        Unity.Netcode.NetworkManager.Singleton.SceneManager.LoadScene(
            "Multiplayer_1",
            UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    private void RefreshStartButton()
    {
        if (startButton == null) return;
        if (isSinglePlayer) return; // single player start is always ready

        bool isHost = Unity.Netcode.NetworkManager.Singleton?.IsHost ?? false;
        if (!isHost) return;

        bool allReady = LobbySync.Instance?.AllPlayersReady() ?? false;
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
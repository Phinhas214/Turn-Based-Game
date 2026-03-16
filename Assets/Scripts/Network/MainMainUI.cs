using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
    // ── Navigation Panels ─────────────────────────────────────────────────
    [Header("Navigation Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject modePanel;
    [SerializeField] private GameObject multiplayerPanel;
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
    [SerializeField] private Button enterLobbyButton;

    // ── Phase 1: Waiting Lobby ────────────────────────────────────────────
    [Header("Phase 1 — Waiting Lobby")]
    [SerializeField] private GameObject      waitingLobbyPanel;
    [SerializeField] private GameObject      waitingLobbyContent;
    [SerializeField] private TextMeshProUGUI waitingPlayerCount;
    [SerializeField] private Transform       waitingPlayerList;
    [SerializeField] private GameObject      playerSlotPrefab;
    [SerializeField] private Button          beginCharSelectButton;
    [SerializeField] private Button          waitingLeaveButton;

    // ── Phase 2: Character Select ─────────────────────────────────────────
    [Header("Phase 2 — Character Select")]
    [SerializeField] private GameObject      characterSelectPanel;
    [SerializeField] private GameObject      characterSelectContent;
    [SerializeField] private Transform       charSelectPlayerList;
    [SerializeField] private List<Button>     characterButtons;
    [SerializeField] private List<string>     characterNames;
    [SerializeField] private List<Sprite>     characterSprites;
    [SerializeField] private List<GameObject> characterPrefabs;
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
    // Awake
    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        newGameButton          ?.onClick.AddListener(() => ShowNavPanel(modePanel));
        multiplayerButton      ?.onClick.AddListener(() => ShowNavPanel(multiplayerPanel));
        startSinglePlayerButton?.onClick.AddListener(GoToSinglePlayerCharSelect);
        backToMainButton       ?.onClick.AddListener(() => ShowNavPanel(mainMenuPanel));
        backToModeButton       ?.onClick.AddListener(() => ShowNavPanel(modePanel));

        playerNameInput?.onEndEdit.AddListener(OnPlayerNameChanged);
        enterLobbyButton?.onClick.AddListener(OnEnterLobbyClicked);

        beginCharSelectButton?.onClick.AddListener(OnBeginCharSelectClicked);
        waitingLeaveButton   ?.onClick.AddListener(OnLeaveClicked);

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

        readyButton          ?.onClick.AddListener(OnReadyClicked);
        startButton          ?.onClick.AddListener(OnStartClicked);
        charSelectLeaveButton?.onClick.AddListener(OnLeaveClicked);

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
        StartCoroutine(LoadPlayerName());

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

        StartCoroutine(SubscribeWhenReady());
    }

    private System.Collections.IEnumerator LoadPlayerName()
    {
        while (NetworkGameManager.Instance == null || string.IsNullOrEmpty(NetworkGameManager.Instance.LocalPlayerId))
            yield return null;

        string nameKey = $"PlayerName_{NetworkGameManager.Instance.LocalPlayerId}";
        string saved   = PlayerPrefs.GetString(nameKey, NetworkGameManager.Instance.LocalPlayerName);
        if (playerNameInput != null) playerNameInput.text = saved;
    }

    private System.Collections.IEnumerator SubscribeWhenReady()
    {
        while (LobbySync.Instance == null)
            yield return null;

        LobbySync.Instance.OnCharSelectPhaseStarted += SwitchToCharSelectPhase;
        LobbySync.Instance.OnPlayerDataUpdated      += HandlePlayerDataUpdated;

        Debug.Log("[MainMenuController] Subscribed to LobbySync events.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Navigation
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
        mainMenuPanel        ?.SetActive(false);
        modePanel            ?.SetActive(false);
        multiplayerPanel     ?.SetActive(false);
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
        CharacterSelection.Index  = selectedCharIndex;
        CharacterSelection.Prefab = GetSelectedPrefab();

        loadingPanel?.SetActive(true);

        // Only reset WaveManager — let LevelGenerator.ClearLevel handle
        // the grid/enemy cleanup at the correct time during generation
        CleanupForNewGame();

        SceneManager.LoadScene(1);
    }

    /// <summary>
    /// Resets persistent state for a fresh game.
    /// Does NOT touch LevelGrid, RoomManager or room grids —
    /// LevelGenerator.ClearLevel() handles those at the right time.
    /// </summary>
    private void CleanupForNewGame()
    {
        // Reset wave progress so the new game starts at level 1
        WaveManager.Instance?.ResetToLevel1();

        // Clear enemy list only — don't destroy EnemyManager
        // and don't clear room grids here (timing issue)
        EnemyManager.Instance?.ClearAllEnemies();

        Debug.Log("[MainMenuController] Cleaned up for new game.");
    }

    private void Update()
    {
        if (enterLobbyButton != null && multiplayerPanel != null && multiplayerPanel.activeSelf)
        {
            bool connected = Unity.Netcode.NetworkManager.Singleton != null
                          && Unity.Netcode.NetworkManager.Singleton.IsListening;
            enterLobbyButton.gameObject.SetActive(connected);
        }
    }

    private void OnEnterLobbyClicked() => HandleSessionActive();

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

        waitingLobbyPanel     ?.SetActive(true);
        waitingLobbyContent   ?.SetActive(true);
        characterSelectPanel  ?.SetActive(false);
        characterSelectContent?.SetActive(false);

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
        Transform list = inCharSelectPhase ? charSelectPlayerList : waitingPlayerList;

        if (list != null && playerSlotPrefab != null && LobbySync.Instance != null)
        {
            foreach (Transform child in list) Destroy(child.gameObject);

            foreach (ulong id in clientIds)
            {
                int    charIdx  = LobbySync.Instance.GetCharacterIndex(id);
                bool   ready    = LobbySync.Instance.IsReady(id);
                bool   isLocal  = id == LobbySync.Instance.LocalClientId;
                bool   isHost   = id == 0;

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
            readyButton?.gameObject.SetActive(false);
            startButton?.gameObject.SetActive(true);
            if (startButton != null) startButton.interactable = true;
        }
        else
        {
            readyButton?.gameObject.SetActive(true);
            bool isHost = Unity.Netcode.NetworkManager.Singleton?.IsHost ?? false;
            startButton?.gameObject.SetActive(isHost);
            if (startButton != null) startButton.interactable = false;
        }
    }

    private void OnBeginCharSelectClicked()
    {
        LobbySync.Instance?.BeginCharSelectPhase();
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

        if (!(Unity.Netcode.NetworkManager.Singleton?.IsHost ?? false)) return;
        CharacterSelection.Index  = selectedCharIndex;
        CharacterSelection.Prefab = GetSelectedPrefab();
        Unity.Netcode.NetworkManager.Singleton.SceneManager.LoadScene(
            "Multiplayer_1",
            UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    private void RefreshStartButton()
    {
        if (startButton == null || isSinglePlayer) return;

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
        readyButton?.gameObject.SetActive(true);

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

    private GameObject GetSelectedPrefab()
    {
        if (characterPrefabs == null || selectedCharIndex >= characterPrefabs.Count)
            return null;
        return characterPrefabs[selectedCharIndex];
    }
}
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject modePanel;
    [SerializeField] private GameObject multiplayerSelectPanel; // NEW: Host vs Join choice
    [SerializeField] private GameObject hostPanel;              // NEW: Just the Create widget
    [SerializeField] private GameObject joinPanel;              // NEW: Just the Session List
    [SerializeField] private GameObject joinByCodePanel;
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private GameObject loadingPanel;

    [Header("Buttons: Main & Mode")]
    [SerializeField] private Button newGameButton;
    [SerializeField] private Button multiplayerButton;
    [SerializeField] private Button backToMainButton;

    [Header("Buttons: Multiplayer Selection")]
    [SerializeField] private Button selectHostButton;
    [SerializeField] private Button selectJoinButton;
    [SerializeField] private TMP_InputField playerNameInput;
    [SerializeField] private Button backToModeButton;

    [Header("Buttons: Host/Join Sub-Panels")]
    [SerializeField] private Button joinByCodeButton; 
    [SerializeField] private Button backFromHostButton;
    [SerializeField] private Button backFromJoinButton;
    [SerializeField] private Button backFromCodeButton;

    [Header("Lobby Panel")]
    [SerializeField] private Transform playerListContainer;
    [SerializeField] private GameObject playerSlotPrefab;
    [SerializeField] private Button readyButton;
    [SerializeField] private Button startButton;
    [SerializeField] private Button leaveLobbyButton;

    private bool isReady = false;

    private void Awake()
    {
        // Setup Navigation Listeners
        newGameButton?.onClick.AddListener(() => ShowPanel(modePanel));
        multiplayerButton?.onClick.AddListener(() => ShowPanel(multiplayerSelectPanel));
        
        // Selection Panel
        selectHostButton?.onClick.AddListener(() => ShowPanel(hostPanel));
        selectJoinButton?.onClick.AddListener(() => ShowPanel(joinPanel));
        playerNameInput?.onEndEdit.AddListener(OnPlayerNameChanged);

        // Join/Host Sub-navigation
        joinByCodeButton?.onClick.AddListener(() => ShowPanel(joinByCodePanel));

        // Back Buttons
        backToMainButton?.onClick.AddListener(() => ShowPanel(mainMenuPanel));
        backToModeButton?.onClick.AddListener(() => ShowPanel(modePanel));
        backFromHostButton?.onClick.AddListener(() => ShowPanel(multiplayerSelectPanel));
        backFromJoinButton?.onClick.AddListener(() => ShowPanel(multiplayerSelectPanel));
        backFromCodeButton?.onClick.AddListener(() => ShowPanel(joinPanel));

        // Lobby
        readyButton?.onClick.AddListener(OnReadyClicked);
        startButton?.onClick.AddListener(OnStartGameClicked);
        leaveLobbyButton?.onClick.AddListener(OnLeaveLobbyClicked);
    }

    private void Start()
    {
        // Restore name
        string savedName = PlayerPrefs.GetString("PlayerName", "Player" + Random.Range(100, 999));
        if (playerNameInput != null) playerNameInput.text = savedName;
        
        ShowPanel(mainMenuPanel);
    }

    // --- Logic Methods ---

    private void OnPlayerNameChanged(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        NetworkGameManager.Instance?.SetLocalPlayerName(newName);
        PlayerPrefs.SetString("PlayerName", newName);
    }

    public void OnReadyClicked()
    {
        isReady = !isReady;
        NetworkGameManager.Instance?.SetLocalReadyState(isReady);
        var txt = readyButton?.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null) txt.text = isReady ? "Not Ready" : "Ready";
    }

    public void OnStartGameClicked() => NetworkGameManager.Instance?.StartGame();

    public void OnLeaveLobbyClicked() => _ = NetworkGameManager.Instance?.LeaveSessionAsync();

    // --- Panel Management ---

    private void ShowPanel(GameObject target)
    {
        // Hide all major panels
        mainMenuPanel?.SetActive(false);
        modePanel?.SetActive(false);
        multiplayerSelectPanel?.SetActive(false);
        hostPanel?.SetActive(false);
        joinPanel?.SetActive(false);
        joinByCodePanel?.SetActive(false);
        lobbyPanel?.SetActive(false);

        // Show the one we want
        target?.SetActive(true);
    }

    // --- Networking Events ---
    private void OnEnable()
    {
        if (NetworkGameManager.Instance == null) return;
        NetworkGameManager.Instance.OnSessionCreated += HandleSessionActive;
        NetworkGameManager.Instance.OnSessionJoined += HandleSessionActive;
        NetworkGameManager.Instance.OnSessionLeft += HandleSessionLeft;
        NetworkGameManager.Instance.OnPlayersUpdated += HandlePlayersUpdated;
    }

    private void HandleSessionActive() 
    {
        isReady = false;
        ShowPanel(lobbyPanel);
        startButton?.gameObject.SetActive(NetworkGameManager.Instance.IsHost);
    }

    private void HandleSessionLeft() => ShowPanel(mainMenuPanel);

    private void HandlePlayersUpdated(List<SessionPlayerInfo> players)
    {
        if (playerListContainer == null) return;
        foreach (Transform child in playerListContainer) Destroy(child.gameObject);
        foreach (var player in players)
        {
            var entry = Instantiate(playerSlotPrefab, playerListContainer);
            entry.GetComponent<LobbyPlayerEntry>()?.Setup(player);
        }
        if (startButton != null && NetworkGameManager.Instance.IsHost)
            startButton.interactable = NetworkGameManager.Instance.AllPlayersReady();
    }
}
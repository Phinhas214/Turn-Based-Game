using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Main menu UI controller.
///
/// SCENE SETUP:
///   Panel hierarchy (all under a Canvas):
///     MainMenuPanel
///       - TitleText (TextMeshProUGUI)
///       - PlayerNameInput (TMP_InputField)
///       - HostButton (Button)
///       - JoinButton (Button)   → reveals JoinCodePanel
///       - QuickJoinButton (Button)
///     JoinCodePanel
///       - JoinCodeInput (TMP_InputField)
///       - ConfirmJoinButton (Button)
///       - BackButton (Button)
///     StatusText (TextMeshProUGUI)  — shown below main panel for errors/info
///     LoadingSpinner (GameObject)   — shown while async ops are running
///
/// Wire all references in the Inspector.
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    // ── Panels ────────────────────────────────────────────────────────────
    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject joinCodePanel;
    [SerializeField] private GameObject loadingPanel;

    // ── Main menu elements ────────────────────────────────────────────────
    [Header("Main Menu")]
    [SerializeField] private TMP_InputField playerNameInput;
    [SerializeField] private Button         hostButton;
    [SerializeField] private Button         joinButton;
    [SerializeField] private Button         quickJoinButton;

    // ── Join code panel ───────────────────────────────────────────────────
    [Header("Join Code Panel")]
    [SerializeField] private TMP_InputField joinCodeInput;
    [SerializeField] private Button         confirmJoinButton;
    [SerializeField] private Button         backButton;

    // ── Feedback ──────────────────────────────────────────────────────────
    [Header("Feedback")]
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private float           statusFadeTime = 3f;

    // ── Private ───────────────────────────────────────────────────────────
    private Coroutine statusFadeRoutine;
    private bool      isWaiting = false;

    // ─────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Wire buttons
        hostButton      .onClick.AddListener(OnHostClicked);
        joinButton      .onClick.AddListener(OnJoinClicked);
        quickJoinButton .onClick.AddListener(OnQuickJoinClicked);
        confirmJoinButton.onClick.AddListener(OnConfirmJoinClicked);
        backButton      .onClick.AddListener(OnBackClicked);

        if (playerNameInput != null)
            playerNameInput.onEndEdit.AddListener(OnPlayerNameChanged);
    }

    private void OnEnable()
    {
        if (NetworkGameManager.Instance == null) return;

        NetworkGameManager.Instance.OnSignedIn          += HandleSignedIn;
        NetworkGameManager.Instance.OnSignInFailed      += HandleSignInFailed;
        NetworkGameManager.Instance.OnLobbyCreated      += HandleLobbyCreated;
        NetworkGameManager.Instance.OnLobbyJoined       += HandleLobbyJoined;
        NetworkGameManager.Instance.OnLobbyError        += HandleLobbyError;
    }

    private void OnDisable()
    {
        if (NetworkGameManager.Instance == null) return;

        NetworkGameManager.Instance.OnSignedIn          -= HandleSignedIn;
        NetworkGameManager.Instance.OnSignInFailed      -= HandleSignInFailed;
        NetworkGameManager.Instance.OnLobbyCreated      -= HandleLobbyCreated;
        NetworkGameManager.Instance.OnLobbyJoined       -= HandleLobbyJoined;
        NetworkGameManager.Instance.OnLobbyError        -= HandleLobbyError;
    }

    private void Start()
    {
        ShowPanel(mainMenuPanel);
        SetButtonsInteractable(false); // disable until signed in
        ShowStatus("Connecting to services...", Color.yellow);

        // Load saved player name
        string savedName = PlayerPrefs.GetString("PlayerName", "");
        if (!string.IsNullOrEmpty(savedName) && playerNameInput != null)
        {
            playerNameInput.text = savedName;
            NetworkGameManager.Instance?.SetLocalPlayerName(savedName);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Button handlers
    // ─────────────────────────────────────────────────────────────────────

    private void OnHostClicked()
    {
        if (isWaiting || NetworkGameManager.Instance == null) return;
        SetWaiting(true);
        ShowStatus("Creating lobby...", Color.cyan);
        _ = NetworkGameManager.Instance.CreateLobbyAsync();
    }

    private void OnJoinClicked()
    {
        ShowPanel(joinCodePanel);
        joinCodeInput.text = "";
        joinCodeInput.Select();
    }

    private void OnQuickJoinClicked()
    {
        if (isWaiting || NetworkGameManager.Instance == null) return;
        SetWaiting(true);
        ShowStatus("Searching for lobby...", Color.cyan);
        _ = NetworkGameManager.Instance.QuickJoinAsync();
    }

    private void OnConfirmJoinClicked()
    {
        string code = joinCodeInput.text.Trim();
        if (string.IsNullOrEmpty(code))
        {
            ShowStatus("Please enter a lobby code.", Color.red);
            return;
        }

        SetWaiting(true);
        ShowStatus($"Joining lobby {code}...", Color.cyan);
        _ = NetworkGameManager.Instance.JoinLobbyByCodeAsync(code);
    }

    private void OnBackClicked()
    {
        ShowPanel(mainMenuPanel);
    }

    private void OnPlayerNameChanged(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        NetworkGameManager.Instance?.SetLocalPlayerName(newName);
        PlayerPrefs.SetString("PlayerName", newName);
        PlayerPrefs.Save();
    }

    // ─────────────────────────────────────────────────────────────────────
    // NetworkGameManager event handlers
    // ─────────────────────────────────────────────────────────────────────

    private void HandleSignedIn()
    {
        SetButtonsInteractable(true);
        ShowStatus("Connected. Ready to play.", Color.green);
        SetWaiting(false);
    }

    private void HandleSignInFailed(string error)
    {
        ShowStatus($"Connection failed: {error}", Color.red);
        SetWaiting(false);
    }

    private void HandleLobbyCreated(Unity.Services.Lobby.Models.Lobby lobby)
    {
        SetWaiting(false);
        // Navigate to the lobby room scene / panel
        // The LobbyRoomUI handles this transition
        Debug.Log($"[MainMenuUI] Lobby created: {lobby.Id}");
    }

    private void HandleLobbyJoined(Unity.Services.Lobby.Models.Lobby lobby)
    {
        SetWaiting(false);
        Debug.Log($"[MainMenuUI] Lobby joined: {lobby.Id}");
    }

    private void HandleLobbyError(string error)
    {
        ShowStatus(error, Color.red);
        SetWaiting(false);
        ShowPanel(mainMenuPanel);
    }

    // ─────────────────────────────────────────────────────────────────────
    // UI helpers
    // ─────────────────────────────────────────────────────────────────────

    private void ShowPanel(GameObject panel)
    {
        if (mainMenuPanel  != null) mainMenuPanel .SetActive(panel == mainMenuPanel);
        if (joinCodePanel  != null) joinCodePanel .SetActive(panel == joinCodePanel);
    }

    private void SetWaiting(bool waiting)
    {
        isWaiting = waiting;
        if (loadingPanel != null) loadingPanel.SetActive(waiting);
        SetButtonsInteractable(!waiting);
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (hostButton      != null) hostButton      .interactable = interactable;
        if (joinButton      != null) joinButton      .interactable = interactable;
        if (quickJoinButton != null) quickJoinButton .interactable = interactable;
    }

    private void ShowStatus(string message, Color color)
    {
        if (statusText == null) return;

        if (statusFadeRoutine != null)
            StopCoroutine(statusFadeRoutine);

        statusText.text  = message;
        statusText.color = color;
        statusText.gameObject.SetActive(true);

        // Auto-fade after a few seconds (only for success/info messages)
        if (color != Color.red)
            statusFadeRoutine = StartCoroutine(FadeStatusRoutine());
    }

    private IEnumerator FadeStatusRoutine()
    {
        yield return new WaitForSeconds(statusFadeTime);

        float elapsed = 0f;
        float fadeTime = 1f;
        Color start = statusText.color;

        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            Color c = start;
            c.a = Mathf.Lerp(1f, 0f, elapsed / fadeTime);
            statusText.color = c;
            yield return null;
        }

        statusText.gameObject.SetActive(false);
    }
}
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject lobbyPanel;

    [Header("Player Name")]
    [SerializeField] private TMP_InputField playerNameInput;

    [Header("Character Selection Buttons")]
    [SerializeField] private Button selectKnightBtn;
    [SerializeField] private Button selectRogueBtn;
    [SerializeField] private Button selectMageBtn;
    [SerializeField] private Button selectClericBtn;

    [Header("Player List Setup")]
    [SerializeField] private Transform  playerListContent;   // The "Content" of a ScrollView
    [SerializeField] private GameObject playerEntryPrefab;   // Has LobbyPlayerEntry component

    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        playerNameInput?.onEndEdit.AddListener(OnPlayerNameChanged);

        selectKnightBtn?.onClick.AddListener(() => SetClass(0));
        selectRogueBtn ?.onClick.AddListener(() => SetClass(1));
        selectMageBtn  ?.onClick.AddListener(() => SetClass(2));
        selectClericBtn?.onClick.AddListener(() => SetClass(3));
    }

    private void OnEnable()
    {
        if (NetworkGameManager.Instance == null) return;
        NetworkGameManager.Instance.OnSignedIn       += HandleSignedIn;
        NetworkGameManager.Instance.OnSessionCreated += HandleSessionCreated;
        NetworkGameManager.Instance.OnSessionJoined  += HandleSessionJoined;
        NetworkGameManager.Instance.OnSessionLeft    += HandleSessionLeft;
        NetworkGameManager.Instance.OnPlayersUpdated += UpdatePlayerListUI;
    }

    private void OnDisable()
    {
        if (NetworkGameManager.Instance == null) return;
        NetworkGameManager.Instance.OnSignedIn       -= HandleSignedIn;
        NetworkGameManager.Instance.OnSessionCreated -= HandleSessionCreated;
        NetworkGameManager.Instance.OnSessionJoined  -= HandleSessionJoined;
        NetworkGameManager.Instance.OnSessionLeft    -= HandleSessionLeft;
        NetworkGameManager.Instance.OnPlayersUpdated -= UpdatePlayerListUI;
    }

    private void Start()
    {
        string savedName = PlayerPrefs.GetString("PlayerName", "");
        if (!string.IsNullOrEmpty(savedName))
        {
            if (playerNameInput != null) playerNameInput.text = savedName;
            NetworkGameManager.Instance?.SetLocalPlayerName(savedName);
        }

        ShowMainMenu();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Session event handlers
    // ─────────────────────────────────────────────────────────────────────

    private void HandleSignedIn()
    {
        Debug.Log("[MainMenuUI] Signed in and ready.");
    }

    private void HandleSessionCreated()
    {
        mainMenuPanel?.SetActive(false);
        lobbyPanel?.SetActive(true);
    }

    private void HandleSessionJoined()
    {
        mainMenuPanel?.SetActive(false);
        lobbyPanel?.SetActive(true);
    }

    private void HandleSessionLeft()
    {
        ShowMainMenu();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Character selection
    // ─────────────────────────────────────────────────────────────────────

    private void SetClass(int index)
    {
        if (NetworkGameManager.Instance == null) return;

        NetworkGameManager.Instance.SetLocalCharacterSelection(index);
        NetworkGameManager.Instance.SetLocalReadyState(true);

        Debug.Log($"Selected Character Index: {index}");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Player list
    // ─────────────────────────────────────────────────────────────────────

    private void UpdatePlayerListUI(List<SessionPlayerInfo> players)
    {
        if (playerListContent == null) return;

        foreach (Transform child in playerListContent)
            Destroy(child.gameObject);

        if (playerEntryPrefab == null) return;

        foreach (var player in players)
        {
            GameObject entry = Instantiate(playerEntryPrefab, playerListContent);
            entry.GetComponent<LobbyPlayerEntry>()?.Setup(player);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Player name
    // ─────────────────────────────────────────────────────────────────────

    private void OnPlayerNameChanged(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        NetworkGameManager.Instance?.SetLocalPlayerName(newName);
        PlayerPrefs.SetString("PlayerName", newName);
        PlayerPrefs.Save();
    }

    // ─────────────────────────────────────────────────────────────────────

    private void ShowMainMenu()
    {
        mainMenuPanel?.SetActive(true);
        lobbyPanel?.SetActive(false);
    }
}
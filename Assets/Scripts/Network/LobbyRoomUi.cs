using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Handles everything inside the Lobby Panel.
///
/// FIX: We no longer disable the LobbyPanel GameObject itself because if it starts
/// inactive, Awake never runs and the session events never get subscribed to.
/// Instead, LobbyPanel stays ACTIVE in the scene always, and we show/hide a
/// child "LobbyContent" GameObject that contains all the visible UI.
///
/// REQUIRED HIERARCHY:
///
///   LobbyPanel                        ← ACTIVE at start, has LobbyRoomUI component
///     LobbyContent                    ← INACTIVE at start — this is what we show/hide
///       ShowSessionCode               ← Widget
///       PlayerCountText               ← TextMeshProUGUI  e.g. "2 / 4 players"
///       PlayerList                    ← empty GO + Vertical Layout Group (player slots spawn here)
///       CharacterSelectPanel          ← empty GO + Horizontal Layout Group
///           KnightButton              ← Button (index 0)
///               CharacterImage        ← child Image (portrait sprite goes here)
///           RogueButton               ← Button (index 1)
///           MageButton                ← Button (index 2)
///           ClericButton              ← Button (index 3)
///       SelectedCharacterName         ← TextMeshProUGUI
///       ReadyButton                   ← Button
///       StartButton                   ← Button (host only)
///       Exit (4)                      ← already your leave widget/button
///
/// INSPECTOR WIRING:
///   Lobby Content        → LobbyContent GameObject
///   Player List          → PlayerList Transform
///   Player Entry Prefab  → your Slot prefab (has LobbyPlayerEntry component)
///   Player Count Text    → PlayerCountText
///   Character Buttons    → KnightButton, RogueButton, MageButton, ClericButton (in order)
///   Character Sprites    → 4 portrait sprites (same order)
///   Selected Char Text   → SelectedCharacterName
///   Ready Button         → ReadyButton
///   Start Button         → StartButton
/// </summary>
public class LobbyRoomUI : MonoBehaviour
{
    // ── Content container (we show/hide this, not LobbyPanel itself) ──────
    [Header("Content Root")]
    [SerializeField] private GameObject lobbyContent;  // the child that holds all lobby UI

    // ── Player List ───────────────────────────────────────────────────────
    [Header("Player List")]
    [SerializeField] private Transform       playerList;
    [SerializeField] private GameObject      playerEntryPrefab;
    [SerializeField] private TextMeshProUGUI playerCountText;

    // ── Character Selection ───────────────────────────────────────────────
    [Header("Character Select")]
    [SerializeField] private List<Button>    characterButtons;   // Knight=0, Rogue=1, Mage=2, Cleric=3
    [SerializeField] private List<Sprite>    characterSprites;   // same order
    [SerializeField] private TextMeshProUGUI selectedCharacterNameText;
    [SerializeField] private Color selectedColor   = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField] private Color deselectedColor = new Color(1f, 1f, 1f, 0.4f);

    // ── Buttons ───────────────────────────────────────────────────────────
    [Header("Buttons")]
    [SerializeField] private Button readyButton;
    [SerializeField] private Button startButton;

    // ── Runtime ───────────────────────────────────────────────────────────
    private static readonly string[] CharacterNames = { "Knight", "Rogue", "Mage", "Cleric" };
    private int  selectedCharacterIndex = 0;
    private bool isReady               = false;

    // ─────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Wire character buttons and apply sprites
        for (int i = 0; i < characterButtons.Count; i++)
        {
            int idx = i;
            characterButtons[i]?.onClick.AddListener(() => SelectCharacter(idx));

            // Apply portrait sprite to the child Image inside the button
            if (i < characterSprites.Count && characterSprites[i] != null)
            {
                var images = characterButtons[i]?.GetComponentsInChildren<Image>();
                // images[0] = button background, images[1] = portrait child Image
                if (images != null && images.Length > 1)
                    images[1].sprite = characterSprites[i];
            }
        }

        readyButton ?.onClick.AddListener(OnReadyClicked);
        startButton ?.onClick.AddListener(OnStartClicked);

        // Hide the content — NOT the panel itself
        lobbyContent?.SetActive(false);
    }

    // Awake runs because LobbyPanel is ACTIVE — now OnEnable can subscribe
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
        lobbyContent?.SetActive(true);

        // Reset for new session
        isReady = false;
        selectedCharacterIndex = 0;
        UpdateReadyButtonVisual();
        RefreshCharacterButtons();

        // Start button: visible only to host, disabled until all ready
        bool isHost = NetworkGameManager.Instance?.IsHost ?? false;
        startButton?.gameObject.SetActive(isHost);
        if (startButton != null) startButton.interactable = false;
    }

    private void HandleSessionEnded()
    {
        isReady = false;
        lobbyContent?.SetActive(false);
    }

    private void HandleGameStarting()
    {
        lobbyContent?.SetActive(false);
    }

    private void HandlePlayersUpdated(List<SessionPlayerInfo> players)
    {
        // Rebuild player list
        if (playerList != null)
        {
            foreach (Transform child in playerList)
                Destroy(child.gameObject);

            if (playerEntryPrefab != null)
                foreach (var player in players)
                {
                    var entry = Instantiate(playerEntryPrefab, playerList);
                    entry.GetComponent<LobbyPlayerEntry>()?.Setup(player);
                }
        }

        if (playerCountText != null)
            playerCountText.text = $"{players.Count} / {NetworkGameManager.Instance?.GetMaxPlayers() ?? 4} players";

        RefreshStartButton();
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

            bool selected = (i == selectedCharacterIndex);

            // Tint button background
            var btnImg = characterButtons[i].GetComponent<Image>();
            if (btnImg != null) btnImg.color = selected ? selectedColor : deselectedColor;

            // Scale selected button up
            characterButtons[i].transform.localScale = selected
                ? new Vector3(1.1f, 1.1f, 1f)
                : Vector3.one;
        }

        if (selectedCharacterNameText != null)
            selectedCharacterNameText.text = selectedCharacterIndex < CharacterNames.Length
                ? CharacterNames[selectedCharacterIndex]
                : "";
    }

    // ─────────────────────────────────────────────────────────────────────
    // Ready & Start
    // ─────────────────────────────────────────────────────────────────────

    private void OnReadyClicked()
    {
        isReady = !isReady;
        NetworkGameManager.Instance?.SetLocalReadyState(isReady);
        UpdateReadyButtonVisual();
        RefreshStartButton();
    }

    private void UpdateReadyButtonVisual()
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
        if (NetworkGameManager.Instance == null || !NetworkGameManager.Instance.IsHost) return;
        if (!NetworkGameManager.Instance.AllPlayersReady()) return;
        NetworkGameManager.Instance.StartGame();
    }

    private void RefreshStartButton()
    {
        if (startButton == null || !(NetworkGameManager.Instance?.IsHost ?? false)) return;

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
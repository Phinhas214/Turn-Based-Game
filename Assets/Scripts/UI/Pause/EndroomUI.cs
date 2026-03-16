using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Detects when the player enters the End room via RoomManager.OnAnyRoomChanged.
/// Place this on any persistent manager GameObject.
///
/// SETUP:
///   Wire up your own Panel, Buttons, and Labels in the Inspector fields below.
///   The panel starts hidden and shows when the player reaches the End room.
/// </summary>
public class EndRoomUI : MonoBehaviour
{
    [Header("Scene Names")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Header("Panel — assign your root panel GameObject")]
    [SerializeField] private GameObject panelRoot;

    [Header("Buttons — assign your UI buttons")]
    [SerializeField] private Button nextLevelButton;
    [SerializeField] private Button mainMenuButton;

    [Header("Labels — assign your TMP text objects (optional)")]
    [SerializeField] private TextMeshProUGUI levelLabel;
    [SerializeField] private TextMeshProUGUI stagesClearedLabel;
    [SerializeField] private TextMeshProUGUI nextLevelLabel;

    private bool _shown = false;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Start()
    {
        nextLevelButton?.onClick.AddListener(OnNextLevelClicked);
        mainMenuButton?.onClick.AddListener(OnMainMenuClicked);
        HidePanel();
    }

    private void OnEnable()
    {
        RoomManager.OnAnyRoomChanged += OnRoomChanged;
        LevelGenerator.OnLevelReady  += OnLevelReady;
    }

    private void OnDisable()
    {
        RoomManager.OnAnyRoomChanged -= OnRoomChanged;
        LevelGenerator.OnLevelReady  -= OnLevelReady;
    }

    // ── Room detection ─────────────────────────────────────────────────────

    private void OnRoomChanged(LevelGenerator.PlacedRoom room)
    {
        if (_shown) return;
        if (room?.prefabData == null) return;

        if (room.prefabData.roomType == LevelGenerator.RoomType.End)
        {
            _shown = true;
            ShowPanel();
        }
    }

    private void OnLevelReady()
    {
        _shown = false;
        HidePanel();
    }

    // ── Panel ──────────────────────────────────────────────────────────────

    private void ShowPanel()
    {
        if (panelRoot == null)
        {
            Debug.LogWarning("[EndRoomUI] panelRoot is not assigned in the Inspector!");
            return;
        }

        panelRoot.SetActive(true);

        int current       = WaveManager.Instance != null ? WaveManager.Instance.CurrentLevel  : 1;
        int stagesCleared = WaveManager.Instance != null ? WaveManager.Instance.StagesCleared : 0;

        if (levelLabel         != null) levelLabel.text         = $"Level {current} Complete!";
        if (stagesClearedLabel != null) stagesClearedLabel.text = stagesCleared == 0
            ? "First stage cleared!"
            : $"Stages Cleared: {stagesCleared}";
        if (nextLevelLabel     != null) nextLevelLabel.text     = $"Next: Level {current + 1}";

        Time.timeScale = 0f;
        Debug.Log($"[EndRoomUI] End room reached. Level={current} StagesCleared={stagesCleared}");
    }

    private void HidePanel()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    // ── Buttons ────────────────────────────────────────────────────────────

    public void OnNextLevelClicked()
    {
        Debug.Log("[EndRoomUI] Next Level clicked.");
        Time.timeScale = 1f;
        HidePanel();
        _shown = false;

        WaveManager.Instance?.AdvanceLevel();
        GameStateManager.Instance?.NotifyLevelAdvanced();

        LevelGenerator levelGen = FindFirstObjectByType<LevelGenerator>();
        if (levelGen != null)
            levelGen.GenerateLevel();
        else
            Debug.LogError("[EndRoomUI] No LevelGenerator found!");
    }

    public void OnMainMenuClicked()
    {
        Debug.Log("[EndRoomUI] Going to Main Menu.");
        Time.timeScale = 1f;

        WaveManager.Instance?.ResetToLevel1();
        CleanupPersistentObjects();
        SceneManager.LoadScene(mainMenuSceneName);
    }

    // ── Cleanup ────────────────────────────────────────────────────────────

    private void CleanupPersistentObjects()
    {
        if (EnemyManager.Instance != null)
        {
            EnemyManager.Instance.ClearAllEnemies();
            Destroy(EnemyManager.Instance.gameObject);
        }

        if (LevelGrid.Instance != null)
        {
            LevelGrid.Instance.ClearAllRoomGrids();
            Destroy(LevelGrid.Instance.gameObject);
        }

        if (RoomManager.Instance != null)
            Destroy(RoomManager.Instance.gameObject);

        if (GameStateManager.Instance != null)
            Destroy(GameStateManager.Instance.gameObject);

        Debug.Log("[EndRoomUI] Persistent objects cleaned up.");
    }
}
using UnityEngine;

/// <summary>
/// Singleton that tracks the current level, stages cleared, and scales difficulty.
/// Persists across scene loads via DontDestroyOnLoad.
/// </summary>
public class WaveManager : MonoBehaviour
{
    public static WaveManager Instance { get; private set; }

    [Header("Enemy Budget Scaling")]
    [Tooltip("Total enemies to spawn on level 1.")]
    [SerializeField] private int baseEnemyCount = 5;
    [Tooltip("Additional enemies added per level.")]
    [SerializeField] private int enemiesPerLevel = 3;
    [Tooltip("Hard cap on total enemies regardless of level.")]
    [SerializeField] private int maxEnemies = 40;

    [Header("Room Count Scaling")]
    [Tooltip("Minimum rooms on level 1.")]
    [SerializeField] private int baseMinRooms = 5;
    [Tooltip("Maximum rooms on level 1.")]
    [SerializeField] private int baseMaxRooms = 8;
    [Tooltip("Extra rooms added to both min and max per level.")]
    [SerializeField] private int roomsPerLevel = 1;
    [Tooltip("Hard cap on room count regardless of level.")]
    [SerializeField] private int maxRooms = 20;

    // ── State ──────────────────────────────────────────────────────────────

    public int CurrentLevel    { get; private set; } = 1;
    public int StagesCleared   { get; private set; } = 0;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── API ────────────────────────────────────────────────────────────────

    /// <summary>Call when the player completes a stage and moves to the next.</summary>
    public void AdvanceLevel()
    {
        StagesCleared++;
        CurrentLevel++;
        Debug.Log($"[WaveManager] Stage cleared! Total cleared: {StagesCleared}. Now on level {CurrentLevel}.");
    }

    /// <summary>Full reset — call when returning to main menu or starting fresh.</summary>
    public void ResetToLevel1()
    {
        CurrentLevel  = 1;
        StagesCleared = 0;
        Debug.Log("[WaveManager] Reset to level 1. Stages cleared reset to 0.");
    }

    public int GetTotalEnemyBudget()
        => Mathf.Min(baseEnemyCount + (CurrentLevel - 1) * enemiesPerLevel, maxEnemies);

    public int GetMinRooms()
        => Mathf.Min(baseMinRooms + (CurrentLevel - 1) * roomsPerLevel, maxRooms - 1);

    public int GetMaxRooms()
        => Mathf.Min(baseMaxRooms + (CurrentLevel - 1) * roomsPerLevel, maxRooms);
}
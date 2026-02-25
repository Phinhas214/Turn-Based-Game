using System;
using UnityEngine;

/// <summary>
/// Represents an enemy on the grid.
/// Implements IHasHealth so HealthComponent auto-initializes from EnemyStats.
/// </summary>
[RequireComponent(typeof(HealthComponent))]
public class EnemyUnit : MonoBehaviour, IHasHealth  // NEW — implements IHasHealth
{
    [Header("Stats")]
    [Tooltip("Data asset defining this enemy's stats. Create via Assets > Create > Combat > Enemy Stats.")]
    [SerializeField] private EnemyStats stats;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    // ── Runtime state ──────────────────────────────────────────────────────
    private GridPosition    gridPosition;
    private RoomGrid        currentRoomGrid;
    private HealthComponent health;
    private bool            isInitialized = false;
    private int             turnsWaited   = 0;

    // ── Events ─────────────────────────────────────────────────────────────
    public event Action<EnemyUnit> OnEnemyDied;

    // ── Properties ────────────────────────────────────────────────────────
    public EnemyStats      Stats           => stats;
    public HealthComponent Health          => health;
    public GridPosition    GridPosition    => gridPosition;
    public RoomGrid        CurrentRoomGrid => currentRoomGrid;
    public bool            IsInitialized   => isInitialized;
    public bool            IsDead          => health != null && health.IsDead;

    // ── IHasHealth ─────────────────────────────────────────────────────────
    // HealthComponent calls this in Awake — must be available before Start.
    // EnemyStats is a serialized field so it's ready at Awake time.

    public int GetMaxHealth()
    {
        return stats != null ? stats.maxHealth : 100;
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        health = GetComponent<HealthComponent>();
        // HealthComponent.Awake runs first (same frame), finds us via IHasHealth,
        // and sets its maxHealth from stats.maxHealth automatically.
    }

    private void Start()
    {
        health.OnDeath += HandleDeath;
    }

    private void OnDestroy()
    {
        if (health != null)
            health.OnDeath -= HandleDeath;

        // FIXED: was incorrectly calling RemoveUnitAtGridPosition with null
        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, this);

        EnemyManager.Instance?.UnregisterEnemy(this);
    }

    // ── Grid placement ─────────────────────────────────────────────────────

    public void PlaceOnGrid(RoomGrid roomGrid, GridPosition position)
    {
        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, this);

        currentRoomGrid = roomGrid;
        gridPosition    = position;

        transform.position = roomGrid.GetWorldPosition(position);
        roomGrid.AddEnemyAtGridPosition(position, this);

        isInitialized = true;

        if (showDebugLogs)
            Debug.Log($"[EnemyUnit] {stats?.enemyName} placed at {position}");
    }

    public void MoveToPosition(GridPosition newPosition)
    {
        if (!isInitialized) return;

        currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, this);
        gridPosition = newPosition;
        currentRoomGrid.AddEnemyAtGridPosition(newPosition, this);

        transform.position = currentRoomGrid.GetWorldPosition(newPosition);

        if (showDebugLogs)
            Debug.Log($"[EnemyUnit] {stats?.enemyName} moved to {newPosition}");
    }

    // ── Turn handling ──────────────────────────────────────────────────────

    public bool CanActThisTurn()
    {
        if (IsDead) return false;
        if (turnsWaited < stats.turnsBeforeFirstAction)
        {
            turnsWaited++;
            return false;
        }
        return true;
    }

    // ── Death ──────────────────────────────────────────────────────────────

    private void HandleDeath()
    {
        if (showDebugLogs)
            Debug.Log($"[EnemyUnit] {stats?.enemyName} died.");

        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, this);

        OnEnemyDied?.Invoke(this);
        EnemyManager.Instance?.UnregisterEnemy(this);

        Destroy(gameObject, 0.5f);
    }
}
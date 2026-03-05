using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked replacement for EnemyUnit.
///
/// SERVER (Host) owns all enemy objects.
/// Clients receive position and health updates via NetworkVariables.
/// EnemyAI ONLY runs on the server.
///
/// SETUP:
///   - Replace EnemyUnit component on enemy prefabs with this script.
///   - Add NetworkObject and NetworkTransform components to the prefab.
///   - Keep EnemyStats serialized field.
///   - EnemySpawner must call netObj.Spawn() after instantiation.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkedHealthComponent))]
public class NetworkedEnemyUnit : NetworkBehaviour, IHasHealth
{
    // ── Inspector ─────────────────────────────────────────────────────────
    [Header("Stats")]
    [SerializeField] private EnemyStats stats;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    // ── Network state — readable by all clients ───────────────────────────
    private NetworkVariable<int> netGridX = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> netGridZ = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> netIsDead = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ── Private runtime ───────────────────────────────────────────────────
    private GridPosition            gridPosition;
    private RoomGrid                currentRoomGrid;
    private NetworkedHealthComponent health;
    private bool                    isInitialized = false;
    private int                     turnsWaited   = 0;

    // ── Events ────────────────────────────────────────────────────────────
    public event Action<NetworkedEnemyUnit> OnEnemyDied;

    // ── Properties ────────────────────────────────────────────────────────
    public EnemyStats               Stats           => stats;
    public NetworkedHealthComponent Health          => health;
    public GridPosition             GridPosition    => gridPosition;
    public RoomGrid                 CurrentRoomGrid => currentRoomGrid;
    public bool                     IsInitialized   => isInitialized;
    public bool                     IsDead          => netIsDead.Value;

    // ── IHasHealth ────────────────────────────────────────────────────────
    public int GetMaxHealth() => stats != null ? stats.maxHealth : 100;

    // ─────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        health = GetComponent<NetworkedHealthComponent>();
    }

    public override void OnNetworkSpawn()
    {
        // Subscribe on all clients so the enemy visually disappears when dead
        health.OnDeath += HandleDeath;
        netIsDead.OnValueChanged += OnIsDeadChanged;
    }

    public override void OnNetworkDespawn()
    {
        health.OnDeath -= HandleDeath;
        netIsDead.OnValueChanged -= OnIsDeadChanged;

        // Server cleans up grid
        if (IsServer && currentRoomGrid != null && isInitialized)
        {
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetEnemyUnitCompat());
            NetworkedEnemyManager.Instance?.UnregisterEnemy(this);
        }
    }

    private void OnDestroy()
    {
        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetEnemyUnitCompat());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Placement — called by NetworkedEnemySpawner on the Server
    // ─────────────────────────────────────────────────────────────────────

    public void PlaceOnGrid(RoomGrid roomGrid, GridPosition position)
    {
        if (!IsServer) return;

        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetEnemyUnitCompat());

        currentRoomGrid = roomGrid;
        gridPosition    = position;

        transform.position = roomGrid.GetWorldPosition(position);
        roomGrid.AddEnemyAtGridPosition(position, GetEnemyUnitCompat());

        netGridX.Value = position.x;
        netGridZ.Value = position.z;

        isInitialized = true;

        if (showDebugLogs)
            Debug.Log($"[NetworkedEnemyUnit] {stats?.enemyName} placed at {position}");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Movement — Server only
    // ─────────────────────────────────────────────────────────────────────

    public void MoveToPosition(GridPosition newPosition)
    {
        if (!IsServer || !isInitialized) return;

        currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetEnemyUnitCompat());
        gridPosition = newPosition;
        currentRoomGrid.AddEnemyAtGridPosition(newPosition, GetEnemyUnitCompat());

        transform.position = currentRoomGrid.GetWorldPosition(newPosition);

        netGridX.Value = newPosition.x;
        netGridZ.Value = newPosition.z;

        if (showDebugLogs)
            Debug.Log($"[NetworkedEnemyUnit] {stats?.enemyName} moved to {newPosition}");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Turn handling — Server only
    // ─────────────────────────────────────────────────────────────────────

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

    // ─────────────────────────────────────────────────────────────────────
    // Death
    // ─────────────────────────────────────────────────────────────────────

    private void HandleDeath()
    {
        if (!IsServer) return;

        if (showDebugLogs)
            Debug.Log($"[NetworkedEnemyUnit] {stats?.enemyName} died.");

        netIsDead.Value = true;

        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetEnemyUnitCompat());

        OnEnemyDied?.Invoke(this);
        NetworkedEnemyManager.Instance?.UnregisterEnemy(this);

        // Despawn via NetworkObject after a short delay for death animation
        StartCoroutine(DespawnAfterDelay(0.5f));
    }

    private System.Collections.IEnumerator DespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (IsServer && TryGetComponent<NetworkObject>(out var netObj))
            netObj.Despawn();
    }

    private void OnIsDeadChanged(bool oldVal, bool newVal)
    {
        // All clients: visually hide the enemy when dead
        if (newVal && !IsServer)
            gameObject.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Compat shim — existing EnemyManager expects EnemyUnit not NetworkedEnemyUnit
    // ─────────────────────────────────────────────────────────────────────
    private EnemyUnit GetEnemyUnitCompat() => GetComponent<EnemyUnit>();
}
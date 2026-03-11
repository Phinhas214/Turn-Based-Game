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
///   - Add NetworkObject and NetworkTransform (Server authority) to the prefab.
///   - Keep EnemyStats serialized field.
///   - EnemySpawner must call netObj.Spawn() after instantiation.
///
/// FIXES in this version:
///   FIX 1 — GetEnemyUnitCompat() was returning null on clients because enemies
///            only have NetworkedEnemyUnit not EnemyUnit. Every RoomGrid Add/Remove
///            call was silently passing null and doing nothing. Enemies were invisible
///            to pathfinding and combat targeting on all non-host clients.
///            → See GetCompatUnit() at the bottom — two options explained.
///
///   FIX 2 — health.OnDeath fires on ALL clients via TriggerDeathClientRpc.
///            Without a guard, the server ran HandleDeath twice: once from the
///            direct event subscription, once from the ClientRpc reaching the host.
///            netIsDead was set twice and UnregisterEnemy called twice.
///            → Added hasDied guard flag.
///
///   FIX 3 — SyncMoveToClientsClientRpc updated grid occupancy but never updated
///            transform.position on clients. The visual enemy stayed at spawn.
///            NetworkTransform interpolates but only after receiving the new
///            position — snapping explicitly here ensures immediate visual update.
///            → Added transform.position = worldPos in SyncMoveToClientsClientRpc.
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
    private GridPosition             gridPosition;
    private RoomGrid                 currentRoomGrid;
    private NetworkedHealthComponent health;
    private bool                     isInitialized = false;
    private bool                     hasDied       = false;
    private int                      turnsWaited   = 0;

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
        health.OnDeath           += HandleDeath;
        netIsDead.OnValueChanged += OnIsDeadChanged;
    }

    public override void OnNetworkDespawn()
    {
        health.OnDeath           -= HandleDeath;
        netIsDead.OnValueChanged -= OnIsDeadChanged;

        if (IsServer && currentRoomGrid != null && isInitialized)
        {
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetCompatUnit());
            NetworkedEnemyManager.Instance?.UnregisterEnemy(this);
        }
    }

    private void OnDestroy()
    {
        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetCompatUnit());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Placement — called by NetworkedEnemySpawner on the Server
    // ─────────────────────────────────────────────────────────────────────

    public void PlaceOnGrid(RoomGrid roomGrid, GridPosition position)
    {
        if (!IsServer) return;

        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetCompatUnit());

        currentRoomGrid    = roomGrid;
        gridPosition       = position;
        transform.position = roomGrid.GetWorldPosition(position);
        roomGrid.AddEnemyAtGridPosition(position, GetCompatUnit());

        netGridX.Value = position.x;
        netGridZ.Value = position.z;
        isInitialized  = true;

        if (showDebugLogs)
            Debug.Log($"[NetworkedEnemyUnit] {stats?.enemyName} placed at {position}");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Movement — Server only
    // ─────────────────────────────────────────────────────────────────────

    public void MoveToPosition(GridPosition newPosition)
    {
        if (!IsServer || !isInitialized) return;

        currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetCompatUnit());
        gridPosition = newPosition;
        currentRoomGrid.AddEnemyAtGridPosition(newPosition, GetCompatUnit());

        Vector3 newWorldPos    = currentRoomGrid.GetWorldPosition(newPosition);
        transform.position     = newWorldPos;
        netGridX.Value         = newPosition.x;
        netGridZ.Value         = newPosition.z;

        // Send world pos + grid pos to clients so they update both visuals and occupancy
        SyncMoveToClientsClientRpc(newWorldPos.x, newWorldPos.y, newWorldPos.z,
                                   newPosition.x, newPosition.z);

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
        // FIX 2: TriggerDeathClientRpc fires OnDeath on ALL clients including
        // the host, so this callback runs twice on the server without the guard.
        if (!IsServer || hasDied) return;
        hasDied = true;

        if (showDebugLogs)
            Debug.Log($"[NetworkedEnemyUnit] {stats?.enemyName} died.");

        netIsDead.Value = true;

        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetCompatUnit());

        OnEnemyDied?.Invoke(this);
        NetworkedEnemyManager.Instance?.UnregisterEnemy(this);

        StartCoroutine(DespawnAfterDelay(0.5f));
    }

    private System.Collections.IEnumerator DespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (IsServer && TryGetComponent<NetworkObject>(out var netObj))
            netObj.Despawn(true);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Client sync RPCs
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called once after spawn to tell clients which room this enemy is in.
    /// </summary>
    [ClientRpc]
    public void SyncRoomToClientsClientRpc(float wx, float wy, float wz, int gx, int gz)
    {
        if (IsServer) return;

        Vector3      worldPos = new Vector3(wx, wy, wz);
        GridPosition pos      = new GridPosition(gx, gz);

        RoomGrid room = LevelGrid.Instance?.GetRoomAtPosition(worldPos);
        if (room == null)
        {
            Debug.LogWarning($"[NetworkedEnemyUnit] SyncRoomToClients: no room found at {worldPos}");
            return;
        }

        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetCompatUnit());

        currentRoomGrid    = room;
        gridPosition       = pos;
        transform.position = worldPos; // snap visual in case NT hasn't arrived yet
        room.AddEnemyAtGridPosition(pos, GetCompatUnit());
        isInitialized = true;

        if (showDebugLogs)
            Debug.Log($"[NetworkedEnemyUnit] Client synced to room {room.gameObject.name} at {pos}");
    }

    /// <summary>
    /// Called every move — syncs grid occupancy AND snaps visual position on clients.
    /// </summary>
    [ClientRpc]
    public void SyncMoveToClientsClientRpc(float wx, float wy, float wz, int gx, int gz)
    {
        if (IsServer) return;

        // If SyncRoomToClients hasn't arrived yet, try to resolve room now
        if (currentRoomGrid == null && LevelGrid.Instance != null)
            currentRoomGrid = LevelGrid.Instance.GetRoomAtPosition(new Vector3(wx, wy, wz));

        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetCompatUnit());

        GridPosition newPos = new GridPosition(gx, gz);
        gridPosition        = newPos;

        // FIX 3: update the transform so the enemy visually moves on clients.
        // NetworkTransform will then interpolate from here on subsequent frames.
        transform.position = new Vector3(wx, wy, wz);

        if (currentRoomGrid != null)
        {
            currentRoomGrid.AddEnemyAtGridPosition(newPos, GetCompatUnit());
            isInitialized = true;
        }
    }

    private void OnIsDeadChanged(bool oldVal, bool newVal)
    {
        // Non-host clients: hide visually — server handles the despawn
        if (newVal && !IsServer)
            gameObject.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────────────
    // FIX 1 — RoomGrid compat helper
    // ─────────────────────────────────────────────────────────────────────
    // The old GetEnemyUnitCompat() called GetComponent<EnemyUnit>() which returns
    // null on clients — the prefab only has NetworkedEnemyUnit. Every grid
    // Add/Remove call silently did nothing, leaving enemies invisible to pathfinding
    // and combat on all non-host clients.
    //
    // You have TWO options — pick whichever requires less change for you:
    //
    // OPTION A — Add EnemyUnit as a tag component on the enemy prefab.
    //   Keep EnemyUnit on the prefab alongside NetworkedEnemyUnit. EnemyUnit
    //   doesn't need any logic for this to work — RoomGrid just uses it as a key.
    //   No code changes needed anywhere else. This is the quickest fix.
    //
    // OPTION B — Update RoomGrid to accept NetworkedEnemyUnit.
    //   Change AddEnemyAtGridPosition / RemoveEnemyAtGridPosition to take
    //   NetworkedEnemyUnit (or an IEnemyUnit interface). Then change this helper
    //   to return 'this' instead. Cleaner architecture but requires editing RoomGrid.
    //
    // Default below uses OPTION A. To switch to OPTION B, replace the body with:
    //   return this;   (and change the method return type to NetworkedEnemyUnit)
    private EnemyUnit GetCompatUnit()
    {
        EnemyUnit eu = GetComponent<EnemyUnit>();
#if UNITY_EDITOR
        if (eu == null)
            Debug.LogError($"[NetworkedEnemyUnit] '{gameObject.name}' is missing an EnemyUnit component. " +
                           "Enemies will not register in RoomGrid on clients — add EnemyUnit to the " +
                           "prefab (Option A) or update RoomGrid to use NetworkedEnemyUnit (Option B). " +
                           "See GetCompatUnit() comments.");
#endif
        return eu;
    }
}
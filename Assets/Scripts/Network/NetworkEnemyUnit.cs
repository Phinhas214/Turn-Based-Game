using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked replacement for EnemyUnit. SERVER (Host) owns all enemy objects.
///
/// FIXES IN THIS VERSION:
///   FIX 1 — RoomGrid Add/Remove used null EnemyUnit on clients (GetCompatUnit returned null).
///   FIX 2 — hasDied guard prevents double-death on server (HandleDeath called twice because
///            TriggerDeathClientRpc fires OnDeath on the host too).
///   FIX 3 — SyncMoveToClientsClientRpc now snaps transform.position on clients.
///   FIX 4 (NEW) — RoomGrid cache: rooms are now cached at spawn instead of being
///            looked up with FindObjectsByType every RPC. FindObjectsByType inside
///            ServerRpc/ClientRpc runs during NGO's network update and causes
///            ALLOC_TEMP_TLS unfreed allocation warnings every frame.
///   FIX 5 (NEW) — OnDestroy no longer runs grid cleanup when the object is being
///            despawned normally — OnNetworkDespawn handles that. OnDestroy was
///            firing AFTER NGO already cleaned up the NetworkObject, causing the
///            MissingReferenceException chain seen in the logs.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkedHealthComponent))]
public class NetworkedEnemyUnit : NetworkBehaviour, IHasHealth
{
    [Header("Stats")]
    [SerializeField] private EnemyStats stats;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    private NetworkVariable<int> netGridX = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> netGridZ = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> netIsDead = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private GridPosition gridPosition;
    private RoomGrid currentRoomGrid;
    private NetworkedHealthComponent health;
    private bool isInitialized = false;
    private bool hasDied = false;
    private int turnsWaited = 0;

    // FIX 4: Cache all RoomGrids at spawn time rather than calling
    // FindObjectsByType<RoomGrid> inside RPCs. That call runs during NGO's
    // network update loop (ALLOC_TEMP_TLS context) and the array it allocates
    // is never freed within the same frame, generating the unfreed-allocation warning.
    private static RoomGrid[] cachedRoomGrids;
    private static bool roomGridCacheValid = false;

    public event Action<NetworkedEnemyUnit> OnEnemyDied;

    public EnemyStats Stats => stats;
    public NetworkedHealthComponent Health => health;
    public GridPosition GridPosition => gridPosition;
    public RoomGrid CurrentRoomGrid => currentRoomGrid;
    public bool IsInitialized => isInitialized;
    public bool IsDead => netIsDead.Value;

    public int GetMaxHealth() => stats != null ? stats.maxHealth : 100;

    private void Awake()
    {
        health = GetComponent<NetworkedHealthComponent>();
    }

    public override void OnNetworkSpawn()
    {
        health.OnDeath += HandleDeath;
        netIsDead.OnValueChanged += OnIsDeadChanged;

        // FIX 4: Refresh the room grid cache when a new enemy spawns.
        // This is called once per enemy spawn, not per RPC, so it's safe here.
        RefreshRoomGridCache();
    }

    public override void OnNetworkDespawn()
    {
        health.OnDeath -= HandleDeath;
        netIsDead.OnValueChanged -= OnIsDeadChanged;

        // FIX 5: Do all cleanup here in OnNetworkDespawn where the NetworkObject
        // is still valid. OnDestroy runs AFTER NGO has already destroyed the
        // NetworkObject internals, causing MissingReferenceException when code
        // tries to access netObj.name or similar in the NGO message queue.
        if (IsServer && currentRoomGrid != null && isInitialized)
        {
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetCompatUnit());
            NetworkedEnemyManager.Instance?.UnregisterEnemy(this);
        }
        else if (!IsServer && currentRoomGrid != null && isInitialized)
        {
            // Clients also need to update their local grid state
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetCompatUnit());
        }

        // Invalidate the cache so the next spawn rebuilds it
        roomGridCacheValid = false;
    }

    // FIX 5: OnDestroy should NOT do grid removal — that belongs in OnNetworkDespawn.
    // When OnDestroy runs, the NetworkObject may already be in an invalid state,
    // which is what causes the MissingReferenceException seen in the logs:
    //   NetworkObject.GetNetworkBehaviourAtOrderIndex → Object.get_name() → NRE
    // Simply leave this empty (or remove it entirely).
    private void OnDestroy()
    {
        // Intentionally empty — cleanup is handled in OnNetworkDespawn.
        // Do NOT call currentRoomGrid.RemoveEnemyAtGridPosition here;
        // the NetworkObject internals are already torn down at this point.
    }

    // ── Room grid cache helpers ───────────────────────────────────────────

    private static void RefreshRoomGridCache()
    {
        cachedRoomGrids = FindObjectsByType<RoomGrid>(FindObjectsSortMode.None);
        roomGridCacheValid = true;
    }

    /// <summary>
    /// Finds a RoomGrid by name using the cache. Safe to call from RPCs
    /// because it does not allocate a new array on every call.
    /// </summary>
    private static RoomGrid FindRoomByName(string roomName)
    {
        if (!roomGridCacheValid || cachedRoomGrids == null)
            RefreshRoomGridCache();

        if (string.IsNullOrEmpty(roomName)) return null;

        foreach (RoomGrid rg in cachedRoomGrids)
        {
            // Guard: the cached entry may have been destroyed if a room was unloaded
            if (rg != null && rg.gameObject.name == roomName)
                return rg;
        }
        return null;
    }

    private static RoomGrid FindRoomByWorldPos(Vector3 worldPos)
    {
        return LevelGrid.Instance?.GetRoomAtPosition(worldPos);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Placement — called by NetworkedEnemySpawner on the Server
    // ─────────────────────────────────────────────────────────────────────

    public void PlaceOnGrid(RoomGrid roomGrid, GridPosition position)
    {
        if (!IsServer) return;

        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetCompatUnit());

        currentRoomGrid = roomGrid;
        gridPosition = position;
        transform.position = roomGrid.GetWorldPosition(position);
        roomGrid.AddEnemyAtGridPosition(position, GetCompatUnit());

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

        currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetCompatUnit());
        gridPosition = newPosition;
        currentRoomGrid.AddEnemyAtGridPosition(newPosition, GetCompatUnit());

        Vector3 newWorldPos = currentRoomGrid.GetWorldPosition(newPosition);
        transform.position = newWorldPos;
        netGridX.Value = newPosition.x;
        netGridZ.Value = newPosition.z;

        SyncMoveToClientsClientRpc(newWorldPos.x, newWorldPos.y, newWorldPos.z,
                                   newPosition.x, newPosition.z,
                                   currentRoomGrid.gameObject.name);

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
        // the host, so this callback would run twice on the server without hasDied.
        // NetworkedHealthComponent.TriggerDeathClientRpc now also has its own
        // hasTriggeredDeathVisuals guard, but we keep hasDied here to protect
        // the server-side game logic (unregister, despawn) from running twice.
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

    private IEnumerator DespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        // FIX 5: Check IsSpawned before calling Despawn — the object may have
        // already been despawned if something else triggered cleanup first.
        if (IsServer && TryGetComponent<NetworkObject>(out var netObj) && netObj.IsSpawned)
            netObj.Despawn(true);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Client sync RPCs
    // ─────────────────────────────────────────────────────────────────────

    [ClientRpc]
    public void SyncRoomToClientsClientRpc(float wx, float wy, float wz, int gx, int gz,
                                           string roomName = "")
    {
        if (IsServer) return;

        Vector3 worldPos = new Vector3(wx, wy, wz);
        GridPosition pos = new GridPosition(gx, gz);

        // FIX 4: Use cached lookup instead of FindObjectsByType
        RoomGrid room = FindRoomByName(roomName);
        if (room == null) room = FindRoomByWorldPos(worldPos);

        if (room == null)
        {
            Debug.LogWarning($"[NetworkedEnemyUnit] SyncRoomToClients: no room found for '{roomName}' at {worldPos}");
            return;
        }

        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetCompatUnit());

        currentRoomGrid = room;
        gridPosition = pos;
        transform.position = worldPos;
        room.AddEnemyAtGridPosition(pos, GetCompatUnit());
        isInitialized = true;

        if (showDebugLogs)
            Debug.Log($"[NetworkedEnemyUnit] Client synced to room {room.gameObject.name} at {pos}");
    }

    [ClientRpc]
    public void SyncMoveToClientsClientRpc(float wx, float wy, float wz, int gx, int gz,
                                           string roomName = "")
    {
        if (IsServer) return;

        // FIX 4: Use cached lookup instead of FindObjectsByType
        if (currentRoomGrid == null)
        {
            RoomGrid found = FindRoomByName(roomName);
            if (found == null) found = FindRoomByWorldPos(new Vector3(wx, wy, wz));
            currentRoomGrid = found;
        }

        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetCompatUnit());

        GridPosition newPos = new GridPosition(gx, gz);
        gridPosition = newPos;
        // FIX 3: Snap visual position so enemy doesn't stay frozen at spawn
        transform.position = new Vector3(wx, wy, wz);

        if (currentRoomGrid != null)
        {
            currentRoomGrid.AddEnemyAtGridPosition(newPos, GetCompatUnit());
            isInitialized = true;
        }
    }

    private void OnIsDeadChanged(bool oldVal, bool newVal)
    {
        // Non-host clients: hide visually — server handles the actual despawn
        if (newVal && !IsServer)
            gameObject.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────────────
    // RoomGrid compat helper — see FIX 1 comment in original
    // ─────────────────────────────────────────────────────────────────────

    private EnemyUnit GetCompatUnit()
    {
        EnemyUnit eu = GetComponent<EnemyUnit>();
#if UNITY_EDITOR
        if (eu == null)
            Debug.LogError($"[NetworkedEnemyUnit] '{gameObject.name}' is missing an EnemyUnit component. " +
                           "Add EnemyUnit to the prefab (Option A) or update RoomGrid to use " +
                           "NetworkedEnemyUnit (Option B). See comments.");
#endif
        return eu;
    }
}
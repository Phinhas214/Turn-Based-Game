using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked EnemyManager — server-authoritative.
///
/// Only runs meaningful logic on the server/host.
/// Clients simply receive enemy state through NetworkedEnemyUnit's NetworkVariables.
///
/// KEY DIFFERENCE from original EnemyManager:
///   - Enemies target the NEAREST player, not just PlayerTarget.Instance.
///   - All registered enemies are NetworkedEnemyUnit, not EnemyUnit.
/// </summary>
public class NetworkedEnemyManager : NetworkBehaviour
{
    public static NetworkedEnemyManager Instance { get; private set; }

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    private List<NetworkedEnemyUnit>  activeEnemies          = new List<NetworkedEnemyUnit>();
    private bool                      isRunningEnemyTurns    = false;
    private HashSet<RoomGrid>         roomsRunningEnemyTurns = new HashSet<RoomGrid>();

    // ── Per-room enemy counts synced to ALL clients ───────────────────────
    // Clients cannot read activeEnemies (it is only populated on the server).
    // RoomNavigationUI calls AreEnemiesInRoom which must work on clients too
    // so the combat lock (no leaving when enemies present) works correctly.
    // We maintain a simple Dictionary<roomInstanceID, count> and push it to
    // clients whenever RegisterEnemy or UnregisterEnemy fires.
    //
    // Key = RoomGrid GameObject instanceID (int, safe to send over network).
    // Value = number of living enemies in that room.
    // Key = room GameObject name e.g. "NormalRoom_(1,0)" — deterministic from seed,
    // identical on server and all clients. Safe to send over network as a string.
    private Dictionary<string, int> roomEnemyCountCache = new Dictionary<string, int>();

    public event Action                       OnEnemyTurnsComplete;
    public event Action                       OnEnemyListChanged;
    public event Action<RoomGrid>             OnRoomCleared;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Registration — Server only
    // ─────────────────────────────────────────────────────────────────────

    public void RegisterEnemy(NetworkedEnemyUnit enemy)
    {
        if (!IsServer) return;

        if (!activeEnemies.Contains(enemy))
        {
            activeEnemies.Add(enemy);
            if (showDebugLogs)
                Debug.Log($"[NetworkedEnemyManager] Registered {enemy.Stats?.enemyName}. Total: {activeEnemies.Count}");
            OnEnemyListChanged?.Invoke();
            BroadcastRoomEnemyCounts();
        }
    }

    public void UnregisterEnemy(NetworkedEnemyUnit enemy)
    {
        if (!IsServer) return;

        RoomGrid roomOfDeadEnemy = enemy.CurrentRoomGrid;

        if (activeEnemies.Remove(enemy))
        {
            if (showDebugLogs)
                Debug.Log($"[NetworkedEnemyManager] Unregistered {enemy.Stats?.enemyName}. Remaining: {activeEnemies.Count}");

            OnEnemyListChanged?.Invoke();
            BroadcastRoomEnemyCounts();

            if (roomOfDeadEnemy != null)
            {
                var remaining = GetEnemiesInRoom(roomOfDeadEnemy);
                if (remaining.Count == 0)
                {
                    Debug.Log($"[NetworkedEnemyManager] Room cleared: {roomOfDeadEnemy.gameObject.name}");
                    OnRoomCleared?.Invoke(roomOfDeadEnemy);
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Per-room enemy count — works on ALL clients
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if there are living enemies in the given room.
    /// Works on both server and clients — use this instead of GetEnemiesInRoom
    /// whenever you only need a yes/no answer (e.g. RoomNavigationUI combat lock).
    /// </summary>
    public bool HasEnemiesInRoom(RoomGrid room)
    {
        if (room == null) return false;

        // Server has the live list — use it directly
        if (IsServer)
            return GetEnemiesInRoom(room).Count > 0;

        // Clients use the synced count cache (keyed by room name, NOT instanceID)
        return roomEnemyCountCache.TryGetValue(room.gameObject.name, out int count) && count > 0;
    }

    // Called on server whenever enemy list changes — pushes room counts to all clients
    private void BroadcastRoomEnemyCounts()
    {
        if (!IsServer) return;

        // Tally living enemies per room
        var tally = new Dictionary<RoomGrid, int>();
        foreach (var enemy in activeEnemies)
        {
            if (enemy == null || enemy.IsDead || enemy.CurrentRoomGrid == null) continue;
            if (!tally.ContainsKey(enemy.CurrentRoomGrid)) tally[enemy.CurrentRoomGrid] = 0;
            tally[enemy.CurrentRoomGrid]++;
        }

        // Use room GameObject name as key — it is set to e.g. "NormalRoom_(1,0)"
        // during generation (deterministic from seed) so it is identical on all clients.
        // GetInstanceID() must NOT be used here — it differs between server and clients
        // for the same GameObject, so the cache lookup would always miss on clients.
        // NGO cannot serialize string[] in a ClientRpc.
        // Encode as a single pipe-delimited string e.g. "NormalRoom_(1,0)|2|StartRoom_(0,0)|1"
        var parts = new System.Text.StringBuilder();
        foreach (var kvp in tally)
        {
            if (parts.Length > 0) parts.Append('|');
            parts.Append(kvp.Key.gameObject.name);
            parts.Append('|');
            parts.Append(kvp.Value);
        }

        SyncRoomEnemyCountsClientRpc(parts.ToString());
    }

    [ClientRpc]
    private void SyncRoomEnemyCountsClientRpc(string payload)
    {
        roomEnemyCountCache.Clear();

        if (!string.IsNullOrEmpty(payload))
        {
            string[] parts = payload.Split('|');
            for (int i = 0; i + 1 < parts.Length; i += 2)
            {
                if (int.TryParse(parts[i + 1], out int count))
                    roomEnemyCountCache[parts[i]] = count;
            }
        }

        OnEnemyListChanged?.Invoke();
    }

    public int GetEnemyCount() => activeEnemies.Count;

    public List<NetworkedEnemyUnit> GetAllEnemies() => new List<NetworkedEnemyUnit>(activeEnemies);

    public List<NetworkedEnemyUnit> GetEnemiesInRoom(RoomGrid room)
    {
        var result = new List<NetworkedEnemyUnit>();
        foreach (var enemy in activeEnemies)
            if (!enemy.IsDead && enemy.CurrentRoomGrid == room)
                result.Add(enemy);
        return result;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Enemy turns — Server only
    // ─────────────────────────────────────────────────────────────────────

    public void RunEnemyTurns()
    {
        if (!IsServer || isRunningEnemyTurns) return;
        StartCoroutine(RunEnemyTurnsRoutine());
    }

    /// <summary>
    /// Runs enemy turns for a specific room only. Used by the per-room turn system.
    /// onComplete is called when all enemies in this room have finished their turns.
    /// Multiple rooms can be running simultaneously without blocking each other.
    /// </summary>
    public void RunEnemyTurnsInRoom(RoomGrid room, Action onComplete)
    {
        if (!IsServer)
        {
            onComplete?.Invoke();
            return;
        }

        if (roomsRunningEnemyTurns.Contains(room))
        {
            Debug.LogWarning($"[NetworkedEnemyManager] Room {room?.gameObject.name} already running enemy turns.");
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(RunEnemyTurnsInRoomRoutine(room, onComplete));
    }

    private IEnumerator RunEnemyTurnsInRoomRoutine(RoomGrid room, Action onComplete)
    {
        roomsRunningEnemyTurns.Add(room);

        // Snapshot enemies in this room at start of turn
        var snapshot = GetEnemiesInRoom(room);

        if (showDebugLogs)
            Debug.Log($"[NetworkedEnemyManager] Room {room.gameObject.name}: running {snapshot.Count} enemy turns.");

        foreach (var enemy in snapshot)
        {
            if (enemy == null || enemy.IsDead) continue;

            // Verify enemy is still in this room (could have been removed mid-turn)
            if (enemy.CurrentRoomGrid != room) continue;

            NetworkedEnemyAI ai = enemy.GetComponent<NetworkedEnemyAI>();
            if (ai == null) continue;

            bool done = false;
            ai.TakeTurn(() => done = true);
            yield return new WaitUntil(() => done);
        }

        roomsRunningEnemyTurns.Remove(room);

        if (showDebugLogs)
            Debug.Log($"[NetworkedEnemyManager] Room {room.gameObject.name}: all enemy turns complete.");

        onComplete?.Invoke();
    }

    private IEnumerator RunEnemyTurnsRoutine()
    {
        isRunningEnemyTurns = true;

        if (showDebugLogs)
            Debug.Log($"[NetworkedEnemyManager] Running turns for {activeEnemies.Count} enemies.");

        var snapshot = new List<NetworkedEnemyUnit>(activeEnemies);

        foreach (var enemy in snapshot)
        {
            if (enemy == null || enemy.IsDead) continue;

            NetworkedEnemyAI ai = enemy.GetComponent<NetworkedEnemyAI>();
            if (ai == null) continue;

            bool done = false;
            ai.TakeTurn(() => done = true);
            yield return new WaitUntil(() => done);
        }

        isRunningEnemyTurns = false;

        if (showDebugLogs)
            Debug.Log("[NetworkedEnemyManager] All enemy turns complete.");

        OnEnemyTurnsComplete?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Nearest player query — used by EnemyAI
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the Unit that is closest (Manhattan distance) to the given enemy,
    /// and is in the same room.
    /// Returns null if no player is in the room.
    /// </summary>
    // Cache registered players to avoid FindObjectsByType every enemy turn
    private List<Unit> registeredPlayers = new List<Unit>();

    public void RegisterPlayer(Unit unit)
    {
        if (!registeredPlayers.Contains(unit))
            registeredPlayers.Add(unit);
    }

    public void UnregisterPlayer(Unit unit)
    {
        registeredPlayers.Remove(unit);
    }

    public Unit FindNearestPlayerInRoom(NetworkedEnemyUnit enemy)
    {
        if (!IsServer) return null;

        RoomGrid enemyRoom = enemy.CurrentRoomGrid;
        if (enemyRoom == null)
        {
            Debug.LogWarning($"[EnemyManager] FindNearestPlayer: enemy {enemy.Stats?.enemyName} has no room.");
            return null;
        }

        // Always do a fresh scan — the registered list can fall out of date if
        // PlaceInRoom fires on the client before NetworkedUnit.OnNetworkSpawn runs
        // on the server, leaving some players never registered.
        if (registeredPlayers.Count == 0)
        {
            foreach (Unit u in FindObjectsByType<Unit>(FindObjectsSortMode.None))
                if (u.GetComponent<Unity.Netcode.NetworkObject>() != null && !registeredPlayers.Contains(u))
                    registeredPlayers.Add(u);
        }

        // --- DEBUG: log every player's server-side room so we can verify it's correct ---
        if (showDebugLogs)
        {
            Debug.Log($"[EnemyManager] FindNearestPlayer for {enemy.Stats?.enemyName} in room '{enemyRoom.gameObject.name}'. Checking {registeredPlayers.Count} players:");
            foreach (Unit u in registeredPlayers)
            {
                if (u == null) continue;
                // Read room from BOTH Unit and NetworkedUnit so we can see if they diverge
                RoomGrid unitRoom    = u.GetCurrentRoomGrid();
                RoomGrid netUnitRoom = u.GetComponent<NetworkedUnit>()?.GetCurrentRoomGrid();
                Debug.Log($"  Player '{u.gameObject.name}' Unit.room='{unitRoom?.gameObject.name ?? "NULL"}' NetworkedUnit.room='{netUnitRoom?.gameObject.name ?? "NULL"}'");
            }
        }

        Unit nearest  = null;
        int  bestDist = int.MaxValue;

        for (int i = registeredPlayers.Count - 1; i >= 0; i--)
        {
            Unit unit = registeredPlayers[i];
            if (unit == null) { registeredPlayers.RemoveAt(i); continue; }

            // Use NetworkedUnit.GetCurrentRoomGrid() on server — it is updated by
            // UpdatePositionServerRpc every time the client moves or changes rooms.
            // Unit.GetCurrentRoomGrid() is NOT reliable on the server because
            // PlaceInRoom on Unit runs on the owning client, not the server.
            NetworkedUnit netUnit = unit.GetComponent<NetworkedUnit>();
            RoomGrid playerRoom   = netUnit != null
                ? netUnit.GetCurrentRoomGrid()
                : unit.GetCurrentRoomGrid();

            if (playerRoom != enemyRoom) continue;

            var health = unit.GetComponent<NetworkedHealthComponent>();
            if (health != null && health.IsDead) continue;

            int dist = ManhattanDist(enemy.GridPosition, unit.GetGridPosition());
            if (dist < bestDist) { bestDist = dist; nearest = unit; }
        }

        if (showDebugLogs)
            Debug.Log($"[EnemyManager] Nearest player: {nearest?.gameObject.name ?? "NONE"}");

        return nearest;
    }

    private int ManhattanDist(GridPosition a, GridPosition b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);
}
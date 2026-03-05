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

    private List<NetworkedEnemyUnit> activeEnemies     = new List<NetworkedEnemyUnit>();
    private bool                     isRunningEnemyTurns = false;

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
    public Unit FindNearestPlayerInRoom(NetworkedEnemyUnit enemy)
    {
        if (!IsServer) return null;

        RoomGrid enemyRoom = enemy.CurrentRoomGrid;
        if (enemyRoom == null) return null;

        Unit   nearest  = null;
        int    bestDist = int.MaxValue;

        // Find all PlayerTarget components — one per connected player
        foreach (PlayerTarget pt in FindObjectsByType<PlayerTarget>(FindObjectsSortMode.None))
        {
            Unit unit = pt.GetUnit();
            if (unit == null) continue;
            if (!pt.IsInRoom(enemyRoom)) continue;

            // Skip dead players
            var health = unit.GetComponent<NetworkedHealthComponent>();
            if (health != null && health.IsDead) continue;

            int dist = ManhattanDist(enemy.GridPosition, unit.GetGridPosition());
            if (dist < bestDist)
            {
                bestDist = dist;
                nearest  = unit;
            }
        }

        return nearest;
    }

    private int ManhattanDist(GridPosition a, GridPosition b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);
}
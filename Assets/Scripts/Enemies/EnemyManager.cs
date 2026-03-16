using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyManager : MonoBehaviour
{
    public static EnemyManager Instance { get; private set; }

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    private List<EnemyUnit> activeEnemies = new List<EnemyUnit>();
    private bool isRunningEnemyTurns = false;

    public event Action OnEnemyTurnsComplete;
    public event Action OnEnemyListChanged;

    /// <summary>
    /// Fired when a room is cleared of all enemies.
    /// Passes the RoomGrid that was just cleared.
    /// </summary>
    public event Action<RoomGrid> OnRoomCleared;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── Registration ───────────────────────────────────────────────────────

    public void RegisterEnemy(EnemyUnit enemy)
    {
        if (!activeEnemies.Contains(enemy))
        {
            activeEnemies.Add(enemy);

            if (showDebugLogs)
                Debug.Log($"[EnemyManager] Registered {enemy.Stats?.enemyName}. Total: {activeEnemies.Count}");

            OnEnemyListChanged?.Invoke();
        }
    }

    public void UnregisterEnemy(EnemyUnit enemy)
    {
        RoomGrid roomOfDeadEnemy = enemy.CurrentRoomGrid;

        if (activeEnemies.Remove(enemy))
        {
            if (showDebugLogs)
                Debug.Log($"[EnemyManager] Unregistered {enemy.Stats?.enemyName}. Remaining: {activeEnemies.Count}");

            OnEnemyListChanged?.Invoke();

            if (roomOfDeadEnemy != null)
            {
                List<EnemyUnit> remaining = GetEnemiesInRoom(roomOfDeadEnemy);
                if (remaining.Count == 0)
                {
                    Debug.Log($"[EnemyManager] Room cleared: {roomOfDeadEnemy.gameObject.name}");
                    OnRoomCleared?.Invoke(roomOfDeadEnemy);
                }
            }
        }
    }

    // ── ClearAllEnemies ────────────────────────────────────────────────────

    /// <summary>
    /// Destroys all active enemy GameObjects and clears the tracking list.
    /// Called by LevelGenerator.ClearLevel() before regenerating so stale
    /// enemies don't persist into the next run.
    /// </summary>
    public void ClearAllEnemies()
    {
        // Stop any running turn coroutine first so it doesn't keep iterating
        // over enemies that are about to be destroyed
        if (isRunningEnemyTurns)
        {
            StopAllCoroutines();
            isRunningEnemyTurns = false;
        }

        foreach (EnemyUnit enemy in activeEnemies)
        {
            if (enemy != null)
                Destroy(enemy.gameObject);
        }

        activeEnemies.Clear();
        OnEnemyListChanged?.Invoke();

        Debug.Log("[EnemyManager] All enemies cleared.");
    }

    // ── Queries ────────────────────────────────────────────────────────────

    public int GetEnemyCount() => activeEnemies.Count;
    public List<EnemyUnit> GetAllEnemies() => new List<EnemyUnit>(activeEnemies);

    public List<EnemyUnit> GetEnemiesInRoom(RoomGrid room)
    {
        List<EnemyUnit> result = new List<EnemyUnit>();
        foreach (EnemyUnit enemy in activeEnemies)
        {
            if (!enemy.IsDead && enemy.CurrentRoomGrid == room)
                result.Add(enemy);
        }
        return result;
    }

    // ── Turn execution ─────────────────────────────────────────────────────

    public void RunEnemyTurns()
    {
        if (isRunningEnemyTurns) return;
        StartCoroutine(RunEnemyTurnsRoutine());
    }

    private IEnumerator RunEnemyTurnsRoutine()
    {
        isRunningEnemyTurns = true;

        if (showDebugLogs)
            Debug.Log($"[EnemyManager] Running turns for {activeEnemies.Count} enemies.");

        List<EnemyUnit> snapshot = new List<EnemyUnit>(activeEnemies);

        foreach (EnemyUnit enemy in snapshot)
        {
            if (enemy == null || enemy.IsDead) continue;

            EnemyAI ai     = enemy.GetComponent<EnemyAI>();
            BossAI  bossAI = enemy.GetComponent<BossAI>();
            if (ai == null && bossAI == null) continue;

            bool done = false;
            if (bossAI != null)
                bossAI.TakeTurn(() => done = true);
            else
                ai.TakeTurn(() => done = true);

            yield return new WaitUntil(() => done);
        }

        isRunningEnemyTurns = false;

        if (showDebugLogs)
            Debug.Log("[EnemyManager] All enemy turns complete.");

        OnEnemyTurnsComplete?.Invoke();
    }
}
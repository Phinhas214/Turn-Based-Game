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
        // 1. Capture the room reference before removal
        RoomGrid roomOfDeadEnemy = enemy.CurrentRoomGrid;

        // 2. Perform the removal and check if successful (Sam's safety check)
        if (activeEnemies.Remove(enemy))
        {
            if (showDebugLogs)
                Debug.Log($"[EnemyManager] Unregistered {enemy.Stats?.enemyName}. Remaining: {activeEnemies.Count}");

            // 3. Notify UI/Systems that list changed
            OnEnemyListChanged?.Invoke();

            // 4. Handle Room Clearing Logic
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
    } // End of UnregisterEnemy

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
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
        }
    }

    public void UnregisterEnemy(EnemyUnit enemy)
    {
        RoomGrid roomOfDeadEnemy = enemy.CurrentRoomGrid;
        activeEnemies.Remove(enemy);

        if (showDebugLogs)
            Debug.Log($"[EnemyManager] Unregistered {enemy.Stats?.enemyName}. Remaining: {activeEnemies.Count}");

        // Check if that room is now empty
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

    public int             GetEnemyCount()  => activeEnemies.Count;
    public List<EnemyUnit> GetAllEnemies()  => new List<EnemyUnit>(activeEnemies);

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
            EnemyAI ai = enemy.GetComponent<EnemyAI>();
            if (ai == null) continue;

            bool done = false;
            ai.TakeTurn(() => done = true);
            yield return new WaitUntil(() => done);
        }

        isRunningEnemyTurns = false;

        if (showDebugLogs)
            Debug.Log("[EnemyManager] All enemy turns complete.");

        OnEnemyTurnsComplete?.Invoke();
    }
}
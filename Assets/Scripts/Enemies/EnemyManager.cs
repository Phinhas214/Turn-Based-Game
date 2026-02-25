using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks all active enemies and runs their turns sequentially.
/// No longer needs a player reference — EnemyAI finds the player
/// via PlayerTarget, which works regardless of spawn order.
/// </summary>
public class EnemyManager : MonoBehaviour
{
    public static EnemyManager Instance { get; private set; }

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    private List<EnemyUnit> activeEnemies = new List<EnemyUnit>();
    private bool isRunningEnemyTurns = false;

    /// <summary>Fired when all enemies have finished their turns.</summary>
    public event Action OnEnemyTurnsComplete;

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
        }
    }

    public void UnregisterEnemy(EnemyUnit enemy)
    {
        activeEnemies.Remove(enemy);
        if (showDebugLogs)
            Debug.Log($"[EnemyManager] Unregistered {enemy.Stats?.enemyName}. Remaining: {activeEnemies.Count}");
    }

    public int             GetEnemyCount()  => activeEnemies.Count;
    public List<EnemyUnit> GetAllEnemies()  => new List<EnemyUnit>(activeEnemies);

    /// <summary>
    /// Returns all enemies currently in the given room.
    /// Used by RoomNavigationUI to check if navigation should be locked.
    /// </summary>
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

            EnemyAI ai = enemy.GetComponent<EnemyAI>();
            if (ai == null) continue;

            bool done = false;
            ai.TakeTurn(() => done = true);   // no longer passes playerUnit
            yield return new WaitUntil(() => done);
        }

        isRunningEnemyTurns = false;

        if (showDebugLogs)
            Debug.Log("[EnemyManager] All enemy turns complete.");

        OnEnemyTurnsComplete?.Invoke();
    }
}
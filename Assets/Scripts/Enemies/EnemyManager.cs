using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks all active enemies in the scene.
/// Runs all enemy turns sequentially when TurnSystem fires the enemy turn.
/// Works with the existing TurnSystem — hook it up by calling RunEnemyTurns()
/// from TurnSystem or from a coordinator that knows when it's the enemy phase.
/// </summary>
public class EnemyManager : MonoBehaviour
{
    public static EnemyManager Instance { get; private set; }

    // ── Inspector ──────────────────────────────────────────────────────────
    [Header("References")]
    [Tooltip("Assign the player Unit here, or leave empty to auto-find on level ready.")]
    [SerializeField] private Unit playerUnit;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    // ── Runtime ────────────────────────────────────────────────────────────
    private List<EnemyUnit> activeEnemies = new List<EnemyUnit>();
    private bool isRunningEnemyTurns = false;

    // ── Events ─────────────────────────────────────────────────────────────
    /// <summary>Fired when all enemies have finished their turns.</summary>
    public event Action OnEnemyTurnsComplete;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady += OnLevelReady;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady -= OnLevelReady;
    }

    private void OnLevelReady()
    {
        if (playerUnit == null)
            playerUnit = FindFirstObjectByType<Unit>();
    }

    // ── Enemy registration ─────────────────────────────────────────────────

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

    public int GetEnemyCount() => activeEnemies.Count;
    public List<EnemyUnit> GetAllEnemies() => new List<EnemyUnit>(activeEnemies);

    // ── Turn execution ─────────────────────────────────────────────────────

    /// <summary>
    /// Run every active enemy's turn sequentially.
    /// Call this from TurnSystem when the enemy phase begins.
    /// </summary>
    public void RunEnemyTurns()
    {
        if (isRunningEnemyTurns) return;
        StartCoroutine(RunEnemyTurnsRoutine());
    }

    private IEnumerator RunEnemyTurnsRoutine()
    {
        isRunningEnemyTurns = true;

        if (showDebugLogs)
            Debug.Log($"[EnemyManager] Starting enemy turns. Enemies: {activeEnemies.Count}");

        // Snapshot the list so deaths mid-turn don't break iteration
        List<EnemyUnit> snapshot = new List<EnemyUnit>(activeEnemies);

        foreach (EnemyUnit enemy in snapshot)
        {
            if (enemy == null || enemy.IsDead) continue;
            if (playerUnit == null) break;

            EnemyAI ai = enemy.GetComponent<EnemyAI>();
            if (ai == null) continue;

            bool turnComplete = false;
            ai.TakeTurn(playerUnit, () => turnComplete = true);

            // Wait for this enemy to finish before moving to the next
            yield return new WaitUntil(() => turnComplete);
        }

        isRunningEnemyTurns = false;

        if (showDebugLogs)
            Debug.Log("[EnemyManager] All enemy turns complete.");

        OnEnemyTurnsComplete?.Invoke();
    }
}
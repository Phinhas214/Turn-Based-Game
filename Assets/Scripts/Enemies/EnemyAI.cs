using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controls one enemy's behaviour on its turn.
/// - If the player is NOT in the same room: does nothing (idles).
/// - If the player IS in the same room:
///     1. Pathfinds and moves toward the player (up to moveRange steps)
///     2. Attacks if the player is within attackRange
/// </summary>
[RequireComponent(typeof(EnemyUnit))]
public class EnemyAI : MonoBehaviour
{
    [Header("Movement Timing")]
    [Tooltip("Seconds between each step when the enemy moves across tiles.")]
    [SerializeField, Min(0f)] private float stepDelay = 0.15f;

    private EnemyUnit enemyUnit;

    private void Awake()
    {
        enemyUnit = GetComponent<EnemyUnit>();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Execute this enemy's full turn. Calls onComplete when finished.
    /// Called by EnemyManager during the enemy turn phase.
    /// </summary>
    public void TakeTurn(Unit playerUnit, Action onComplete)
    {
        if (!enemyUnit.CanActThisTurn() || enemyUnit.IsDead)
        {
            onComplete?.Invoke();
            return;
        }

        // Only act if the player is in the same room
        if (!IsPlayerInSameRoom(playerUnit))
        {
            if (enemyUnit.Stats != null)
                Debug.Log($"[EnemyAI] {enemyUnit.Stats.enemyName} idles — player not in room.");
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(TurnRoutine(playerUnit, onComplete));
    }

    // ── Turn coroutine ─────────────────────────────────────────────────────

    private IEnumerator TurnRoutine(Unit playerUnit, Action onComplete)
    {
        EnemyStats stats = enemyUnit.Stats;
        RoomGrid   room  = enemyUnit.CurrentRoomGrid;

        if (room == null || playerUnit == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        GridPosition enemyPos  = enemyUnit.GridPosition;
        GridPosition playerPos = playerUnit.GetGridPosition();

        // ── Move phase ─────────────────────────────────────────────────────
        int distToPlayer = ManhattanDist(enemyPos, playerPos);

        if (distToPlayer > stats.attackRange)
        {
            Pathfinder pathfinder = new Pathfinder(room);
            List<GridPosition> path = pathfinder.FindPathToRange(enemyPos, playerPos, stats.attackRange);

            if (path.Count > 0)
            {
                int stepsToTake = Mathf.Min(path.Count, stats.moveRange);

                for (int i = 0; i < stepsToTake; i++)
                {
                    if (enemyUnit.IsDead) { onComplete?.Invoke(); yield break; }

                    enemyUnit.MoveToPosition(path[i]);
                    yield return new WaitForSeconds(stepDelay);
                }

                // Recalculate after moving
                enemyPos     = enemyUnit.GridPosition;
                distToPlayer = ManhattanDist(enemyPos, playerPos);
            }
        }

        yield return new WaitForSeconds(stepDelay);

        // ── Attack phase ───────────────────────────────────────────────────
        if (distToPlayer <= stats.attackRange && !enemyUnit.IsDead)
        {
            PerformAttack(playerUnit);
            yield return new WaitForSeconds(stepDelay);
        }

        onComplete?.Invoke();
    }

    // ── Attack ─────────────────────────────────────────────────────────────

    private void PerformAttack(Unit playerUnit)
    {
        EnemyStats stats = enemyUnit.Stats;
        if (stats.attackData == null) return;

        HealthComponent playerHealth = playerUnit.GetComponent<HealthComponent>();
        if (playerHealth == null)
        {
            Debug.LogWarning("[EnemyAI] Player has no HealthComponent.");
            return;
        }

        if (stats.attackData.attackPattern != null)
        {
            GridPosition enemyPos  = enemyUnit.GridPosition;
            GridPosition playerPos = playerUnit.GetGridPosition();
            Vector2Int   facing    = GetFacingToward(enemyPos, playerPos);

            List<GridPosition> hitTiles = stats.attackData.attackPattern
                .GetAffectedPositions(enemyPos, facing);

            foreach (GridPosition tile in hitTiles)
            {
                if (tile == playerPos)
                {
                    playerHealth.TakeDamage(stats.attackData.baseDamage);
                    Debug.Log($"[EnemyAI] {stats.enemyName} hit player for {stats.attackData.baseDamage} dmg.");
                }
            }
        }
        else
        {
            playerHealth.TakeDamage(stats.attackData.baseDamage);
            Debug.Log($"[EnemyAI] {stats.enemyName} hit player for {stats.attackData.baseDamage} dmg.");
        }
    }

    // ── Room check ─────────────────────────────────────────────────────────

    private bool IsPlayerInSameRoom(Unit playerUnit)
    {
        if (playerUnit == null) return false;

        RoomGrid enemyRoom  = enemyUnit.CurrentRoomGrid;
        RoomGrid playerRoom = playerUnit.GetCurrentRoomGrid();

        // Both must be initialized and in the same room instance
        return enemyRoom != null
            && playerRoom != null
            && enemyRoom == playerRoom;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private int ManhattanDist(GridPosition a, GridPosition b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);
    }

    private Vector2Int GetFacingToward(GridPosition from, GridPosition to)
    {
        int dx = to.x - from.x;
        int dz = to.z - from.z;
        if (Mathf.Abs(dz) > Mathf.Abs(dx))
            return dz >= 0 ? new Vector2Int(0, 1) : new Vector2Int(0, -1);
        else
            return dx >= 0 ? new Vector2Int(1, 0) : new Vector2Int(-1, 0);
    }
}
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controls one enemy's behaviour on its turn.
///
/// Uses PlayerTarget to find the player — this works regardless of
/// initialization order and will naturally extend to multiplayer
/// (target the closest PlayerTarget).
///
/// Per turn:
///   - If no PlayerTarget in same room → idle
///   - If player in room but out of attack range → move closer
///   - If player in range → attack
/// </summary>
[RequireComponent(typeof(EnemyUnit))]
public class EnemyAI : MonoBehaviour
{
    [Header("Movement Timing")]
    [Tooltip("Seconds between each step when moving across tiles.")]
    [SerializeField, Min(0f)] private float stepDelay = 0.2f;

    private EnemyUnit enemyUnit;

    private void Awake()
    {
        enemyUnit = GetComponent<EnemyUnit>();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Called by EnemyManager. Runs the full turn then calls onComplete.</summary>
    public void TakeTurn(Action onComplete)
    {
        if (!enemyUnit.CanActThisTurn() || enemyUnit.IsDead)
        {
            onComplete?.Invoke();
            return;
        }

        // Find the player target
        PlayerTarget target = FindPlayerInRoom();

        if (target == null)
        {
            Debug.Log($"[EnemyAI] {enemyUnit.Stats?.enemyName} idles — no player in room.");
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(TurnRoutine(target, onComplete));
    }

    // ── Turn logic ─────────────────────────────────────────────────────────

    private IEnumerator TurnRoutine(PlayerTarget target, Action onComplete)
    {
        EnemyStats stats = enemyUnit.Stats;
        RoomGrid   room  = enemyUnit.CurrentRoomGrid;

        if (stats == null || room == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        Unit playerUnit   = target.GetUnit();
        GridPosition myPos     = enemyUnit.GridPosition;
        GridPosition playerPos = playerUnit.GetGridPosition();
        int dist = ManhattanDist(myPos, playerPos);

        // ── Move phase ─────────────────────────────────────────────────────
        if (dist > stats.attackRange)
        {
            Pathfinder pathfinder = new Pathfinder(room);
            List<GridPosition> path = pathfinder.FindPathToRange(myPos, playerPos, stats.attackRange);

            if (path.Count > 0)
            {
                int steps = Mathf.Min(path.Count, stats.moveRange);
                for (int i = 0; i < steps; i++)
                {
                    if (enemyUnit.IsDead) { onComplete?.Invoke(); yield break; }
                    enemyUnit.MoveToPosition(path[i]);
                    yield return new WaitForSeconds(stepDelay);
                }

                // Recalculate distance after moving
                myPos = enemyUnit.GridPosition;
                dist  = ManhattanDist(myPos, playerPos);
            }
        }

        yield return new WaitForSeconds(stepDelay);

        // ── Attack phase ───────────────────────────────────────────────────
        if (dist <= stats.attackRange && !enemyUnit.IsDead)
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
        if (stats.attackData == null)
        {
            Debug.LogWarning($"[EnemyAI] {stats?.enemyName} has no attackData assigned in EnemyStats.");
            return;
        }

        HealthComponent playerHealth = playerUnit.GetComponent<HealthComponent>();
        if (playerHealth == null)
        {
            Debug.LogWarning("[EnemyAI] Player has no HealthComponent — cannot deal damage.");
            return;
        }

        // Use pattern if one is set, otherwise hit directly
        if (stats.attackData.attackPattern != null)
        {
            GridPosition myPos     = enemyUnit.GridPosition;
            GridPosition playerPos = playerUnit.GetGridPosition();
            Vector2Int   facing    = GetFacingToward(myPos, playerPos);

            List<GridPosition> hitTiles = stats.attackData.attackPattern
                .GetAffectedPositions(myPos, facing);

            bool hit = false;
            foreach (GridPosition tile in hitTiles)
            {
                if (tile == playerPos)
                {
                    playerHealth.TakeDamage(stats.attackData.baseDamage);
                    hit = true;
                    Debug.Log($"[EnemyAI] {stats.enemyName} hit player for {stats.attackData.baseDamage} dmg.");
                }
            }

            if (!hit)
            {
                // Pattern didn't reach — fall back to direct hit
                playerHealth.TakeDamage(stats.attackData.baseDamage);
                Debug.Log($"[EnemyAI] {stats.enemyName} hit player (direct) for {stats.attackData.baseDamage} dmg.");
            }
        }
        else
        {
            playerHealth.TakeDamage(stats.attackData.baseDamage);
            Debug.Log($"[EnemyAI] {stats.enemyName} hit player for {stats.attackData.baseDamage} dmg.");
        }
    }

    // ── Player detection ───────────────────────────────────────────────────

    /// <summary>
    /// Returns the PlayerTarget if they are in the same room as this enemy.
    /// Uses PlayerTarget component — no fragile grid comparison.
    /// </summary>
    private PlayerTarget FindPlayerInRoom()
    {
        PlayerTarget target = PlayerTarget.Instance;
        if (target == null) return null;

        RoomGrid enemyRoom = enemyUnit.CurrentRoomGrid;
        if (enemyRoom == null) return null;

        return target.IsInRoom(enemyRoom) ? target : null;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private int ManhattanDist(GridPosition a, GridPosition b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);

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
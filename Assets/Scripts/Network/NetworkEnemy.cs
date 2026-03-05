using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked EnemyAI — runs on SERVER ONLY.
///
/// Differences from original EnemyAI:
///   - Uses NetworkedEnemyManager.FindNearestPlayerInRoom() instead of PlayerTarget.Instance.
///   - Reads/writes NetworkedEnemyUnit instead of EnemyUnit.
///   - All movement and damage goes through NetworkedHealthComponent.TakeDamage()
///     which is already server-authoritative.
/// </summary>
[RequireComponent(typeof(NetworkedEnemyUnit))]
public class NetworkedEnemyAI : NetworkBehaviour
{
    [Header("Movement Timing")]
    [SerializeField, Min(0f)] private float stepDelay = 0.2f;

    private NetworkedEnemyUnit enemyUnit;

    private void Awake()
    {
        enemyUnit = GetComponent<NetworkedEnemyUnit>();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Public API — called by NetworkedEnemyManager on Server
    // ─────────────────────────────────────────────────────────────────────

    public void TakeTurn(Action onComplete)
    {
        // AI only runs on server
        if (!IsServer)
        {
            onComplete?.Invoke();
            return;
        }

        if (!enemyUnit.CanActThisTurn() || enemyUnit.IsDead)
        {
            onComplete?.Invoke();
            return;
        }

        Unit target = NetworkedEnemyManager.Instance?.FindNearestPlayerInRoom(enemyUnit);

        if (target == null)
        {
            if (enemyUnit.Stats?.alwaysChases == true)
            {
                // Optional: move toward the nearest room with players
                // For now, idle
            }
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(TurnRoutine(target, onComplete));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Turn logic
    // ─────────────────────────────────────────────────────────────────────

    private IEnumerator TurnRoutine(Unit targetUnit, Action onComplete)
    {
        EnemyStats stats = enemyUnit.Stats;
        RoomGrid   room  = enemyUnit.CurrentRoomGrid;

        if (stats == null || room == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        GridPosition myPos     = enemyUnit.GridPosition;
        GridPosition playerPos = targetUnit.GetGridPosition();
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
            PerformAttack(targetUnit, myPos, playerPos);
            yield return new WaitForSeconds(stepDelay);
        }

        onComplete?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Attack — Server only, uses NetworkedHealthComponent
    // ─────────────────────────────────────────────────────────────────────

    private void PerformAttack(Unit targetUnit, GridPosition myPos, GridPosition playerPos)
    {
        EnemyStats stats = enemyUnit.Stats;

        if (stats.attackData == null)
        {
            Debug.LogWarning($"[NetworkedEnemyAI] {stats?.enemyName} has no attackData.");
            return;
        }

        NetworkedHealthComponent playerHealth = targetUnit.GetComponent<NetworkedHealthComponent>();
        if (playerHealth == null)
        {
            // Fall back to old HealthComponent
            HealthComponent oldHealth = targetUnit.GetComponent<HealthComponent>();
            oldHealth?.TakeDamage(stats.attackData.baseDamage);
            Debug.Log($"[NetworkedEnemyAI] {stats.enemyName} hit player for {stats.attackData.baseDamage} dmg (old HC).");
            return;
        }

        if (stats.attackData.attackPattern != null)
        {
            Vector2Int facing = GetFacingToward(myPos, playerPos);
            List<GridPosition> hitTiles = stats.attackData.attackPattern
                .GetAffectedPositions(myPos, facing);

            bool hit = false;
            foreach (GridPosition tile in hitTiles)
            {
                if (tile == playerPos)
                {
                    playerHealth.TakeDamage(stats.attackData.baseDamage);
                    hit = true;
                    Debug.Log($"[NetworkedEnemyAI] {stats.enemyName} hit player for {stats.attackData.baseDamage} dmg.");
                }
            }

            if (!hit)
            {
                // Pattern missed — direct hit fallback
                playerHealth.TakeDamage(stats.attackData.baseDamage);
                Debug.Log($"[NetworkedEnemyAI] {stats.enemyName} hit player (direct) for {stats.attackData.baseDamage} dmg.");
            }
        }
        else
        {
            playerHealth.TakeDamage(stats.attackData.baseDamage);
            Debug.Log($"[NetworkedEnemyAI] {stats.enemyName} hit player for {stats.attackData.baseDamage} dmg.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

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
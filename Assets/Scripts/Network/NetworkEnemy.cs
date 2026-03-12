using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked EnemyAI — runs on SERVER ONLY.
///
/// FIXES IN THIS VERSION:
///   1. Player position read from NetworkedUnit.GetGridPosition() (server-authoritative
///      NetworkVariables) instead of Unit.GetGridPosition() which is only reliable for
///      the host. Non-host client Unit.gridPosition was always (0,0) on the server,
///      causing enemies to walk to spawn tile and attack there regardless of player pos.
///
///   2. Target re-evaluated every move step — enemy switches to the nearest living
///      player dynamically rather than locking on one target for the whole turn.
///
///   3. Attack pattern miss fallback REMOVED — if the pattern does not cover the
///      player tile, the attack misses. Old code always dealt damage even on miss.
///
///   4. Dead player filtering — skips players whose NetworkedHealthComponent.IsDead.
/// </summary>
[RequireComponent(typeof(NetworkedEnemyUnit))]
public class NetworkedEnemyAI : NetworkBehaviour
{
    [Header("Movement Timing")]
    [SerializeField, Min(0f)] private float stepDelay = 0.2f;

    private NetworkedEnemyUnit enemyUnit;

    private void Awake() => enemyUnit = GetComponent<NetworkedEnemyUnit>();

    // ─────────────────────────────────────────────────────────────────────
    // Public API — called by NetworkedEnemyManager on Server
    // ─────────────────────────────────────────────────────────────────────

    public void TakeTurn(Action onComplete)
    {
        if (!IsServer)                                       { onComplete?.Invoke(); return; }
        if (!enemyUnit.CanActThisTurn() || enemyUnit.IsDead){ onComplete?.Invoke(); return; }
        StartCoroutine(TurnRoutine(onComplete));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Turn coroutine
    // ─────────────────────────────────────────────────────────────────────

    private IEnumerator TurnRoutine(Action onComplete)
    {
        EnemyStats stats = enemyUnit.Stats;
        RoomGrid   room  = enemyUnit.CurrentRoomGrid;
        if (stats == null || room == null) { onComplete?.Invoke(); yield break; }

        // ── Move phase ────────────────────────────────────────────────────
        // Re-evaluate nearest living player at the start of each step so the
        // enemy dynamically switches targets if someone else gets closer mid-move.
        int stepsLeft = stats.moveRange;
        while (stepsLeft > 0)
        {
            if (enemyUnit.IsDead) { onComplete?.Invoke(); yield break; }

            var (bestUnit, bestPos) = FindNearestLivingPlayer(room);
            if (bestUnit == null) break;

            GridPosition myPos = enemyUnit.GridPosition;
            if (ManhattanDist(myPos, bestPos) <= stats.attackRange) break;

            List<GridPosition> path = new Pathfinder(room).FindPathToRange(myPos, bestPos, stats.attackRange);
            if (path.Count == 0) break;

            // Skip tiles occupied by another enemy or a living player.
            // We own this data — no need for RoomGrid to expose a query method.
            GridPosition nextStep = path[0];
            if (IsTileOccupied(nextStep, room)) break; // blocked — stop moving this turn

            enemyUnit.MoveToPosition(nextStep);
            stepsLeft--;
            yield return new WaitForSeconds(stepDelay);
        }

        yield return new WaitForSeconds(stepDelay);
        if (enemyUnit.IsDead) { onComplete?.Invoke(); yield break; }

        // ── Attack phase ──────────────────────────────────────────────────
        // Re-evaluate target after moving — might be a different player now.
        {
            var (bestUnit, bestPos) = FindNearestLivingPlayer(room);
            if (bestUnit != null)
            {
                GridPosition myPos = enemyUnit.GridPosition;
                if (ManhattanDist(myPos, bestPos) <= stats.attackRange)
                {
                    PerformAttack(bestUnit, myPos, bestPos, stats);
                    yield return new WaitForSeconds(stepDelay);
                }
            }
        }

        onComplete?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Find nearest living player in room using NetworkedUnit positions
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Uses NetworkedUnit.GetGridPosition() which reads netGridX/netGridZ
    /// NetworkVariables — reliable for ALL clients on the server.
    /// DO NOT use Unit.GetGridPosition() here: it is only correct for the host.
    /// Non-host client Unit.gridPosition stays at (0,0) on the server because
    /// Unit.PlaceInRoom only runs on the owning client, not the server.
    /// </summary>
    private (Unit unit, GridPosition pos) FindNearestLivingPlayer(RoomGrid room)
    {
        Unit         best     = null;
        GridPosition bestPos  = default;
        int          bestDist = int.MaxValue;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            var health = client.PlayerObject.GetComponent<NetworkedHealthComponent>();
            if (health != null && health.IsDead) continue;

            var netUnit = client.PlayerObject.GetComponent<NetworkedUnit>();
            if (netUnit == null) continue;
            if (netUnit.GetCurrentRoomGrid() != room) continue;

            GridPosition pos  = netUnit.GetGridPosition();
            int          dist = ManhattanDist(enemyUnit.GridPosition, pos);

            if (dist < bestDist)
            {
                bestDist = dist;
                best     = client.PlayerObject.GetComponent<Unit>();
                bestPos  = pos;
            }
        }

        return (best, bestPos);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Attack
    // ─────────────────────────────────────────────────────────────────────

    private void PerformAttack(Unit target, GridPosition myPos, GridPosition playerPos, EnemyStats stats)
    {
        if (stats.attackData == null)
        {
            Debug.LogWarning($"[NetworkedEnemyAI] {stats.enemyName} has no attackData.");
            return;
        }

        var netHealth = target.GetComponent<NetworkedHealthComponent>();
        if (netHealth == null)
        {
            target.GetComponent<HealthComponent>()?.TakeDamage(stats.attackData.baseDamage);
            return;
        }

        if (stats.attackData.attackPattern != null)
        {
            Vector2Int         facing   = GetFacingToward(myPos, playerPos);
            List<GridPosition> hitTiles = stats.attackData.attackPattern.GetAffectedPositions(myPos, facing);

            foreach (GridPosition tile in hitTiles)
            {
                if (tile == playerPos)
                {
                    netHealth.TakeDamage(stats.attackData.baseDamage);
                    Debug.Log($"[NetworkedEnemyAI] {stats.enemyName} hit {target.name} for {stats.attackData.baseDamage}.");
                    return;
                }
            }
            // Pattern did not cover player tile — attack misses, no damage
            Debug.Log($"[NetworkedEnemyAI] {stats.enemyName} missed {target.name} (pattern miss).");
        }
        else
        {
            netHealth.TakeDamage(stats.attackData.baseDamage);
            Debug.Log($"[NetworkedEnemyAI] {stats.enemyName} hit {target.name} for {stats.attackData.baseDamage}.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    // ─────────────────────────────────────────────────────────────────────
    // Occupancy check — prevents enemies stacking on each other or on players
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the given tile is occupied by another enemy or a living player.
    /// We build this from data we already own — no RoomGrid query method required.
    /// </summary>
    private bool IsTileOccupied(GridPosition pos, RoomGrid room)
    {
        // Check other enemies in this room
        var enemies = NetworkedEnemyManager.Instance?.GetEnemiesInRoom(room);
        if (enemies != null)
        {
            foreach (var other in enemies)
            {
                if (other == enemyUnit) continue;
                if (other == null || other.IsDead) continue;
                if (other.GridPosition == pos) return true;
            }
        }

        // Check living players — downed players don't block tiles (they're on the ground)
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;
            var health = client.PlayerObject.GetComponent<NetworkedHealthComponent>();
            if (health != null && (health.IsDead || health.IsDown)) continue;
            var netUnit = client.PlayerObject.GetComponent<NetworkedUnit>();
            if (netUnit == null || netUnit.GetCurrentRoomGrid() != room) continue;
            if (netUnit.GetGridPosition() == pos) return true;
        }

        return false;
    }

    private int ManhattanDist(GridPosition a, GridPosition b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);

    private Vector2Int GetFacingToward(GridPosition from, GridPosition to)
    {
        int dx = to.x - from.x;
        int dz = to.z - from.z;
        return Mathf.Abs(dz) > Mathf.Abs(dx)
            ? (dz >= 0 ? new Vector2Int(0, 1) : new Vector2Int(0, -1))
            : (dx >= 0 ? new Vector2Int(1, 0) : new Vector2Int(-1, 0));
    }
}
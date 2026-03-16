using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AI for a ranged enemy. Behaviour per turn:
///
///   1. Find the nearest player in the same room.
///   2. If no player → idle.
///   3. If player is within attackRange AND this enemy has line-of-sight → attack.
///   4. If player is out of range OR no LoS → move closer until LoS + range is achieved.
///      The enemy STOPS moving as soon as it can shoot; it does NOT close to melee.
///   5. Optional kiting: if player walks too close, the enemy retreats before attacking.
///
/// Line-of-sight uses a Bresenham tile walk — any non-walkable tile blocks the shot.
/// Attach this INSTEAD OF EnemyAI on ranged enemy prefabs.
/// </summary>
[RequireComponent(typeof(EnemyUnit))]
public class RangedEnemyAI : MonoBehaviour
{
    [Header("Movement Timing")]
    [SerializeField, Min(0f)] private float stepDelay = 0.2f;

    // kiteEnabled and kiteRange are read from EnemyStats — configure them on the asset.

    private EnemyUnit enemyUnit;

    private void Awake() => enemyUnit = GetComponent<EnemyUnit>();

    // ── Public API ─────────────────────────────────────────────────────────

    public void TakeTurn(Action onComplete)
    {
        if (!enemyUnit.CanActThisTurn() || enemyUnit.IsDead) { onComplete?.Invoke(); return; }

        PlayerTarget target = FindPlayerInRoom();
        if (target == null) { Debug.Log($"[RangedEnemyAI] {enemyUnit.Stats?.enemyName} idles."); onComplete?.Invoke(); return; }

        StartCoroutine(TurnRoutine(target, onComplete));
    }

    // ── Turn logic ─────────────────────────────────────────────────────────

    private IEnumerator TurnRoutine(PlayerTarget target, Action onComplete)
    {
        EnemyStats stats = enemyUnit.Stats;
        RoomGrid   room  = enemyUnit.CurrentRoomGrid;
        if (stats == null || room == null) { onComplete?.Invoke(); yield break; }

        Unit         playerUnit = target.GetUnit();
        GridPosition myPos      = enemyUnit.GridPosition;
        GridPosition playerPos  = playerUnit.GetGridPosition();
        int          dist       = ManhattanDist(myPos, playerPos);

        // ── Kiting: retreat if player is within kite range ─────────────────
        if (stats.kiteEnabled && dist < stats.kiteRange)
        {
            GridPosition? fleePos = FindFleePosition(myPos, playerPos, room, stats.moveRange);
            if (fleePos.HasValue)
            {
                enemyUnit.MoveToPosition(fleePos.Value);
                yield return new WaitForSeconds(stepDelay);
                myPos = enemyUnit.GridPosition;
                dist  = ManhattanDist(myPos, playerPos);
            }
        }
        // ── Move: approach until in range with clear LoS ───────────────────
        else if (dist > stats.attackRange || !HasLineOfSight(myPos, playerPos, room))
        {
            Pathfinder         pathfinder = new Pathfinder(room);
            List<GridPosition> path       = pathfinder.FindPathToRange(myPos, playerPos, stats.attackRange);

            int steps = Mathf.Min(path.Count, stats.moveRange);
            for (int i = 0; i < steps; i++)
            {
                if (enemyUnit.IsDead) { onComplete?.Invoke(); yield break; }

                GridPosition next    = path[i];
                int          newDist = ManhattanDist(next, playerPos);

                // Stop as soon as stepping here gives us range + LoS
                if (newDist <= stats.attackRange && HasLineOfSight(next, playerPos, room))
                    break;

                // Don't step onto a tile already occupied by another enemy or the player
                if (IsTileOccupied(next, room)) break;

                enemyUnit.MoveToPosition(next);
                yield return new WaitForSeconds(stepDelay);
            }

            myPos = enemyUnit.GridPosition;
            dist  = ManhattanDist(myPos, playerPos);
        }

        yield return new WaitForSeconds(stepDelay);

        // ── Attack ─────────────────────────────────────────────────────────
        if (!enemyUnit.IsDead && dist <= stats.attackRange && HasLineOfSight(myPos, playerPos, room))
        {
            PerformAttack(playerUnit);
            yield return new WaitForSeconds(stepDelay);
        }

        onComplete?.Invoke();
    }

    // ── Line of sight (Bresenham) ──────────────────────────────────────────

    private bool HasLineOfSight(GridPosition from, GridPosition to, RoomGrid room)
    {
        TilemapRoomGrid tilemapGrid = room.GetTilemapRoomGrid();
        if (tilemapGrid == null) return true;

        int x0 = from.x, z0 = from.z;
        int x1 = to.x,   z1 = to.z;
        int dx = Mathf.Abs(x1 - x0), dz = Mathf.Abs(z1 - z0);
        int sx = x0 < x1 ? 1 : -1, sz = z0 < z1 ? 1 : -1;
        int err = dx - dz;

        while (true)
        {
            bool isEndpoint = (x0 == from.x && z0 == from.z) || (x0 == to.x && z0 == to.z);
            if (!isEndpoint && !tilemapGrid.IsWalkable(new GridPosition(x0, z0)))
                return false;

            if (x0 == x1 && z0 == z1) break;

            int e2 = 2 * err;
            if (e2 > -dz) { err -= dz; x0 += sx; }
            if (e2 <  dx) { err += dx; z0 += sz; }
        }

        return true;
    }

    // ── Kiting ─────────────────────────────────────────────────────────────

    private GridPosition? FindFleePosition(GridPosition myPos, GridPosition playerPos,
                                           RoomGrid room, int moveRange)
    {
        TilemapRoomGrid tilemapGrid = room.GetTilemapRoomGrid();
        if (tilemapGrid == null) return null;

        GridPosition? best     = null;
        int           bestDist = ManhattanDist(myPos, playerPos);

        for (int dx = -moveRange; dx <= moveRange; dx++)
        for (int dz = -moveRange; dz <= moveRange; dz++)
        {
            if (Mathf.Abs(dx) + Mathf.Abs(dz) > moveRange) continue;
            GridPosition c = new GridPosition(myPos.x + dx, myPos.z + dz);
            if (!tilemapGrid.IsWalkable(c)) continue;
            if (IsTileOccupied(c, room)) continue;
            int d = ManhattanDist(c, playerPos);
            if (d > bestDist) { bestDist = d; best = c; }
        }

        return best;
    }

    // ── Tile occupation ────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the tile is already occupied by another living enemy or the player.
    /// Prevents enemies from stacking on the same tile during movement.
    /// </summary>
    private bool IsTileOccupied(GridPosition pos, RoomGrid room)
    {
        // Check other enemies in this room
        var enemies = EnemyManager.Instance?.GetEnemiesInRoom(room);
        if (enemies != null)
            foreach (EnemyUnit other in enemies)
            {
                if (other == enemyUnit || other == null || other.IsDead) continue;
                if (other.GridPosition == pos) return true;
            }

        // Check the player
        PlayerTarget pt = PlayerTarget.Instance;
        if (pt != null)
        {
            Unit playerUnit = pt.GetUnit();
            if (playerUnit != null)
            {
                var health = playerUnit.GetComponent<HealthComponent>();
                if (health == null || !health.IsDead)
                    if (playerUnit.GetGridPosition() == pos) return true;
            }
        }

        return false;
    }

    // ── Attack ─────────────────────────────────────────────────────────────

    private void PerformAttack(Unit playerUnit)
    {
        EnemyStats stats = enemyUnit.Stats;
        if (stats.attackData == null) { Debug.LogWarning($"[RangedEnemyAI] {stats?.enemyName} has no attackData."); return; }

        HealthComponent health = playerUnit.GetComponent<HealthComponent>();
        if (health == null) { Debug.LogWarning("[RangedEnemyAI] Player has no HealthComponent."); return; }

        int damage = stats.attackData.CalculateDamage();

        // AttackSpritePopup.Show(stats.attackData, playerUnit.transform.position);
        // Nudge half a tile forward and to the right
        AttackSpritePopup.Show(stats.attackData, playerUnit.transform.position,
            offset: new Vector3(0f, 2f, 5f));

        if (stats.attackData.attackPattern != null)
        {
            GridPosition myPos     = enemyUnit.GridPosition;
            GridPosition playerPos = playerUnit.GetGridPosition();
            Vector2Int   facing    = GetFacingToward(myPos, playerPos);

            List<GridPosition> hitTiles = stats.attackData.attackPattern.GetAffectedPositions(myPos, facing);
            bool hit = false;
            foreach (GridPosition tile in hitTiles)
                if (tile == playerPos) { health.TakeDamage(damage); hit = true; break; }

            if (!hit)
                health.TakeDamage(damage);
        }
        else
        {
            health.TakeDamage(damage);
        }

        Debug.Log($"[RangedEnemyAI] {stats.enemyName} shot player for {damage} dmg.");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private PlayerTarget FindPlayerInRoom()
    {
        PlayerTarget target = PlayerTarget.Instance;
        RoomGrid room = enemyUnit.CurrentRoomGrid;
        if (target == null || room == null) return null;
        return target.IsInRoom(room) ? target : null;
    }

    private int ManhattanDist(GridPosition a, GridPosition b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);

    private Vector2Int GetFacingToward(GridPosition from, GridPosition to)
    {
        int dx = to.x - from.x, dz = to.z - from.z;
        return Mathf.Abs(dz) > Mathf.Abs(dx)
            ? (dz >= 0 ? new Vector2Int(0, 1) : new Vector2Int(0, -1))
            : (dx >= 0 ? new Vector2Int(1, 0) : new Vector2Int(-1, 0));
    }
}
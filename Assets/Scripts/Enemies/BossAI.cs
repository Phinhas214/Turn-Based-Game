using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the boss's per-turn behaviour by combining:
///   - BossUnit  (state: invisible, wave progression, combat mode)
///   - EnemyUnit (grid position, stats, health)
///
/// Per turn:
///   1. OnTurnStart: tick cooldowns, check proximity + minion reveals.
///   2. TrySpawnNextWave if no wave is in progress.
///   3. If invisible → skip movement and attack (boss hides).
///   4. Otherwise, behave like melee (EnemyAI) or ranged (RangedEnemyAI)
///      depending on the randomly chosen CombatMode.
///   5. OnAttackPerformed: potentially trigger invisibility.
///
/// Attach this INSTEAD OF EnemyAI / RangedEnemyAI on boss prefabs.
/// </summary>
[RequireComponent(typeof(EnemyUnit))]
[RequireComponent(typeof(BossUnit))]
public class BossAI : MonoBehaviour
{
    [Header("Movement Timing")]
    [SerializeField, Min(0f)] private float stepDelay = 0.2f;

    // kiteEnabled/kiteRange for ranged phase are read from BossStats.rangedRetreatRange / rangedPreferredRange.

    private EnemyUnit enemyUnit;
    private BossUnit  bossUnit;

    private void Awake()
    {
        enemyUnit = GetComponent<EnemyUnit>();
        bossUnit  = GetComponent<BossUnit>();
    }

    // ── Public API (mirrors EnemyAI.TakeTurn for EnemyManager compatibility) ─

    public void TakeTurn(Action onComplete)
    {
        if (!enemyUnit.CanActThisTurn() || enemyUnit.IsDead) { onComplete?.Invoke(); return; }

        PlayerTarget target = FindPlayerInRoom();
        if (target == null)
        {
            Debug.Log($"[BossAI] Boss idles — no player in room.");
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
        if (stats == null || room == null) { onComplete?.Invoke(); yield break; }

        // 1. Tick cooldowns and check auto-reveals
        bossUnit.OnTurnStart();

        // 2. Try to spawn next wave
        bossUnit.TrySpawnNextWave();

        yield return new WaitForSeconds(stepDelay);

        // 3. Skip combat while invisible
        if (bossUnit.IsInvisible)
        {
            Debug.Log("[BossAI] Boss is invisible — skipping combat this turn.");
            onComplete?.Invoke();
            yield break;
        }

        // 4. Combat — delegate to melee or ranged logic
        Unit playerUnit = target.GetUnit();
        if (bossUnit.CombatMode == BossStats.BossCombatMode.Melee)
            yield return StartCoroutine(MeleeTurn(playerUnit, stats, room));
        else
            yield return StartCoroutine(RangedTurn(playerUnit, stats, room));

        onComplete?.Invoke();
    }

    // ── Melee turn (same logic as EnemyAI) ────────────────────────────────

    private IEnumerator MeleeTurn(Unit playerUnit, EnemyStats stats, RoomGrid room)
    {
        GridPosition myPos     = enemyUnit.GridPosition;
        GridPosition playerPos = playerUnit.GetGridPosition();
        int          dist      = ManhattanDist(myPos, playerPos);

        if (dist > stats.attackRange)
        {
            Pathfinder         pathfinder = new Pathfinder(room);
            List<GridPosition> path       = pathfinder.FindPathToRange(myPos, playerPos, stats.attackRange);

            int steps = Mathf.Min(path.Count, stats.moveRange);
            for (int i = 0; i < steps; i++)
            {
                if (enemyUnit.IsDead) yield break;
                if (IsTileOccupied(path[i], room)) break;
                enemyUnit.MoveToPosition(path[i]);
                yield return new WaitForSeconds(stepDelay);
            }

            myPos = enemyUnit.GridPosition;
            dist  = ManhattanDist(myPos, playerPos);
        }

        yield return new WaitForSeconds(stepDelay);

        if (!enemyUnit.IsDead && dist <= stats.attackRange)
        {
            PerformAttack(playerUnit);
            bossUnit.OnAttackPerformed();
            yield return new WaitForSeconds(stepDelay);
        }
    }

    // ── Ranged turn (same logic as RangedEnemyAI) ─────────────────────────

    private IEnumerator RangedTurn(Unit playerUnit, EnemyStats stats, RoomGrid room)
    {
        GridPosition myPos     = enemyUnit.GridPosition;
        GridPosition playerPos = playerUnit.GetGridPosition();
        int          dist      = ManhattanDist(myPos, playerPos);

        // Kiting
        BossStats bossStats = bossUnit.BossStats;
        if (bossStats != null && dist < bossStats.rangedRetreatRange)
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
        else if (dist > stats.attackRange || !HasLineOfSight(myPos, playerPos, room))
        {
            Pathfinder         pathfinder = new Pathfinder(room);
            List<GridPosition> path       = pathfinder.FindPathToRange(myPos, playerPos, stats.attackRange);

            int steps = Mathf.Min(path.Count, stats.moveRange);
            for (int i = 0; i < steps; i++)
            {
                if (enemyUnit.IsDead) yield break;
                GridPosition next    = path[i];
                int          newDist = ManhattanDist(next, playerPos);
                if (newDist <= stats.attackRange && HasLineOfSight(next, playerPos, room)) break;
                if (IsTileOccupied(next, room)) break;
                enemyUnit.MoveToPosition(next);
                yield return new WaitForSeconds(stepDelay);
            }

            myPos = enemyUnit.GridPosition;
            dist  = ManhattanDist(myPos, playerPos);
        }

        yield return new WaitForSeconds(stepDelay);

        if (!enemyUnit.IsDead && dist <= stats.attackRange && HasLineOfSight(myPos, playerPos, room))
        {
            PerformAttack(playerUnit);
            bossUnit.OnAttackPerformed();
            yield return new WaitForSeconds(stepDelay);
        }
    }

    // ── Attack ─────────────────────────────────────────────────────────────

    private void PerformAttack(Unit playerUnit)
    {
        EnemyStats stats = enemyUnit.Stats;
        if (stats.attackData == null) { Debug.LogWarning("[BossAI] No attackData on boss stats."); return; }

        HealthComponent health = playerUnit.GetComponent<HealthComponent>();
        if (health == null) { Debug.LogWarning("[BossAI] Player has no HealthComponent."); return; }

        int damage = stats.attackData.CalculateDamage();

        AttackSpritePopup.Show(stats.attackData, playerUnit.transform.position);

        if (stats.attackData.attackPattern != null)
        {
            GridPosition myPos     = enemyUnit.GridPosition;
            GridPosition playerPos = playerUnit.GetGridPosition();
            Vector2Int   facing    = GetFacingToward(myPos, playerPos);

            List<GridPosition> hitTiles = stats.attackData.attackPattern.GetAffectedPositions(myPos, facing);
            bool hit = false;
            foreach (GridPosition tile in hitTiles)
                if (tile == playerPos) { health.TakeDamage(damage); hit = true; break; }
            if (!hit) health.TakeDamage(damage);
        }
        else
        {
            health.TakeDamage(damage);
        }

        Debug.Log($"[BossAI] Boss ({bossUnit.CombatMode}) hit player for {damage} dmg.");
    }

    // ── LoS (Bresenham) ────────────────────────────────────────────────────

    private bool HasLineOfSight(GridPosition from, GridPosition to, RoomGrid room)
    {
        TilemapRoomGrid tilemapGrid = room.GetTilemapRoomGrid();
        if (tilemapGrid == null) return true;

        int x0 = from.x, z0 = from.z, x1 = to.x, z1 = to.z;
        int dx = Mathf.Abs(x1 - x0), dz = Mathf.Abs(z1 - z0);
        int sx = x0 < x1 ? 1 : -1, sz = z0 < z1 ? 1 : -1;
        int err = dx - dz;

        while (true)
        {
            bool endpoint = (x0 == from.x && z0 == from.z) || (x0 == to.x && z0 == to.z);
            if (!endpoint && !tilemapGrid.IsWalkable(new GridPosition(x0, z0))) return false;
            if (x0 == x1 && z0 == z1) break;
            int e2 = 2 * err;
            if (e2 > -dz) { err -= dz; x0 += sx; }
            if (e2 <  dx) { err += dx; z0 += sz; }
        }
        return true;
    }

    // ── Kite helper ────────────────────────────────────────────────────────

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

    private bool IsTileOccupied(GridPosition pos, RoomGrid room)
    {
        var enemies = EnemyManager.Instance?.GetEnemiesInRoom(room);
        if (enemies != null)
            foreach (EnemyUnit other in enemies)
            {
                if (other == enemyUnit || other == null || other.IsDead) continue;
                if (other.GridPosition == pos) return true;
            }

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
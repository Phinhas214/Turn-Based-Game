using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controls one enemy's behaviour on its turn.
/// Enemies will not stack on top of each other or on the player's tile.
/// </summary>
[RequireComponent(typeof(EnemyUnit))]
public class EnemyAI : MonoBehaviour
{
    [Header("Movement Timing")]
    [SerializeField, Min(0f)] private float stepDelay = 0.2f;

    private EnemyUnit enemyUnit;

    private void Awake()
    {
        enemyUnit = GetComponent<EnemyUnit>();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void TakeTurn(Action onComplete)
    {
        if (!enemyUnit.CanActThisTurn() || enemyUnit.IsDead)
        {
            onComplete?.Invoke();
            return;
        }

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

        Unit         playerUnit = target.GetUnit();
        GridPosition myPos      = enemyUnit.GridPosition;
        GridPosition playerPos  = playerUnit.GetGridPosition();
        int          dist       = ManhattanDist(myPos, playerPos);

        // ── Move phase ─────────────────────────────────────────────────────
        if (dist > stats.attackRange)
        {
            Pathfinder         pathfinder = new Pathfinder(room);
            List<GridPosition> path       = pathfinder.FindPathToRange(myPos, playerPos, stats.attackRange);

            if (path.Count > 0)
            {
                int steps = Mathf.Min(path.Count, stats.moveRange);
                for (int i = 0; i < steps; i++)
                {
                    if (enemyUnit.IsDead) { onComplete?.Invoke(); yield break; }

                    // Don't step onto a tile already occupied by another enemy or the player
                    if (IsTileOccupied(path[i], room)) break;

                    enemyUnit.MoveToPosition(path[i]);
                    yield return new WaitForSeconds(stepDelay);
                }

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
            Debug.LogWarning($"[EnemyAI] {stats?.enemyName} has no attackData assigned.");
            return;
        }

        HealthComponent playerHealth = playerUnit.GetComponent<HealthComponent>();
        if (playerHealth == null)
        {
            Debug.LogWarning("[EnemyAI] Player has no HealthComponent.");
            return;
        }

        int damage = stats.attackData.CalculateDamage();

        AttackSpritePopup.Show(stats.attackData, playerUnit.transform.position);

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
                    playerHealth.TakeDamage(damage);
                    hit = true;
                    Debug.Log($"[EnemyAI] {stats.enemyName} hit player for {damage} dmg.");
                }
            }

            if (!hit)
            {
                playerHealth.TakeDamage(damage);
                Debug.Log($"[EnemyAI] {stats.enemyName} hit player (direct) for {damage} dmg.");
            }
        }
        else
        {
            playerHealth.TakeDamage(damage);
            Debug.Log($"[EnemyAI] {stats.enemyName} hit player for {damage} dmg.");
        }
    }

    // ── Tile occupation ────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the tile is already occupied by another living enemy or the player.
    /// Prevents enemies from stacking on the same tile during movement.
    /// </summary>
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

    // ── Player detection ───────────────────────────────────────────────────

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
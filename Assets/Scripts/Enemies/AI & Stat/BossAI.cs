using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drop-in replacement for EnemyAI on the boss prefab.
/// Melee attacker that goes invisible every N turns after attacking.
/// Revealed when player gets within revealRadius tiles.
/// </summary>
[RequireComponent(typeof(EnemyUnit))]
public class BossAI : MonoBehaviour
{
    [Header("Movement Timing")]
    [SerializeField, Min(0f)] private float stepDelay = 0.2f;

    [Header("Invisibility")]
    [Tooltip("Goes invisible every N turns. 0 = never.")]
    [SerializeField] private int goInvisEveryNTurns = 3;
    [Tooltip("How many turns the boss stays invisible before auto-revealing.")]
    [SerializeField] private int maxInvisTurns = 2;
    [Tooltip("Player must be within this many tiles to reveal the boss.")]
    [SerializeField] private int revealRadius = 3;
    [Tooltip("Child GameObject containing renderers to hide. Must NOT include colliders.")]
    [SerializeField] private GameObject visualRoot;

    private EnemyUnit enemyUnit;
    private int       turnsSinceLastInvis = 0;
    private int       invisTurnsRemaining = 0;
    private bool      isInvisible         = false;

    private void Awake()
    {
        enemyUnit = GetComponent<EnemyUnit>();
    }

    private void Start()
    {
        SetVisible(true);
    }

    // ── Called by EnemyManager ─────────────────────────────────────────────

    public void TakeTurn(Action onComplete)
    {
        if (!enemyUnit.CanActThisTurn() || enemyUnit.IsDead) { onComplete?.Invoke(); return; }

        PlayerTarget target = FindPlayerInRoom();

        // Tick invisibility even if no player in room
        TickInvisibility(target);

        if (target == null)
        {
            Debug.Log("[BossAI] No player in room — idling.");
            onComplete?.Invoke();
            return;
        }

        if (isInvisible)
        {
            Debug.Log("[BossAI] Boss is invisible — skipping combat.");
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(MeleeTurn(target, onComplete));
    }

    // ── Invisibility ───────────────────────────────────────────────────────

    private void TickInvisibility(PlayerTarget target)
    {
        if (isInvisible)
        {
            invisTurnsRemaining--;

            // Check proximity reveal
            if (target != null)
            {
                Unit player = target.GetUnit();
                if (player != null)
                {
                    int dist = Mathf.Abs(enemyUnit.GridPosition.x - player.GetGridPosition().x)
                             + Mathf.Abs(enemyUnit.GridPosition.z - player.GetGridPosition().z);
                    if (dist <= revealRadius) Reveal();
                }
            }

            if (invisTurnsRemaining <= 0) Reveal();
        }
        else
        {
            turnsSinceLastInvis++;
            if (goInvisEveryNTurns > 0 && turnsSinceLastInvis >= goInvisEveryNTurns)
            {
                GoInvisible();
                turnsSinceLastInvis = 0;
            }
        }
    }

    private void GoInvisible()
    {
        isInvisible         = true;
        invisTurnsRemaining = maxInvisTurns;
        SetVisible(false);
        Debug.Log("[BossAI] Boss went invisible.");
    }

    private void Reveal()
    {
        isInvisible = false;
        SetVisible(true);
        Debug.Log("[BossAI] Boss revealed.");
    }

    private void SetVisible(bool visible)
    {
        if (visualRoot != null)
            visualRoot.SetActive(visible);
    }

    // ── Melee turn ─────────────────────────────────────────────────────────

    private IEnumerator MeleeTurn(PlayerTarget target, Action onComplete)
    {
        EnemyStats stats = enemyUnit.Stats;
        RoomGrid   room  = enemyUnit.CurrentRoomGrid;

        if (stats == null || room == null) { onComplete?.Invoke(); yield break; }

        Unit         playerUnit = target.GetUnit();
        GridPosition myPos      = enemyUnit.GridPosition;
        GridPosition playerPos  = playerUnit.GetGridPosition();
        int          dist       = ManhattanDist(myPos, playerPos);

        // Move toward player if out of range
        if (dist > stats.attackRange)
        {
            Pathfinder         pathfinder = new Pathfinder(room);
            List<GridPosition> path       = pathfinder.FindPathToRange(myPos, playerPos, stats.attackRange);

            int steps = Mathf.Min(path.Count, stats.moveRange);
            for (int i = 0; i < steps; i++)
            {
                if (enemyUnit.IsDead) { onComplete?.Invoke(); yield break; }
                if (IsTileOccupied(path[i], room)) break;
                enemyUnit.MoveToPosition(path[i]);
                yield return new WaitForSeconds(stepDelay);
            }

            myPos = enemyUnit.GridPosition;
            dist  = ManhattanDist(myPos, playerPos);
        }

        yield return new WaitForSeconds(stepDelay);

        // Attack if in range
        if (!enemyUnit.IsDead && dist <= stats.attackRange)
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
        if (stats.attackData == null) { Debug.LogWarning("[BossAI] No attackData assigned."); return; }

        HealthComponent health = playerUnit.GetComponent<HealthComponent>();
        if (health == null) return;

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

        Debug.Log($"[BossAI] Boss hit player for {damage} dmg.");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

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

    private PlayerTarget FindPlayerInRoom()
    {
        PlayerTarget target = PlayerTarget.Instance;
        if (target == null || enemyUnit.CurrentRoomGrid == null) return null;
        return target.IsInRoom(enemyUnit.CurrentRoomGrid) ? target : null;
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
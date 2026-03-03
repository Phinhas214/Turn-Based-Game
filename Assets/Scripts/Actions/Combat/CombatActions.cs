using System;
using System.Collections.Generic;
using UnityEngine;

public class CombatAction : BaseAction
{
    [Header("Action Configuration")]
    [SerializeField] private CombatActionData actionData;

    [Header("Facing Correction")]
    [Range(0, 3)]
    [Tooltip("Number of 90-degree CCW rotations to correct facing.\n" +
             "Try 1 if pattern appears rotated 90 degrees.")]
    [SerializeField] private int facingRotationSteps = 1;

    [Header("Debug")]
    [SerializeField] private bool debugGizmos = false;

    private Vector2Int currentFacing = new Vector2Int(0, 1);
    private List<GridPosition> lastPreviewPositions = new List<GridPosition>();

    public CombatActionData ActionData => actionData;
    public void SetActionData(CombatActionData data) => actionData = data;

    public override string GetActionName() =>
        actionData != null ? actionData.actionName : "Attack";

    // ── Preview — called every frame by TilemapGridVisual ─────────────────

    public List<GridPosition> GetPreviewPositions(GridPosition mouseGridPos)
    {
        if (actionData == null) return new List<GridPosition>();

        GridPosition unitPos = unit.GetGridPosition();

        if (actionData.rotatesToFacing)
            currentFacing = ApplyFacingCorrection(GetFacingToward(unitPos, mouseGridPos));

        List<GridPosition> positions;

        if (IsRanged())
        {
            // Ranged: pattern centered on mouse tile if in range
            positions = IsInRange(unitPos, mouseGridPos)
                ? GetPatternAt(mouseGridPos, currentFacing)
                : new List<GridPosition>();
        }
        else
        {
            // Melee: pattern centered on player, rotates toward mouse
            positions = GetPatternAt(unitPos, currentFacing);
        }

        lastPreviewPositions = positions;
        return positions;
    }

    // ── Attack execution ───────────────────────────────────────────────────

    public void PerformAttack(GridPosition targetGridPos, Action onComplete)
    {
        if (actionData == null)
        {
            Debug.LogError($"[CombatAction] {gameObject.name} has no CombatActionData!");
            onComplete?.Invoke();
            return;
        }

        this.onActionComplete = onComplete;
        isActive = true;

        GridPosition unitPos = unit.GetGridPosition();

        if (actionData.rotatesToFacing)
            currentFacing = ApplyFacingCorrection(GetFacingToward(unitPos, targetGridPos));

        // Ranged: detonate at clicked tile
        // Melee: detonate at player position
        List<GridPosition> hitPositions = IsRanged()
            ? GetPatternAt(targetGridPos, currentFacing)
            : GetPatternAt(unitPos, currentFacing);

        SpendStamina();
        ApplyDamage(hitPositions);

        isActive = false;
        onActionComplete?.Invoke();
    }

    // ── Valid targets ──────────────────────────────────────────────────────

    public List<GridPosition> GetValidActionGridPositionList()
    {
        List<GridPosition> valid = new List<GridPosition>();
        if (actionData == null) return valid;

        RoomGrid room = unit.GetCurrentRoomGrid();
        if (room == null) return valid;

        GridPosition unitPos = unit.GetGridPosition();

        if (IsRanged())
        {
            // Ring between minRange and maxRange
            for (int x = -actionData.maxRange; x <= actionData.maxRange; x++)
            {
                for (int z = -actionData.maxRange; z <= actionData.maxRange; z++)
                {
                    int dist = Mathf.Abs(x) + Mathf.Abs(z);
                    if (dist < actionData.minRange || dist > actionData.maxRange) continue;
                    if (dist == 0 && !actionData.canTargetSelf) continue;

                    GridPosition candidate = new GridPosition(unitPos.x + x, unitPos.z + z);
                    if (!room.IsValidGridPosition(candidate)) continue;
                    valid.Add(candidate);
                }
            }
        }
        else
        {
            // Melee: all tiles reachable by pattern in any direction
            foreach (Vector2Int facing in CardinalDirections())
                foreach (GridPosition p in GetPatternAt(unitPos, facing))
                    if (room.IsValidGridPosition(p) && !valid.Contains(p))
                        valid.Add(p);
        }

        Debug.Log($"[CombatAction] {GetActionName()} valid positions: {valid.Count}");
        return valid;
    }

    public bool IsValidTarget(GridPosition gridPos) =>
        GetValidActionGridPositionList().Contains(gridPos);

    public bool CanAfford() =>
        playerStats == null ||
        !actionData.requiresEnoughStamina ||
        playerStats.currentStamina >= actionData.staminaCost;

    // ── Damage ─────────────────────────────────────────────────────────────

    private void ApplyDamage(List<GridPosition> positions)
    {
        RoomGrid room = unit.GetCurrentRoomGrid();
        if (room == null) return;

        foreach (GridPosition pos in positions)
        {
            if (!room.IsValidGridPosition(pos)) continue;

            TilemapRoomGrid tilemapGrid = room.GetTilemapRoomGrid();
            if (tilemapGrid == null) continue;

            foreach (EnemyUnit enemy in tilemapGrid.GetEnemiesAtGridPosition(pos))
            {
                if (enemy == null || enemy.IsDead) continue;
                Debug.Log($"[CombatAction] {GetActionName()} hit " +
                          $"{enemy.Stats?.enemyName} for {actionData.baseDamage} dmg.");
                enemy.Health.TakeDamage(actionData.baseDamage);
            }

            foreach (Unit target in tilemapGrid.GetUnitsAtGridPosition(pos))
            {
                if (target == unit && !actionData.canTargetSelf) continue;
                HealthComponent health = target.GetComponent<HealthComponent>();
                health?.TakeDamage(actionData.baseDamage);
            }
        }
    }

    private void SpendStamina()
    {
        if (playerStats == null) return;
        playerStats.currentStamina = Mathf.Max(0,
            playerStats.currentStamina - actionData.staminaCost);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private List<GridPosition> GetPatternAt(GridPosition origin, Vector2Int facing)
    {
        if (actionData.attackPattern == null)
            return new List<GridPosition> { origin };
        return actionData.attackPattern.GetAffectedPositions(origin, facing);
    }

    private bool IsRanged() => actionData != null && actionData.maxRange > 0;

    private bool IsInRange(GridPosition from, GridPosition to)
    {
        int dist = Mathf.Abs(from.x - to.x) + Mathf.Abs(from.z - to.z);
        return dist >= actionData.minRange && dist <= actionData.maxRange;
    }

    private Vector2Int ApplyFacingCorrection(Vector2Int facing)
    {
        Vector2Int f = facing;
        for (int i = 0; i < facingRotationSteps; i++)
            f = new Vector2Int(-f.y, f.x);
        return f;
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

    private static readonly List<Vector2Int> _cardinals = new List<Vector2Int>
    {
        new Vector2Int( 0,  1),
        new Vector2Int( 0, -1),
        new Vector2Int( 1,  0),
        new Vector2Int(-1,  0),
    };

    private IEnumerable<Vector2Int> CardinalDirections() => _cardinals;

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!debugGizmos || !Application.isPlaying) return;
        if (UnitActionSystem.Instance?.GetSelectedAction() != this) return;

        RoomGrid room = unit?.GetCurrentRoomGrid();
        if (room == null) return;

        Vector3 unitWorld = room.GetWorldPosition(unit.GetGridPosition());
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(unitWorld,
            unitWorld + new Vector3(currentFacing.x, 0, currentFacing.y) * 1.5f);
        Gizmos.DrawSphere(
            unitWorld + new Vector3(currentFacing.x, 0, currentFacing.y) * 1.5f, 0.15f);

        Gizmos.color = Color.red;
        foreach (GridPosition gp in lastPreviewPositions)
            if (room.IsValidGridPosition(gp))
                Gizmos.DrawSphere(room.GetWorldPosition(gp), 0.2f);
    }
#endif
}
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Combat action driven by a CombatActionData asset.
///
/// If your highlights appear rotated (North shows East etc.), set
/// FacingRotationSteps in the Inspector to correct it:
///   0 = no correction      (camera faces Z+, grid Z+ = screen up)
///   1 = 90 CCW correction  (highlights shifted 1 step CW → fix with 1 step CCW)
///   2 = 180 correction
///   3 = 90 CW correction
///
/// Since "North on mouse = East highlights" = 1 step CW wrong, set it to 1.
/// </summary>
public class CombatAction : BaseAction
{
    [Header("Action Configuration")]
    [Tooltip("The data asset that defines this attack's damage, range, pattern, and costs.")]
    [SerializeField] private CombatActionData actionData;

    [Header("Facing Correction")]
    [Tooltip("Rotates the detected facing direction to compensate for camera orientation.\n" +
             "If North mouse = East highlight: set to 1\n" +
             "If North mouse = South highlight: set to 2\n" +
             "If North mouse = West highlight: set to 3\n" +
             "If North mouse = North highlight: set to 0 (correct, no change needed)")]
    [Range(0, 3)]
    [SerializeField] private int facingRotationSteps = 1;

    [Header("Debug")]
    [Tooltip("Draw facing arrow and hit-tile spheres in the Scene view while selected.")]
    [SerializeField] private bool debugGizmos = false;

    // ── Runtime state ──────────────────────────────────────────────────────
    private Vector2Int currentFacing = new Vector2Int(0, 1);
    private List<GridPosition> lastPreviewPositions = new List<GridPosition>();

    // ── Public accessors ───────────────────────────────────────────────────
    public CombatActionData ActionData => actionData;
    public void SetActionData(CombatActionData data) => actionData = data;
    public override string GetActionName() =>
        actionData != null ? actionData.actionName : "Attack";

    // ── Preview ────────────────────────────────────────────────────────────

    public List<GridPosition> GetPreviewPositions(GridPosition mouseGridPos)
    {
        if (actionData == null) return new List<GridPosition>();

        GridPosition unitPos = unit.GetGridPosition();

        if (actionData.rotatesToFacing)
            currentFacing = ApplyFacingCorrection(GetFacingToward(unitPos, mouseGridPos));

        List<GridPosition> positions;

        if (IsRanged())
        {
            positions = IsInRange(unitPos, mouseGridPos)
                ? GetPatternAt(mouseGridPos, currentFacing)
                : new List<GridPosition>();
        }
        else
        {
            positions = GetPatternAt(unitPos, currentFacing);
        }

        lastPreviewPositions = positions;
        return positions;
    }

    // ── Execution ──────────────────────────────────────────────────────────

    public void PerformAttack(GridPosition targetGridPos, Action onComplete)
    {
        if (actionData == null)
        {
            Debug.LogError($"[CombatAction] {gameObject.name} has no CombatActionData assigned!");
            onComplete?.Invoke();
            return;
        }

        this.onActionComplete = onComplete;
        isActive = true;

        GridPosition unitPos = unit.GetGridPosition();

        if (actionData.rotatesToFacing)
            currentFacing = ApplyFacingCorrection(GetFacingToward(unitPos, targetGridPos));

        List<GridPosition> hitPositions = IsRanged()
            ? GetPatternAt(targetGridPos, currentFacing)
            : GetPatternAt(unitPos, currentFacing);

        SpendStamina();
        ApplyDamage(hitPositions);

        isActive = false;
        onActionComplete?.Invoke();
    }

    // ── Validity ───────────────────────────────────────────────────────────

    public List<GridPosition> GetValidActionGridPositionList()
    {
        List<GridPosition> valid = new List<GridPosition>();
        if (actionData == null) return valid;

        RoomGrid room = unit.GetCurrentRoomGrid();
        if (room == null) return valid;

        GridPosition unitPos = unit.GetGridPosition();

        if (IsRanged())
        {
            int range = actionData.maxRange;
            for (int x = -range; x <= range; x++)
            {
                for (int z = -range; z <= range; z++)
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
            foreach (Vector2Int facing in CardinalDirections())
            {
                foreach (GridPosition p in GetPatternAt(unitPos, facing))
                {
                    if (room.IsValidGridPosition(p) && !valid.Contains(p))
                        valid.Add(p);
                }
            }
        }

        return valid;
    }

    public bool IsValidTarget(GridPosition gridPos) =>
        GetValidActionGridPositionList().Contains(gridPos);

    public bool CanAfford() =>
        playerStats == null ||
        !actionData.requiresEnoughStamina ||
        playerStats.currentStamina >= actionData.staminaCost;

    // ── Facing correction ──────────────────────────────────────────────────

    /// <summary>
    /// Rotates the raw facing vector by facingRotationSteps * 90 degrees CCW.
    /// This compensates for camera orientation mismatches.
    ///
    /// Steps:
    ///   0 → no change
    ///   1 → 90 CCW:  (x,z) → (-z, x)
    ///   2 → 180:     (x,z) → (-x,-z)
    ///   3 → 90 CW:   (x,z) → ( z,-x)
    /// </summary>
    private Vector2Int ApplyFacingCorrection(Vector2Int facing)
    {
        Vector2Int f = facing;
        for (int i = 0; i < facingRotationSteps; i++)
            f = new Vector2Int(-f.y, f.x); // one step CCW
        return f;
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private void SpendStamina()
    {
        if (playerStats == null) return;
        playerStats.currentStamina = Mathf.Max(0, playerStats.currentStamina - actionData.staminaCost);
    }

    private void ApplyDamage(List<GridPosition> positions)
    {
        RoomGrid room = unit.GetCurrentRoomGrid();
        if (room == null) return;

        foreach (GridPosition pos in positions)
        {
            if (!room.IsValidGridPosition(pos)) continue;
            if (!room.HasAnyUnitOnGridPosition(pos)) continue;

            GridObject gridObj = room.GetGridSystem().GetGridObject(pos);
            foreach (Unit target in new List<Unit>(gridObj.GetUnitList()))
            {
                if (target == unit && !actionData.canTargetSelf) continue;
                HealthComponent health = target.GetComponent<HealthComponent>();
                if (health != null)
                    health.TakeDamage(actionData.baseDamage);
                else
                    Debug.Log($"[CombatAction] {target.name} hit for {actionData.baseDamage} (no HealthComponent).");
            }
        }
    }

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

    /// <summary>
    /// Returns the cardinal direction from 'from' toward 'to' in grid space.
    /// Z+ = North, X+ = East.
    /// Vertical (Z) axis wins on ties so front-facing patterns feel natural.
    /// </summary>
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

    // ── Debug gizmos ───────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!debugGizmos || !Application.isPlaying) return;
        if (UnitActionSystem.Instance?.GetSelectedAction() != this) return;

        RoomGrid room = unit?.GetCurrentRoomGrid();
        if (room == null) return;

        // Yellow arrow = current facing direction after correction
        Vector3 unitWorld = room.GetWorldPosition(unit.GetGridPosition());
        Vector3 facingWorld = new Vector3(currentFacing.x, 0, currentFacing.y) * 1.5f;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(unitWorld, unitWorld + facingWorld);
        Gizmos.DrawSphere(unitWorld + facingWorld, 0.15f);

        // Red spheres = preview hit tiles
        Gizmos.color = Color.red;
        foreach (GridPosition gp in lastPreviewPositions)
            if (room.IsValidGridPosition(gp))
                Gizmos.DrawSphere(room.GetWorldPosition(gp), 0.2f);
    }
#endif
}
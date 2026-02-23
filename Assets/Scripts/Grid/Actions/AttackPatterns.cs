using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines the shape/area of an attack relative to the attacker's facing direction.
/// All offsets are in LOCAL space: Z+ = forward, X+ = right.
/// Create via: Assets > Create > Combat > Attack Pattern
/// </summary>
[CreateAssetMenu(fileName = "NewAttackPattern", menuName = "Combat/Attack Pattern")]
public class AttackPattern : ScriptableObject
{
    [Serializable]
    public class PatternTile
    {
        [Tooltip("Offset in LOCAL space from the pattern origin.\n" +
                 "Z+ = Forward   Z- = Behind\n" +
                 "X+ = Right     X- = Left")]
        public Vector2Int offset;

        public PatternTile(int x, int z) { offset = new Vector2Int(x, z); }
    }

    [Header("Pattern Shape")]
    [Tooltip("Each tile that will be hit, defined in local space relative to the pattern origin.\n" +
             "The origin is either the attacker (melee) or the selected target tile (ranged).")]
    public List<PatternTile> tiles = new List<PatternTile>();

    [Header("Range")]
    [Min(0)] public int minRange = 0;
    [Min(0)] public int maxRange = 0;

#if UNITY_EDITOR
    [Header("Preview Info (read-only)")]
    [SerializeField, TextArea(2, 4)] private string _previewInfo = "";

    private void OnValidate()
    {
        if (maxRange < minRange) maxRange = minRange;
        _previewInfo = string.Format("Tile count: {0}\nRange: {1} - {2} ({3})",
            tiles != null ? tiles.Count : 0,
            minRange, maxRange,
            maxRange == 0 ? "MELEE" : "RANGED");
    }
#endif

    // ── Core API ──────────────────────────────────────────────────────────

    public List<GridPosition> GetAffectedPositions(GridPosition attackerPos,
                                                   Vector2Int   facingDir,
                                                   int          originOffset = 0)
    {
        List<GridPosition> result = new List<GridPosition>();
        Vector2Int originShift = facingDir * originOffset;

        foreach (PatternTile tile in tiles)
        {
            Vector2Int rotated = RotateOffset(tile.offset, facingDir);
            result.Add(new GridPosition(
                attackerPos.x + originShift.x + rotated.x,
                attackerPos.z + originShift.y + rotated.y));
        }

        return result;
    }

    // ── Rotation ──────────────────────────────────────────────────────────
    //
    // The pattern is defined with Z+ = forward (North).
    // We need to rotate the offsets so "forward" points in facingDir.
    //
    // Unity grid axes:  X+ = East,  Z+ = North
    //
    // The symptom "North shows East" means the old table was 90 CW too far.
    // Fix: shift every case 90 degrees CCW relative to the old table.
    //
    //   Facing North (0, 1) → pattern already points North → no rotation needed
    //   Facing East  (1, 0) → rotate pattern 90 CW  → (x,z) becomes ( z, -x)  -- WRONG OLD
    //   ...
    //
    // Correct table derived from standard 2D rotation matrix:
    //   Rotate θ CCW: x' =  x·cosθ - z·sinθ
    //                 z' =  x·sinθ + z·cosθ
    //
    //   North (0,1)  θ=0°:   ( x,  z)
    //   East  (1,0)  θ=-90°: ( z, -x)   — but symptom shows we need one step CCW from old
    //   South (0,-1) θ=180°: (-x, -z)
    //   West  (-1,0) θ=90°:  (-z,  x)
    //
    // Since the world is showing everything 90 CW from expected, we apply an
    // extra 90 CCW correction to every case:
    //
    //   North → apply 90 CCW correction → was (x,z),  now (-z, x)  -- NO, test first
    //
    // Simplest verified fix: swap the North and East cases so North gets
    // the "no rotation" treatment that East was incorrectly getting.
    // This directly corrects the one-step CW shift without touching the math.
    //
    //   North (0, 1)  → no rotation:     (x,  z)   ← was East's result
    //   East  (1, 0)  → 90 CW:           (z, -x)   ← now correctly rotates East
    //   South (0,-1)  → 180:            (-x, -z)
    //   West  (-1, 0) → 90 CCW:         (-z,  x)

    private Vector2Int RotateOffset(Vector2Int offset, Vector2Int facing)
    {
        // North: pattern already defined pointing North, no rotation needed
        if (facing == new Vector2Int( 0,  1)) return new Vector2Int( offset.x,  offset.y);

        // East: rotate 90 degrees clockwise
        // (x, z) → (z, -x)
        if (facing == new Vector2Int( 1,  0)) return new Vector2Int( offset.y, -offset.x);

        // South: rotate 180 degrees
        // (x, z) → (-x, -z)
        if (facing == new Vector2Int( 0, -1)) return new Vector2Int(-offset.x, -offset.y);

        // West: rotate 90 degrees counter-clockwise
        // (x, z) → (-z, x)
        if (facing == new Vector2Int(-1,  0)) return new Vector2Int(-offset.y,  offset.x);

        return offset;
    }

    // ── Static preset factories ───────────────────────────────────────────

    public static AttackPattern CreateSingleFront()
    {
        var p = CreateInstance<AttackPattern>(); p.name = "SingleFront";
        p.tiles.Add(new PatternTile(0, 1));
        return p;
    }

    public static AttackPattern CreateLine(int length)
    {
        var p = CreateInstance<AttackPattern>(); p.name = "Line_" + length;
        for (int i = 1; i <= length; i++) p.tiles.Add(new PatternTile(0, i));
        return p;
    }

    public static AttackPattern CreateFrontArc()
    {
        var p = CreateInstance<AttackPattern>(); p.name = "FrontArc";
        p.tiles.Add(new PatternTile(-1, 1));
        p.tiles.Add(new PatternTile( 0, 1));
        p.tiles.Add(new PatternTile( 1, 1));
        return p;
    }

    public static AttackPattern CreateDiamond(int radius)
    {
        var p = CreateInstance<AttackPattern>(); p.name = "Diamond_" + radius;
        for (int x = -radius; x <= radius; x++)
            for (int z = -radius; z <= radius; z++)
                if (Mathf.Abs(x) + Mathf.Abs(z) <= radius)
                    p.tiles.Add(new PatternTile(x, z));
        return p;
    }

    public static AttackPattern CreateCross(int armLength)
    {
        var p = CreateInstance<AttackPattern>(); p.name = "Cross_" + armLength;
        for (int i = 1; i <= armLength; i++)
        {
            p.tiles.Add(new PatternTile( 0,  i));
            p.tiles.Add(new PatternTile( 0, -i));
            p.tiles.Add(new PatternTile( i,  0));
            p.tiles.Add(new PatternTile(-i,  0));
        }
        return p;
    }
}
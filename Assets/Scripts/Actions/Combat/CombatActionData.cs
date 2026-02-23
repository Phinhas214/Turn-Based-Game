using UnityEngine;

/// <summary>
/// Data asset that fully defines one combat action.
/// Create via: Assets > Create > Combat > Combat Action Data
/// Assign to a CombatAction component on the player unit.
/// </summary>
[CreateAssetMenu(fileName = "NewCombatAction", menuName = "Combat/Combat Action Data")]
public class CombatActionData : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Display name shown in the action button UI.")]
    public string actionName = "Attack";

    [Tooltip("Optional icon shown in the action button UI.")]
    public Sprite icon;

    [Header("Damage")]
    [Tooltip("Flat damage dealt to every unit on every tile the pattern covers.")]
    [Min(0)]
    public int baseDamage = 10;

    [Header("Attack Pattern")]
    [Tooltip("ScriptableObject defining which tiles are hit relative to the attacker/target. Leave empty to hit only the single clicked tile.")]
    public AttackPattern attackPattern;

    [Tooltip("If true the pattern rotates so forward always points toward the clicked tile. If false the pattern is applied in its raw North-facing orientation.")]
    public bool rotatesToFacing = true;

    [Header("Range")]
    [Tooltip("Minimum range in tiles (Manhattan distance) to a valid target. 0 = adjacent or self. Usually 0 for melee.")]
    [Min(0)]
    public int minRange = 0;

    [Tooltip("Maximum range in tiles (Manhattan distance) to a valid target. 0 = melee (pattern fixed at attacker). >0 = ranged (player selects a target tile).")]
    [Min(0)]
    public int maxRange = 0;

    [Tooltip("If true the attacker can target their own tile. Useful for self-buffs or stomp AoEs.")]
    public bool canTargetSelf = false;

    [Header("Stamina Cost")]
    [Tooltip("Stamina points deducted from the unit when this action is used.")]
    [Min(0)]
    public int staminaCost = 2;

    [Tooltip("If true the action is blocked when the unit has insufficient stamina.")]
    public bool requiresEnoughStamina = true;

    [Header("Visual Feedback")]
    [Tooltip("Color of AoE preview tiles shown while hovering over a target.")]
    public Color aoeHighlightColor = new Color(1f, 0.25f, 0.25f, 1f);

    [Tooltip("Color of the valid-range tiles shown when this action is selected.")]
    public Color rangeHighlightColor = new Color(1f, 0.8f, 0.2f, 1f);

#if UNITY_EDITOR
    [Header("Summary (read-only)")]
    [SerializeField, TextArea(3, 5)]
    private string _summary = "";

    private void OnValidate()
    {
        if (maxRange < minRange)
            maxRange = minRange;

        string patternInfo = attackPattern != null
            ? string.Format("{0} ({1} tiles)", attackPattern.name, attackPattern.tiles != null ? attackPattern.tiles.Count : 0)
            : "Single tile (no pattern)";

        string rangeInfo = maxRange == 0
            ? "Melee (pattern at unit)"
            : string.Format("Ranged {0}-{1} tiles", minRange, maxRange);

        _summary = string.Format(
            "Damage      : {0}\nStamina cost: {1}\nPattern     : {2}\nRange       : {3}\nRotates     : {4}",
            baseDamage, staminaCost, patternInfo, rangeInfo, rotatesToFacing);
    }
#endif
}
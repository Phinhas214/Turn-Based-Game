using UnityEngine;

[CreateAssetMenu(fileName = "NewCombatAction", menuName = "Combat/Combat Action Data")]
public class CombatActionData : ScriptableObject
{
    [Header("Identity")]
    public string actionName = "Attack";
    public Sprite icon;

    [Tooltip("All frames of the attack sprite sheet in order. Drag the sliced child sprites here.\n" +
             "Leave empty to fall back to showing 'icon' as a static flash.")]
    public Sprite[] animationFrames;

    [Header("Damage Mode")]
    [Tooltip("If enabled, damage is rolled using dice instead of flat damage.")]
    public bool useDiceDamage = false;

    [Header("Flat Damage (used if dice disabled)")]
    [Min(0)]
    public int baseDamage = 10;

    [Header("Dice Damage (used if dice enabled)")]
    [Min(0)]
    public int diceCount = 1;
    public DieType dieType = DieType.D6;
    [Tooltip("Flat modifier added after dice are rolled.")]
    public int flatBonus = 0;

    [Header("Damage Multiplier")]
    [Tooltip("Final damage is multiplied by this value. 1 = normal, 2 = double, 0.5 = half.\n" +
             "Applied after dice/flat damage is calculated.")]
    [Min(0f)]
    public float damageMultiplier = 1f;

    [Header("Attack Pattern")]
    public AttackPattern attackPattern;
    public bool rotatesToFacing = true;

    [Header("Range")]
    [Min(0)] public int minRange = 0;
    [Min(0)] public int maxRange = 0;
    public bool canTargetSelf = false;

    [Header("Stamina Cost")]
    [Min(0)]
    public int staminaCost = 2;
    public bool requiresEnoughStamina = true;

    [Header("Visual Feedback")]
    public Color aoeHighlightColor   = new Color(1f, 0.25f, 0.25f, 1f);
    public Color rangeHighlightColor = new Color(1f, 0.8f,  0.2f,  1f);

    // ── Damage helper ──────────────────────────────────────────────────────

    /// <summary>
    /// Calculates the final damage value, rolling dice if enabled and applying
    /// the damage multiplier. Use this everywhere instead of reading baseDamage directly.
    /// </summary>
    public int CalculateDamage()
    {
        int raw;
        if (useDiceDamage)
        {
            int sides = (int)dieType;
            raw = flatBonus;
            for (int i = 0; i < diceCount; i++)
                raw += UnityEngine.Random.Range(1, sides + 1);
        }
        else
        {
            raw = baseDamage;
        }

        return Mathf.Max(1, Mathf.RoundToInt(raw * damageMultiplier));
    }

#if UNITY_EDITOR
    [Header("Summary (read-only)")]
    [SerializeField, TextArea(3, 5)]
    private string _summary = "";

    private void OnValidate()
    {
        if (maxRange < minRange)
            maxRange = minRange;

        string patternInfo = attackPattern != null
            ? $"{attackPattern.name} ({attackPattern.tiles?.Count ?? 0} tiles)"
            : "Single tile";

        string rangeInfo = maxRange == 0
            ? "Melee"
            : $"Ranged {minRange}-{maxRange}";

        string damageText = useDiceDamage
            ? $"{diceCount}d{(int)dieType} + {flatBonus}"
            : $"{baseDamage}";

        string multiplierText = !Mathf.Approximately(damageMultiplier, 1f)
            ? $" × {damageMultiplier}"
            : "";

        _summary =
            $"Damage : {damageText}{multiplierText}\n" +
            $"Stamina cost: {staminaCost}\n" +
            $"Pattern : {patternInfo}\n" +
            $"Range : {rangeInfo}\n" +
            $"Rotates : {rotatesToFacing}";
    }
#endif
}
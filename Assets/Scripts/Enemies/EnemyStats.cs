using UnityEngine;

/// <summary>
/// Data asset defining the stats for one enemy type.
/// Create via: Assets > Create > Combat > Enemy Stats
///
/// ATTACK RANGE is now driven by attackData.maxRange — set it there, not here.
/// KITE settings are used by RangedEnemyAI; ignored by EnemyAI.
/// </summary>
[CreateAssetMenu(fileName = "NewEnemyStats", menuName = "Combat/Enemy Stats")]
public class EnemyStats : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Display name shown in UI.")]
    public string enemyName = "Enemy";

    [Header("Health")]
    [Min(1)]
    public int maxHealth = 50;

    [Header("Movement")]
    [Tooltip("How many tiles this enemy can move per turn.")]
    [Min(1)]
    public int moveRange = 3;

    [Header("Attack")]
    [Tooltip("The attack this enemy uses. Attack range is read from attackData.maxRange.")]
    public CombatActionData attackData;

    [Header("Ranged Behaviour")]
    [Tooltip("Used by RangedEnemyAI only. If the player comes closer than this, the enemy retreats.")]
    public bool kiteEnabled = true;
    [Tooltip("Retreat trigger distance in tiles. Enemy backs away when player is within this range.")]
    [Min(1)]
    public int kiteRange = 2;

    [Header("Behaviour")]
    [Tooltip("If true the enemy always moves toward the player even if it can't attack.")]
    public bool alwaysChases = true;

    [Tooltip("How many turns the enemy waits before acting after spawning.")]
    [Min(0)]
    public int turnsBeforeFirstAction = 0;

    // ── Derived property ───────────────────────────────────────────────────

    /// <summary>
    /// Attack range in tiles. Reads directly from attackData.maxRange so there is
    /// only one place to configure it (the CombatActionData asset).
    /// Falls back to 1 (melee) if no attackData is assigned.
    /// </summary>
    public int attackRange => attackData != null ? attackData.maxRange : 1;

#if UNITY_EDITOR
    [Header("Summary (read-only)")]
    [SerializeField, TextArea(3, 4)] private string _summary = "";

    private void OnValidate()
    {
        int range = attackData != null ? attackData.maxRange : 1;
        _summary = string.Format(
            "HP: {0}  |  Move: {1}  |  Attack range: {2} (from attackData)\n" +
            "Attack: {3}  |  Kite: {4} (range {5})",
            maxHealth, moveRange, range,
            attackData != null ? attackData.actionName : "None",
            kiteEnabled, kiteRange);
    }
#endif
}
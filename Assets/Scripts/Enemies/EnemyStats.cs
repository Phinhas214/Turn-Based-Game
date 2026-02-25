using UnityEngine;

/// <summary>
/// Data asset defining the stats for one enemy type.
/// Create via: Assets > Create > Combat > Enemy Stats
/// Assign to an EnemyUnit component.
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
    [Tooltip("The attack this enemy uses. Create via Assets > Create > Combat > Combat Action Data.")]
    public CombatActionData attackData;

    [Tooltip("How close the enemy needs to be to attack the player (Manhattan distance).")]
    [Min(1)]
    public int attackRange = 1;

    [Header("Behaviour")]
    [Tooltip("If true the enemy always moves toward the player even if it can't attack.\n" +
             "If false it stops moving once it can't find a path.")]
    public bool alwaysChases = true;

    [Tooltip("How many turns the enemy waits before acting after spawning.")]
    [Min(0)]
    public int turnsBeforeFirstAction = 0;

#if UNITY_EDITOR
    [Header("Summary (read-only)")]
    [SerializeField, TextArea(3, 4)] private string _summary = "";

    private void OnValidate()
    {
        _summary = string.Format(
            "HP: {0}  |  Move: {1}  |  Attack range: {2}\nAttack: {3}",
            maxHealth, moveRange, attackRange,
            attackData != null ? attackData.actionName : "None");
    }
#endif
}
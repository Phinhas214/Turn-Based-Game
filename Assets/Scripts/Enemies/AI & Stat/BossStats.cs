using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject defining a Boss enemy's stats, wave tables, and invisibility config.
///
/// Create via: Assets > Create > Combat > Boss Stats
/// Assign to a BossUnit component (alongside EnemyStats for base combat values).
///
/// WAVE TABLE EXAMPLE
/// ------------------
///   Wave 0: prefab=Goblin,  count=5   → spawns 5 Goblins
///   Wave 1: prefab=Archer,  count=2   → spawns 2 Archers + 3 Goblins
///            prefab=Goblin,  count=3
///
/// The boss waits until ALL enemies from a wave are dead before spawning the next wave.
/// </summary>
[CreateAssetMenu(fileName = "NewBossStats", menuName = "Combat/Boss Stats")]
public class BossStats : ScriptableObject
{
    // ── Wave Spawning ──────────────────────────────────────────────────────

    [System.Serializable]
    public class SpawnRow
    {
        [Tooltip("Enemy prefab to spawn (must have EnemyUnit + an AI component).")]
        public GameObject prefab;
        [Min(1)]
        public int count = 1;
    }

    [System.Serializable]
    public class WaveTable
    {
        [Tooltip("Label shown in the Inspector for clarity.")]
        public string waveName = "Wave";
        [Tooltip("One or more enemy types to spawn in this wave.")]
        public List<SpawnRow> spawns = new List<SpawnRow>();
    }

    [Header("Wave Tables")]
    [Tooltip("Waves are triggered in order. Each wave waits for the previous one to be fully cleared.")]
    public List<WaveTable> waves = new List<WaveTable>();

    [Tooltip("How many tiles from the boss enemies are allowed to spawn (uses random walkable tile within radius).")]
    [Min(1)]
    public int spawnRadius = 5;

    // ── Invisibility ───────────────────────────────────────────────────────

    [Header("Invisibility")]
    [Tooltip("How many turns the boss takes before it can go invisible again after revealing.")]
    [Min(1)]
    public int invisibilityCooldownTurns = 3;

    [Tooltip("The boss goes invisible after attacking. Set to 0 to disable.")]
    [Min(0)]
    public int goesInvisAfterAttackEveryNTurns = 2;

    [Tooltip("Base chance (0-1) that a player attack hits the invisible boss.")]
    [Range(0f, 1f)]
    public float invisHitChance = 0.15f;

    [Header("Proximity Reveal")]
    [Tooltip("Boss is revealed if a player stands within this many tiles (scales with player count).")]
    [Min(1)]
    public int baseRevealRadius = 2;

    [Tooltip("Added to revealRadius per extra player in the room beyond the first.")]
    [Min(0)]
    public int revealRadiusPerExtraPlayer = 1;

    // ── Combat mode ────────────────────────────────────────────────────────

    [Header("Combat Mode")]
    [Tooltip("If true the boss picks melee or ranged randomly when it spawns and keeps it for the fight.")]
    public bool randomiseCombatMode = true;

    [Tooltip("Forced mode used when randomiseCombatMode is false.")]
    public BossCombatMode forcedCombatMode = BossCombatMode.Melee;

    public enum BossCombatMode { Melee, Ranged }

    [Header("Ranged Phase Settings")]
    [Tooltip("Attack range and AoE pattern are read from the EnemyStats.attackData asset.\n" +
             "These fields only control movement behaviour during the ranged phase.\n"+"When in ranged phase: if the player enters within this many tiles the boss retreats.")]
    [Min(1)]
    public int rangedRetreatRange = 2;

    [Tooltip("When in ranged phase: the boss tries to stay at least this far away (its preferred shooting distance).")]
    [Min(1)]
    public int rangedPreferredRange = 4;

#if UNITY_EDITOR
    [Header("Summary (read-only)")]
    [SerializeField, TextArea(3, 6)] private string _summary = "";

    private void OnValidate()
    {
        _summary = string.Format(
            "Waves: {0}  |  Spawn radius: {1}\n" +
            "Invis cooldown: {2} turns  |  Invis after attack every {3} turns\n" +
            "Reveal radius: {4} (+{5}/extra player)  |  Hit chance while invis: {6:P0}\n" +
            "Ranged retreat: {7} tiles  |  Preferred range: {8} tiles",
            waves.Count, spawnRadius,
            invisibilityCooldownTurns, goesInvisAfterAttackEveryNTurns,
            baseRevealRadius, revealRadiusPerExtraPlayer,
            invisHitChance,
            rangedRetreatRange, rangedPreferredRange);
    }
#endif
}
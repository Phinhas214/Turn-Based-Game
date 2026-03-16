using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject defining which enemies spawn in a room type and in what ratio.
/// Only active when the current level falls within minLevel–maxLevel.
///
/// How it works:
///   - WaveManager provides a total enemy budget (e.g. 10 enemies on level 1)
///   - Each entry's percentage determines how many of that budget become that enemy type
///   - Example: budget=10, Wolf=60%, Snowman=40% → 6 Wolves, 4 Snowmen
///   - Percentages should sum to 100
///
/// Create via: Assets > Create > Combat > Enemy Spawn Table
/// </summary>
[CreateAssetMenu(fileName = "NewSpawnTable", menuName = "Combat/Enemy Spawn Table")]
public class EnemySpawnTable : ScriptableObject
{
    [Serializable]
    public class EnemyEntry
    {
        [Tooltip("The enemy prefab to spawn.")]
        public GameObject prefab;

        [Tooltip("What percentage of the total enemy budget this enemy takes up. All entries should sum to 100.")]
        [Range(0f, 100f)]
        public float percentage = 50f;
    }

    [Header("Enemy Composition")]
    [Tooltip("Add enemy types here and set their % share. Should total 100%.")]
    public List<EnemyEntry> entries = new List<EnemyEntry>();

    [Header("Room Type")]
    [Tooltip("Which room type this table populates.")]
    public LevelGenerator.RoomType roomType = LevelGenerator.RoomType.Normal;

    [Header("Level Range")]
    [Tooltip("This table is only used when current level >= minLevel.")]
    [Min(1)] public int minLevel = 1;
    [Tooltip("This table is only used when current level <= maxLevel. Set to 99 for no upper limit.")]
    [Min(1)] public int maxLevel = 99;

#if UNITY_EDITOR
    [Header("Summary (read-only)")]
    [SerializeField, TextArea(3, 6)] private string _summary = "";

    private void OnValidate()
    {
        if (maxLevel < minLevel) maxLevel = minLevel;

        float total = 0f;
        foreach (var e in entries) total += e.percentage;

        string pctStatus = Mathf.Abs(total - 100f) < 0.1f
            ? "✓ Good"
            : $"⚠ Should sum to 100! (currently {total:F1}%)";

        _summary = $"Active levels : {minLevel} – {(maxLevel >= 99 ? "∞" : maxLevel.ToString())}\n" +
                   $"Room type     : {roomType}\n" +
                   $"Percentages   : {total:F1}%  {pctStatus}\n" +
                   $"Enemy types   : {entries.Count}\n\n";

        foreach (var e in entries)
        {
            string name = e.prefab != null ? e.prefab.name : "(none)";
            _summary += $"  {name} — {e.percentage:F0}%\n";
        }
    }
#endif

    /// <summary>Returns true if this table should be used on the given level.</summary>
    public bool IsActiveForLevel(int level) => level >= minLevel && level <= maxLevel;

    /// <summary>
    /// Given a total enemy budget, returns how many of each enemy type to spawn.
    /// Each enemy's count = round(budget * percentage / 100).
    /// </summary>
    public List<(GameObject prefab, int count)> CalculateSpawns(int totalBudget)
    {
        var result = new List<(GameObject, int)>();

        foreach (EnemyEntry entry in entries)
        {
            if (entry.prefab == null || entry.percentage <= 0f) continue;

            int count = Mathf.Max(1, Mathf.RoundToInt(totalBudget * (entry.percentage / 100f)));
            result.Add((entry.prefab, count));
        }

        return result;
    }
}
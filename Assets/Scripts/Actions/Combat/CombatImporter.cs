using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Loads CombatActionData values from a CSV file at runtime and creates
/// ScriptableObject instances in memory that can be handed directly to units.
///
/// ── CSV Column Order ─────────────────────────────────────────────────────
///   actionName, baseDamage, minRange, maxRange, staminaCost,
///   rotatesToFacing, requiresEnoughStamina, canTargetSelf, attackPatternName
///
/// ── Example rows ─────────────────────────────────────────────────────────
///   Slash,15,0,0,2,true,true,false,MeleeSlash
///   Fireball,30,2,5,4,true,true,false,DiamondAoE
///
/// ── Setup ────────────────────────────────────────────────────────────────
///   1. Place your CSV in  Assets/Resources/CombatActions/actions.csv
///   2. Create AttackPattern assets and add them to the Available Patterns list.
///      The pattern's ASSET NAME must match the attackPatternName column exactly.
///   3. Call LoadFromCSV() at startup to get a List<CombatActionData>.
/// </summary>
public class CombatActionCSVLoader : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────
    [Header("CSV Source")]
    [Tooltip("Path to the CSV file relative to the Resources folder, WITHOUT the .csv extension.\n" +
             "Example: 'CombatActions/actions'  →  Assets/Resources/CombatActions/actions.csv")]
    [SerializeField] private string csvResourcePath = "CombatActions/actions";

    [Tooltip("Whether to log a line for every successfully parsed action.")]
    [SerializeField] private bool verboseLogging = false;

    // ─────────────────────────────────────────────────────────────────────
    [Header("Pattern Lookup")]
    [Tooltip("Drag AttackPattern ScriptableObject assets here.\n" +
             "Each pattern is matched by its ASSET NAME against the 'attackPatternName' CSV column.")]
    [SerializeField] private List<AttackPattern> availablePatterns = new List<AttackPattern>();

    // ─────────────────────────────────────────────────────────────────────
    //  Runtime state
    // ─────────────────────────────────────────────────────────────────────
    private Dictionary<string, AttackPattern> _patternLookup;

    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        BuildPatternLookup();
    }

    private void BuildPatternLookup()
    {
        _patternLookup = new Dictionary<string, AttackPattern>();
        foreach (AttackPattern p in availablePatterns)
        {
            if (p != null && !_patternLookup.ContainsKey(p.name))
                _patternLookup[p.name] = p;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses the configured CSV and returns a list of runtime CombatActionData instances.
    /// Safe to call multiple times; each call creates fresh instances.
    /// </summary>
    public List<CombatActionData> LoadFromCSV()
    {
        if (_patternLookup == null) BuildPatternLookup();

        List<CombatActionData> results = new List<CombatActionData>();

        TextAsset csvFile = Resources.Load<TextAsset>(csvResourcePath);
        if (csvFile == null)
        {
            Debug.LogError($"[CombatActionCSVLoader] Could not find CSV at " +
                           $"Resources/{csvResourcePath}.csv");
            return results;
        }

        string[] lines = csvFile.text.Split('\n');
        int parsed = 0;

        // Row 0 is the header – skip it
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("//")) continue;

            string[] cols = line.Split(',');
            if (cols.Length < 6)
            {
                Debug.LogWarning($"[CombatActionCSVLoader] Row {i} skipped – too few columns: {line}");
                continue;
            }

            CombatActionData data = ScriptableObject.CreateInstance<CombatActionData>();
            FillData(data, cols, i);
            results.Add(data);
            parsed++;

            if (verboseLogging)
                Debug.Log($"[CombatActionCSVLoader] Loaded action '{data.actionName}'");
        }

        Debug.Log($"[CombatActionCSVLoader] Loaded {parsed} actions from '{csvResourcePath}.csv'.");
        return results;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Private parsers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Column order:
    ///   0  actionName
    ///   1  baseDamage
    ///   2  minRange
    ///   3  maxRange
    ///   4  staminaCost
    ///   5  rotatesToFacing
    ///   6  requiresEnoughStamina  (optional, default true)
    ///   7  canTargetSelf          (optional, default false)
    ///   8  attackPatternName      (optional)
    /// </summary>
    private void FillData(CombatActionData data, string[] cols, int rowIndex)
    {
        data.name                 = cols[0].Trim(); // ScriptableObject asset name
        data.actionName           = cols[0].Trim();
        data.diceCount            = ParseInt(Get(cols, 1), 1, rowIndex, "diceCount");
        data.dieType              = DieType.D6;
        data.flatBonus            = 0;
        data.minRange             = ParseInt (Get(cols, 2),  0,   rowIndex, "minRange");
        data.maxRange             = ParseInt (Get(cols, 3),  0,   rowIndex, "maxRange");
        data.staminaCost          = ParseInt (Get(cols, 4),  2,   rowIndex, "staminaCost");
        data.rotatesToFacing      = ParseBool(Get(cols, 5),  true,  rowIndex, "rotatesToFacing");
        data.requiresEnoughStamina= ParseBool(Get(cols, 6),  true,  rowIndex, "requiresEnoughStamina");
        data.canTargetSelf        = ParseBool(Get(cols, 7),  false, rowIndex, "canTargetSelf");

        if (data.maxRange < data.minRange)
        {
            Debug.LogWarning($"[CombatActionCSVLoader] Row {rowIndex}: maxRange < minRange – clamping.");
            data.maxRange = data.minRange;
        }

        string patternName = Get(cols, 8).Trim();
        if (!string.IsNullOrEmpty(patternName))
        {
            if (_patternLookup.TryGetValue(patternName, out AttackPattern pattern))
                data.attackPattern = pattern;
            else
                Debug.LogWarning($"[CombatActionCSVLoader] Row {rowIndex}: " +
                                 $"AttackPattern '{patternName}' not found in Available Patterns list.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────

    private string Get(string[] cols, int index) =>
        index < cols.Length ? cols[index] : string.Empty;

    private int ParseInt(string s, int fallback, int row, string col)
    {
        if (int.TryParse(s.Trim(), out int v)) return v;
        if (!string.IsNullOrEmpty(s.Trim()))
            Debug.LogWarning($"[CombatActionCSVLoader] Row {row}: could not parse '{col}' " +
                             $"value '{s}' as int – using default {fallback}.");
        return fallback;
    }

    private bool ParseBool(string s, bool fallback, int row, string col)
    {
        if (bool.TryParse(s.Trim(), out bool v)) return v;
        if (!string.IsNullOrEmpty(s.Trim()))
            Debug.LogWarning($"[CombatActionCSVLoader] Row {row}: could not parse '{col}' " +
                             $"value '{s}' as bool – using default {fallback}.");
        return fallback;
    }
}

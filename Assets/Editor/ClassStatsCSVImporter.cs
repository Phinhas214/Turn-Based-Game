using UnityEditor;
using UnityEngine;

public static class ClassStatsCSVImporter
{
    [MenuItem("Tools/Import/Class Stats CSV")]
    public static void Import()
    {
        // 1. Load CSV
        TextAsset csv = AssetDatabase.LoadAssetAtPath<TextAsset>(
            "Assets/Data/CSV/ClassStats.csv"
        );

        if (csv == null)
        {
            Debug.LogError("ClassStats.csv not found at Assets/Data/CSV/");
            return;
        }

        // 2. Load Database
        ClassStatsDatabase db = AssetDatabase.LoadAssetAtPath<ClassStatsDatabase>(
            "Assets/ClassStatsDatabase.asset"
        );

        if (db == null)
        {
            Debug.LogError("ClassStatsDatabase.asset not found in Assets/");
            return;
        }

        db.classes.Clear();

        // 3. Parse CSV
        string[] lines = csv.text.Split('\n');

        for (int i = 1; i < lines.Length; i++) // skip header
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;

            string[] values = lines[i].Trim().Split(',');

            ClassStats stats = new ClassStats
            {
                playerClass = (PlayerClass)System.Enum.Parse(
                    typeof(PlayerClass), values[0]
                ),
                maxHealth = int.Parse(values[1]),
                maxStamina = int.Parse(values[2])
            };

            db.classes.Add(stats);
        }

        // 4. Save
        EditorUtility.SetDirty(db);
        AssetDatabase.SaveAssets();

        Debug.Log("ClassStats.csv imported into ClassStatsDatabase");
    }
}

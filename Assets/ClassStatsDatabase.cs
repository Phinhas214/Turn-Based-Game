using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Data/Class Stats Database")]
public class ClassStatsDatabase : ScriptableObject
{
    public List<ClassStats> classes = new();

    public ClassStats Get(PlayerClass playerClass)
    {
        foreach (var stats in classes)
        {
            if (stats.playerClass == playerClass)
                return stats;
        }

        Debug.LogError($"No ClassStats found for {playerClass}");
        return null;
    }
}

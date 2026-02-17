using System;
using System.Collections.Generic;
using UnityEngine;

public enum DieType
{
    D4 = 4,
    D6 = 6,
    D8 = 8,
    D10 = 10,
    D12 = 12,
    D20 = 20
}

public static class DiceRoller
{
    public static int Roll(DieType die)
    {
        return UnityEngine.Random.Range(1, (int)die + 1);
    }

    public static List<int> RollMultiple(DieType die, int count)
    {
        List<int> results = new();

        for (int i = 0; i < count; i++)
        {
            results.Add(Roll(die));
        }

        return results;
    }
}

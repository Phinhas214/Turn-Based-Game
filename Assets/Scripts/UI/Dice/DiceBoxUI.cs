using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class DiceBoxUI : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI resultsText;
    public TextMeshProUGUI totalText;

    [Header("Dice Visuals")]
    public Transform diceSpawnPoint;
    public Transform diceCenter;
    public DiceVisual d6Prefab;
    public Transform diceVisualContainer;

    List<DiceVisual> diceVisuals = new();

    // Dice rolled this attack
    List<int> currentRoll = new();

    int currentFlatBonus = 0;

    public void ShowRoll(List<int> results, int flatBonus = 0)
    {
        currentRoll = new List<int>(results);
        currentFlatBonus = flatBonus;

        foreach (int value in results)
            SpawnD6Visual(value);

        UpdateUI();
    }

    void SpawnD6Visual(int rolledValue)
    {
        DiceVisual die = Instantiate(d6Prefab, diceVisualContainer);

        Vector3 spawnLocalPos = diceSpawnPoint.localPosition;
        Vector3 settleLocalPos = diceCenter.localPosition;

        die.Initialize(spawnLocalPos, settleLocalPos, rolledValue);

        diceVisuals.Add(die);
        UpdateDiceLayout();
    }

    void UpdateDiceLayout()
    {
        float spacingX = 50f;
        float spacingY = 50f;
        int maxPerRow = 6;

        Vector3 center = diceCenter.localPosition;

        int totalDice = diceVisuals.Count;
        int totalRows = Mathf.CeilToInt((float)totalDice / maxPerRow);

        float totalHeight = (totalRows - 1) * spacingY;
        float topRowY = center.y + totalHeight / 2f;

        int dieIndex = 0;

        for (int row = 0; row < totalRows; row++)
        {
            int diceInThisRow = Mathf.Min(maxPerRow, totalDice - dieIndex);

            float rowWidth = (diceInThisRow - 1) * spacingX;
            float startX = center.x - rowWidth / 2f;

            float y = topRowY - row * spacingY;

            for (int col = 0; col < diceInThisRow; col++)
            {
                float x = startX + col * spacingX;
                Vector3 target = new Vector3(x, y, center.z);

                diceVisuals[dieIndex].UpdateTarget(target);
                dieIndex++;
            }
        }
    }

    public void Clear()
    {
        currentRoll.Clear();
        currentFlatBonus = 0;

        for (int i = 0; i < diceVisuals.Count; i++)
        {
            if (diceVisuals[i] != null)
                Destroy(diceVisuals[i].gameObject);
        }

        diceVisuals.Clear();

        UpdateUI();
    }

    public void Reroll()
    {
        if (diceVisuals.Count == 0)
            return;

        currentRoll.Clear();

        for (int i = 0; i < diceVisuals.Count; i++)
        {
            int newValue = DiceRoller.Roll(DieType.D6);

            currentRoll.Add(newValue);
            diceVisuals[i].Reroll(newValue);
        }

        UpdateUI();
    }

    void UpdateUI()
    {
        // Show dice results
        if (currentRoll.Count == 0)
        {
            resultsText.text = "-";
            totalText.text = "-";
            return;
        }

        resultsText.text = string.Join(", ", currentRoll);

        int diceTotal = 0;

        foreach (int value in currentRoll)
            diceTotal += value;

        int finalTotal = diceTotal + currentFlatBonus;

        if (currentFlatBonus != 0)
        {
            totalText.text =
                $"{diceTotal} " +
                $"<color=#6CFF6C>+ {currentFlatBonus}</color> " +
                $"= <b>{finalTotal}</b>";
        }
        else
        {
            totalText.text = $"{finalTotal}";
        }
    }
}
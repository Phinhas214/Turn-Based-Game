using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class DiceBoxUI : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI resultsText;
    public TextMeshProUGUI totalText;

    public Transform diceSpawnPoint;
    public Transform diceCenter;
    public DiceVisual d6Prefab;

    public Transform diceVisualContainer;


    List<DiceVisual> diceVisuals = new();

    // Persistent dice in the box
    List<int> allResults = new();

    // Just the most recent roll
    List<int> currentRoll = new();

    public void ShowRoll(List<int> results)
    {
        currentRoll = results;

        allResults.AddRange(results);

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

        // Vertical centering: rows grow upward and downward from center
        float totalHeight = (totalRows - 1) * spacingY;
        float topRowY = center.y + totalHeight / 2f;

        int dieIndex = 0;

        for (int row = 0; row < totalRows; row++)
        {
            int diceInThisRow = Mathf.Min(maxPerRow, totalDice - dieIndex);

            // Center this row horizontally
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
        // Clear data
        allResults.Clear();
        currentRoll.Clear();

        // Destroy dice visuals
        for (int i = 0; i < diceVisuals.Count; i++)
        {
            if (diceVisuals[i] != null)
                Destroy(diceVisuals[i].gameObject);
        }

        diceVisuals.Clear();

        // Reset UI
        UpdateUI();
    }

    public void Reroll()
    {
        if (diceVisuals.Count == 0)
            return;

        allResults.Clear();
        currentRoll.Clear();

        for (int i = 0; i < diceVisuals.Count; i++)
        {
            // Roll a new value for this die
            int newValue = DiceRoller.Roll(DieType.D6);

            // Store result
            allResults.Add(newValue);
            currentRoll.Add(newValue);

            // Update the visual
            diceVisuals[i].Reroll(newValue);
        }

        UpdateUI();
    }


    void UpdateUI()
    {
        // Results = current roll only
        if (currentRoll.Count == 0)
            resultsText.text = "-";
        else
            resultsText.text = string.Join(", ", currentRoll);

        // Total = all dice in box
        int total = 0;
        foreach (int value in allResults)
            total += value;

        totalText.text = $"{total}";
    }
}

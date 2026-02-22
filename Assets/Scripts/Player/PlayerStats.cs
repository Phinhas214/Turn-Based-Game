using UnityEngine;

public class PlayerStats : MonoBehaviour
{
    [Header("Class")]
    public PlayerClass playerClass;

    [Header("Database")]
    public ClassStatsDatabase classStatsDatabase;

    [Header("Health")]
    public int maxHealth;
    public int currentHealth;

    [Header("Stamina")]
    public int maxStamina;
    public int currentStamina;

    private void Start()
    {
        ApplyClassStats();
    }

    private void OnValidate()
    {
        if (Application.isPlaying && classStatsDatabase != null)
        {
            ApplyClassStats();
        }
    }

    private void ApplyClassStats()
    {
        if (!classStatsDatabase)
        {
            Debug.LogError("Missing ClassStatsDatabase reference.");
            return;
        }

        ClassStats stats = classStatsDatabase.Get(playerClass);
        if (stats == null) return;

        maxHealth = stats.maxHealth;
        maxStamina = stats.maxStamina;

        currentHealth = maxHealth;
        currentStamina = maxStamina;
    }

    public int GetCurrentStaminaPoints()
    {
        return currentStamina;
    }

    public void SetCurrentStaminaPoints(int stamina)
    {
        currentStamina = stamina;
    }

    public int GetMaxStaminaPoints()
    {
        return maxStamina;
    }
}
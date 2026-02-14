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

    void Start()
    {
        ApplyClassStats();
    }

    void OnValidate()
    {
        if (Application.isPlaying && classStatsDatabase != null)
        {
            ApplyClassStats();
        }
    }


    void ApplyClassStats()
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
}

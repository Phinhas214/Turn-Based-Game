using UnityEngine;

public class HealthFireUI : MonoBehaviour
{
    [Header("References")]
    public PlayerStats playerStats;
    public Animator animator;

    int lastTier = -1;

    void OnEnable()
    {
        LevelGenerator.OnLevelReady += OnLevelReady;
    }

    void OnDisable()
    {
        LevelGenerator.OnLevelReady -= OnLevelReady;
    }

    void OnLevelReady()
    {
        Unit unit = FindFirstObjectByType<Unit>();
        if (unit != null)
        {
            playerStats = unit.GetComponent<PlayerStats>();
        }
        else
        {
            Debug.LogWarning("HealthFireUI: No Unit found!");
        }
    }

    void Update()
    {
        if (!playerStats || !animator) return;

        float hpPercent =
            (float)playerStats.currentHealth / playerStats.maxHealth;

        int tier = CalculateTier(hpPercent);

        if (tier != lastTier)
        {
            animator.SetInteger("HealthTier", tier);
            lastTier = tier;
        }
    }

    int CalculateTier(float hp)
    {
        if (hp > 0.75f) return 3;   // Max
        if (hp > 0.50f) return 2;   // High
        if (hp > 0.25f) return 1;   // Medium
        if (hp > 0f) return 0;   // Low
        return 0;
    }
}
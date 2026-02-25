using UnityEngine;

/// <summary>
/// Loads player stats from ClassStatsDatabase (which is populated via CSV import).
/// Explicitly initializes HealthComponent after loading so health always
/// reflects the CSV values — no Awake ordering issues.
/// </summary>
public class PlayerStats : MonoBehaviour, IHasHealth
{
    [Header("Class")]
    public PlayerClass playerClass;

    [Header("Database")]
    public ClassStatsDatabase classStatsDatabase;

    [Header("Health (read-only — driven by CSV data)")]
    public int maxHealth;
    public int currentHealth;

    [Header("Stamina")]
    public int maxStamina;
    public int currentStamina;

    // Cached reference so we don't GetComponent every frame
    private HealthComponent healthComponent;

    private void Awake()
    {
        healthComponent = GetComponent<HealthComponent>();

        // Load stats from the database first
        ApplyClassStats();

        // Then explicitly push the health value into HealthComponent.
        // This bypasses any Awake ordering race — we set it ourselves
        // after we know maxHealth is correct.
        if (healthComponent != null)
        {
            healthComponent.InitializeHealth(maxHealth);
            Debug.Log($"[PlayerStats] Initialized health to {maxHealth} from CSV data ({playerClass}).");
        }
        else
        {
            Debug.LogWarning("[PlayerStats] No HealthComponent found on player — attach one to the player prefab.");
        }
    }

    private void ApplyClassStats()
    {
        if (!classStatsDatabase)
        {
            Debug.LogError("[PlayerStats] Missing ClassStatsDatabase reference.");
            return;
        }

        ClassStats stats = classStatsDatabase.Get(playerClass);
        if (stats == null)
        {
            Debug.LogError($"[PlayerStats] No stats found for class {playerClass} in database.");
            return;
        }

        maxHealth  = stats.maxHealth;
        maxStamina = stats.maxStamina;

        currentHealth  = maxHealth;
        currentStamina = maxStamina;
    }

    private void OnValidate()
    {
        if (Application.isPlaying && classStatsDatabase != null)
            ApplyClassStats();
    }

    // ── IHasHealth ─────────────────────────────────────────────────────────
    // Kept so anything else that calls GetComponent<IHasHealth>() still works.
    // But the primary initialization path is now the explicit InitializeHealth()
    // call in Awake, not the auto-detection in HealthComponent.Awake.

    public int GetMaxHealth() => maxHealth;

    // ── Stamina API (unchanged) ────────────────────────────────────────────

    public int  GetCurrentStaminaPoints()            => currentStamina;
    public void SetCurrentStaminaPoints(int stamina) { currentStamina = stamina; }
    public int  GetMaxStaminaPoints()                => maxStamina;
}
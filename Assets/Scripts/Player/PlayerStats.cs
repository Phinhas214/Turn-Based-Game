using UnityEngine;

/// <summary>
/// Loads player stats from ClassStatsDatabase (CSV-driven).
/// Owns stamina + reacts to room transitions.
/// </summary>
public class PlayerStats : MonoBehaviour, IHasHealth
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

    private HealthComponent healthComponent;

    // ─────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────

    private void Awake()
    {
        healthComponent = GetComponent<HealthComponent>();

        ApplyClassStats();

        if (healthComponent != null)
        {
            healthComponent.InitializeHealth(maxHealth);
            currentHealth = healthComponent.CurrentHealth;
            

            // Subscribe to health updates
            healthComponent.OnHealthChanged += OnHealthChanged;
        }
        else
        {
            Debug.LogWarning("[PlayerStats] Missing HealthComponent.");
        }
    }

    private void OnEnable()
    {
        RoomManager.OnAnyRoomChanged += HandleRoomChanged;
    }

    private void OnDisable()
    {
        RoomManager.OnAnyRoomChanged -= HandleRoomChanged;

        if (healthComponent != null)
            healthComponent.OnHealthChanged -= OnHealthChanged;
    }

    // ─────────────────────────────────────────
    // Health
    // ─────────────────────────────────────────

    private void OnHealthChanged(int current, int max)
    {
        currentHealth = current;
        maxHealth = max;
    }

    // ─────────────────────────────────────────
    // Room transition reaction (IMPORTANT)
    // ─────────────────────────────────────────

    private void HandleRoomChanged(LevelGenerator.PlacedRoom room)
    {
        // Refill stamina
        currentStamina = maxStamina;

        // Force player control
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.ForcePlayerTurn();

        Debug.Log("[PlayerStats] Room entered → stamina reset, player turn forced.");
    }

    // ─────────────────────────────────────────
    // Stats loading
    // ─────────────────────────────────────────

    private void ApplyClassStats()
    {
        if (!classStatsDatabase)
        {
            Debug.LogError("[PlayerStats] Missing ClassStatsDatabase.");
            return;
        }

        ClassStats stats = classStatsDatabase.Get(playerClass);
        if (stats == null)
        {
            Debug.LogError($"[PlayerStats] No stats for class {playerClass}.");
            return;
        }

        maxHealth = stats.maxHealth;
        maxStamina = stats.maxStamina;

        currentHealth = maxHealth;
        currentStamina = maxStamina;
    }

    private void OnValidate()
    {
        if (Application.isPlaying && classStatsDatabase != null)
            ApplyClassStats();
    }

    // ─────────────────────────────────────────
    // Interfaces
    // ─────────────────────────────────────────

    public int GetMaxHealth() => maxHealth;

    public int GetCurrentStaminaPoints() => currentStamina;
    public void SetCurrentStaminaPoints(int value) => currentStamina = value;
    public int GetMaxStaminaPoints() => maxStamina;
}
using System;
using UnityEngine;

/// <summary>
/// Health component for any unit that can take or receive damage.
/// Automatically reads max health from any IHasHealth component on the same GameObject
/// (PlayerStats, EnemyUnit, etc.) so you never need to set values manually in the Inspector.
/// Falls back to the Inspector maxHealth field if no IHasHealth is found.
/// </summary>
public class HealthComponent : MonoBehaviour
{
    [Header("Health Settings")]
    [Tooltip("Maximum hit-points. Ignored if an IHasHealth component is found on this GameObject.")]
    [Min(1)]
    [SerializeField] private int maxHealth = 100;

    [Tooltip("Starting health. Defaults to maxHealth on Awake if left at 0. Ignored if IHasHealth is found.")]
    [Min(0)]
    [SerializeField] private int startingHealth = 0;

    [Header("Death Behaviour")]
    [Tooltip("If true the GameObject is destroyed on death.\n" +
             "If false it is only disabled (useful for pooling or death animations).")]
    [SerializeField] private bool destroyOnDeath = false;

    [Tooltip("Delay in seconds before the GameObject is destroyed/disabled after death.")]
    [Min(0f)]
    [SerializeField] private float deathDelay = 0f;

    [Header("Debug (read-only in play mode)")]
    [SerializeField, Min(0)] private int _currentHealth;

    // ── Events ─────────────────────────────────────────────────────────────

    /// <summary>Fired whenever health changes. Args: (currentHealth, maxHealth).</summary>
    public event Action<int, int> OnHealthChanged;

    /// <summary>Fired once when health reaches 0.</summary>
    public event Action OnDeath;

    // ── Properties ─────────────────────────────────────────────────────────

    public int   CurrentHealth => _currentHealth;
    public int   MaxHealth     => maxHealth;
    public bool  IsDead        => _currentHealth <= 0;
    public float HealthPercent => maxHealth > 0 ? (float)_currentHealth / maxHealth : 0f;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        // Look for a stats provider on the same GameObject.
        // If found, use its max health instead of the Inspector value.
        IHasHealth statsProvider = GetComponent<IHasHealth>();
        if (statsProvider != null)
        {
            maxHealth      = statsProvider.GetMaxHealth();
            _currentHealth = maxHealth;
        }
        else
        {
            // Fall back to Inspector values — same behaviour as before
            _currentHealth = startingHealth > 0 ? Mathf.Min(startingHealth, maxHealth) : maxHealth;
        }
    }

    // ── Public API (all unchanged) ─────────────────────────────────────────

    /// <summary>Deal damage to this unit. Clamps to 0; triggers OnDeath if fatal.</summary>
    public void TakeDamage(int amount)
    {
        if (IsDead || amount <= 0) return;

        _currentHealth = Mathf.Max(0, _currentHealth - amount);
        Debug.Log($"[HealthComponent] {gameObject.name} took {amount} dmg → {_currentHealth}/{maxHealth}");

        OnHealthChanged?.Invoke(_currentHealth, maxHealth);

        if (_currentHealth == 0)
            Die();
    }

    /// <summary>Restore health. Will not exceed maxHealth.</summary>
    public void Heal(int amount)
    {
        if (IsDead || amount <= 0) return;

        _currentHealth = Mathf.Min(maxHealth, _currentHealth + amount);
        OnHealthChanged?.Invoke(_currentHealth, maxHealth);
    }

    /// <summary>Set health to an exact value, clamped between 0 and maxHealth.</summary>
    public void SetHealth(int value)
    {
        _currentHealth = Mathf.Clamp(value, 0, maxHealth);
        OnHealthChanged?.Invoke(_currentHealth, maxHealth);
        if (_currentHealth == 0) Die();
    }

    /// <summary>
    /// Re-initialize max health from a new value (e.g. after a level-up).
    /// Fully restores current health to the new max.
    /// </summary>
    public void InitializeHealth(int newMaxHealth)
    {
        maxHealth      = Mathf.Max(1, newMaxHealth);
        _currentHealth = maxHealth;
        OnHealthChanged?.Invoke(_currentHealth, maxHealth);
    }

    // ── Private ────────────────────────────────────────────────────────────

    private void Die()
    {
        Debug.Log($"[HealthComponent] {gameObject.name} has died.");
        OnDeath?.Invoke();

        if (deathDelay > 0f)
            Invoke(nameof(ExecuteDeath), deathDelay);
        else
            ExecuteDeath();
    }

    private void ExecuteDeath()
    {
        if (destroyOnDeath)
            Destroy(gameObject);
        else
            gameObject.SetActive(false);
    }
}
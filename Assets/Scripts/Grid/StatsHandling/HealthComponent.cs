using System;
using UnityEngine;

/// <summary>
/// Health component for any unit that can take or receive damage.
/// Attach alongside a Unit component.
/// Fires events for UI and other systems to react to.
/// </summary>
public class HealthComponent : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────
    [Header("Health Settings")]
    [Tooltip("Maximum hit-points for this unit.")]
    [Min(1)]
    [SerializeField] private int maxHealth = 100;

    [Tooltip("Starting health. Defaults to maxHealth on Awake if left at 0.")]
    [Min(0)]
    [SerializeField] private int startingHealth = 0;

    // ─────────────────────────────────────────────────────────────────────
    [Header("Death Behaviour")]
    [Tooltip("If true, the GameObject is destroyed on death.\n" +
             "If false, it is only disabled (useful for pooling or death animations).")]
    [SerializeField] private bool destroyOnDeath = false;

    [Tooltip("Delay in seconds before the GameObject is destroyed/disabled after death.\n" +
             "Useful for playing a death animation first.")]
    [Min(0f)]
    [SerializeField] private float deathDelay = 0f;

    // ─────────────────────────────────────────────────────────────────────
    [Header("Debug (read-only in play mode)")]
    [SerializeField, Min(0)] private int _currentHealth;

    // ─────────────────────────────────────────────────────────────────────
    //  Events
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Fired whenever health changes. Args: (currentHealth, maxHealth).</summary>
    public event Action<int, int> OnHealthChanged;

    /// <summary>Fired once when health reaches 0.</summary>
    public event Action OnDeath;

    // ─────────────────────────────────────────────────────────────────────
    //  Properties
    // ─────────────────────────────────────────────────────────────────────
    public int  CurrentHealth => _currentHealth;
    public int  MaxHealth     => maxHealth;
    public bool IsDead        => _currentHealth <= 0;
    public float HealthPercent => maxHealth > 0 ? (float)_currentHealth / maxHealth : 0f;

    // ─────────────────────────────────────────────────────────────────────
    //  Lifecycle
    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _currentHealth = startingHealth > 0 ? Mathf.Min(startingHealth, maxHealth) : maxHealth;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────────────────────────────

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

    // ─────────────────────────────────────────────────────────────────────
    //  Private
    // ─────────────────────────────────────────────────────────────────────

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

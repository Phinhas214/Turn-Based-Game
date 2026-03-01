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

    [Header("Damage Numbers")]
    [SerializeField] private DamageNumber damageNumberPrefab;
    [SerializeField] private Vector3 damageNumberOffset = new Vector3(0f, 1.5f, 0f);

    // ─────────────────────────────────────────────────────────────
    // Damage Flash (overlay-based, same as HealthContainerUI)
    // ─────────────────────────────────────────────────────────────
    private SpriteRenderer flashRenderer;

    [Header("Damage Flash")]
    [SerializeField] private GameObject damageFlashObject;
    [SerializeField] private float flashDuration = 0.15f;
    [SerializeField] private int flashCount = 1;

    // ─────────────────────────────────────────────────────────────
    // Health Settings
    // ─────────────────────────────────────────────────────────────

    [Header("Health Settings")]
    [Min(1)]
    [SerializeField] private int maxHealth = 100;

    [Min(0)]
    [SerializeField] private int startingHealth = 0;

    // ─────────────────────────────────────────────────────────────
    // Death Behaviour
    // ─────────────────────────────────────────────────────────────

    [Header("Death Behaviour")]
    [SerializeField] private bool destroyOnDeath = false;

    [Min(0f)]
    [SerializeField] private float deathDelay = 0f;

    // ─────────────────────────────────────────────────────────────
    // Debug
    // ─────────────────────────────────────────────────────────────

    [Header("Debug (read-only in play mode)")]
    [SerializeField] private int _currentHealth;

    // ─────────────────────────────────────────────────────────────
    // Events
    // ─────────────────────────────────────────────────────────────

    public event Action<int, int> OnHealthChanged;
    public event Action OnDeath;

    // ─────────────────────────────────────────────────────────────
    // Runtime
    // ─────────────────────────────────────────────────────────────

    private Coroutine flashRoutine;

    // ─────────────────────────────────────────────────────────────
    // Properties
    // ─────────────────────────────────────────────────────────────

    public int CurrentHealth => _currentHealth;
    public int MaxHealth => maxHealth;
    public bool IsDead => _currentHealth <= 0;
    public float HealthPercent => maxHealth > 0 ? (float)_currentHealth / maxHealth : 0f;

    // ─────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        IHasHealth statsProvider = GetComponent<IHasHealth>();
        if (statsProvider != null)
        {
            maxHealth = statsProvider.GetMaxHealth();
            _currentHealth = maxHealth;
        }
        else
        {
            _currentHealth = startingHealth > 0
                ? Mathf.Min(startingHealth, maxHealth)
                : maxHealth;
        }

        if (damageFlashObject)
        {
            flashRenderer = damageFlashObject.GetComponent<SpriteRenderer>();
            damageFlashObject.SetActive(true);

            // Start fully transparent
            SetFlashAlpha(0f);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────

    public void TakeDamage(int amount)
    {
        if (IsDead || amount <= 0) return;

        _currentHealth = Mathf.Max(0, _currentHealth - amount);
        OnHealthChanged?.Invoke(_currentHealth, maxHealth);

        SpawnDamageNumber(amount);
        TriggerDamageFlash();

        if (_currentHealth == 0)
            Die();
    }

    private void SpawnDamageNumber(int amount)
    {
        if (!damageNumberPrefab) return;

        Vector3 spawnPos = transform.position + damageNumberOffset;

        DamageNumber dmg = Instantiate(
            damageNumberPrefab,
            spawnPos,
            Quaternion.identity
        );

        dmg.Initialize(amount);
    }

    public void Heal(int amount)
    {
        if (IsDead || amount <= 0) return;

        _currentHealth = Mathf.Min(maxHealth, _currentHealth + amount);
        OnHealthChanged?.Invoke(_currentHealth, maxHealth);
    }

    public void SetHealth(int value)
    {
        _currentHealth = Mathf.Clamp(value, 0, maxHealth);
        OnHealthChanged?.Invoke(_currentHealth, maxHealth);

        if (_currentHealth == 0)
            Die();
    }

    public void InitializeHealth(int newMaxHealth)
    {
        maxHealth = Mathf.Max(1, newMaxHealth);
        _currentHealth = maxHealth;
        OnHealthChanged?.Invoke(_currentHealth, maxHealth);
    }

    // ─────────────────────────────────────────────────────────────
    // Damage Flash Logic (overlay toggle)
    // ─────────────────────────────────────────────────────────────

    private void TriggerDamageFlash()
    {
        if (!flashRenderer) return;

        if (flashRoutine != null)
            StopCoroutine(flashRoutine);

        flashRoutine = StartCoroutine(DamageFlashRoutine());
    }

    private System.Collections.IEnumerator DamageFlashRoutine()
    {
        float half = flashDuration * 0.5f;

        for (int i = 0; i < flashCount; i++)
        {
            // Fade in
            yield return FadeFlash(0f, 1f, half);

            // Fade out
            yield return FadeFlash(1f, 0f, half);
        }

        flashRoutine = null;
    }

    private System.Collections.IEnumerator FadeFlash(float from, float to, float duration)
    {
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float alpha = Mathf.Lerp(from, to, t / duration);
            SetFlashAlpha(alpha);
            yield return null;
        }

        SetFlashAlpha(to);
    }

    private void SetFlashAlpha(float alpha)
    {
        Color c = flashRenderer.color;
        c.a = alpha;
        flashRenderer.color = c;
    }

    // ─────────────────────────────────────────────────────────────
    // Death
    // ─────────────────────────────────────────────────────────────

    private void Die()
    {
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
using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked replacement for HealthComponent.
///
/// RULES:
///   - Health is stored in a NetworkVariable — readable by ALL clients.
///   - Only the SERVER can write health (via TakeDamage / Heal ServerRpcs).
///   - OnHealthChanged and OnDeath events fire on ALL clients via ClientRpc.
///   - DamageNumbers and flash effects spawn locally on each client when they
///     receive the health-changed notification.
///
/// SETUP:
///   - Replace HealthComponent on your Unit and EnemyUnit prefabs with this script.
///   - Prefab must have a NetworkObject component.
///   - Keep the DamageNumber prefab reference and flash object references.
///
/// MIGRATION:
///   - External code calling health.TakeDamage(n) should instead call
///     health.RequestTakeDamage(n) from a client, OR call TakeDamage directly
///     if already on the server.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class NetworkedHealthComponent : NetworkBehaviour
{
    // ── Network state ─────────────────────────────────────────────────────
    private NetworkVariable<int> netCurrentHealth = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> netMaxHealth = new NetworkVariable<int>(
        100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ── Inspector ─────────────────────────────────────────────────────────
    [Header("Damage Numbers")]
    [SerializeField] private DamageNumber damageNumberPrefab;
    [SerializeField] private Vector3 damageNumberOffset = new Vector3(0f, 1.5f, 0f);

    [Header("Damage Flash")]
    [SerializeField] private GameObject damageFlashObject;
    [SerializeField] private float flashDuration = 0.15f;
    [SerializeField] private int   flashCount    = 1;

    [Header("Health Settings")]
    [Min(1)] [SerializeField] private int maxHealth    = 100;
    [Min(0)] [SerializeField] private int startingHealth = 0;

    [Header("Death Behaviour")]
    [SerializeField] private bool  destroyOnDeath = false;
    [SerializeField, Min(0f)] private float deathDelay = 0f;

    [Header("Debug (read-only in play mode)")]
    [SerializeField] private int _currentHealthDebug;

    // ── Events — fire on ALL clients ──────────────────────────────────────
    public event Action<int, int> OnHealthChanged;
    public event Action           OnDeath;

    // ── Private ───────────────────────────────────────────────────────────
    private SpriteRenderer flashRenderer;
    private Coroutine      flashRoutine;

    // ── Properties ────────────────────────────────────────────────────────
    public int   CurrentHealth  => netCurrentHealth.Value;
    public int   MaxHealth      => netMaxHealth.Value;
    public bool  IsDead         => netCurrentHealth.Value <= 0;
    public float HealthPercent  => netMaxHealth.Value > 0 ? (float)netCurrentHealth.Value / netMaxHealth.Value : 0f;

    // ─────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (damageFlashObject)
        {
            flashRenderer = damageFlashObject.GetComponent<SpriteRenderer>();
            damageFlashObject.SetActive(true);
            SetFlashAlpha(0f);
        }

        // Resolve max health from IHasHealth on the same GameObject
        IHasHealth statsProvider = GetComponent<IHasHealth>();
        if (statsProvider != null)
            maxHealth = statsProvider.GetMaxHealth();
    }

    public override void OnNetworkSpawn()
    {
        // Server sets initial values
        if (IsServer)
        {
            netMaxHealth.Value     = maxHealth;
            netCurrentHealth.Value = startingHealth > 0
                ? Mathf.Min(startingHealth, maxHealth)
                : maxHealth;
        }

        // All clients subscribe to health changes for local visuals
        netCurrentHealth.OnValueChanged += HandleNetHealthChanged;
        _currentHealthDebug = netCurrentHealth.Value;
    }

    public override void OnNetworkDespawn()
    {
        netCurrentHealth.OnValueChanged -= HandleNetHealthChanged;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Public API — called from game logic
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Apply damage. Can be called from server directly.
    /// From a client, call RequestTakeDamage() instead.
    /// </summary>
    public void TakeDamage(int amount)
    {
        if (!IsServer)
        {
            RequestTakeDamageServerRpc(amount);
            return;
        }
        ApplyDamageOnServer(amount);
    }

    /// <summary>Request damage from a client — routes to server.</summary>
    public void RequestTakeDamage(int amount)
    {
        RequestTakeDamageServerRpc(amount);
    }

    /// <summary>
    /// Heal. Can be called from server directly.
    /// From a client, call RequestHeal() instead.
    /// </summary>
    public void Heal(int amount)
    {
        if (!IsServer)
        {
            RequestHealServerRpc(amount);
            return;
        }
        ApplyHealOnServer(amount);
    }

    public void RequestHeal(int amount) => RequestHealServerRpc(amount);

    /// <summary>
    /// Set max health and refill current. Server or IHasHealth init only.
    /// </summary>
    public void InitializeHealth(int newMaxHealth)
    {
        if (!IsServer)
        {
            InitializeHealthServerRpc(newMaxHealth);
            return;
        }
        netMaxHealth.Value     = Mathf.Max(1, newMaxHealth);
        netCurrentHealth.Value = netMaxHealth.Value;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Server RPCs
    // ─────────────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void RequestTakeDamageServerRpc(int amount) => ApplyDamageOnServer(amount);

    [ServerRpc(RequireOwnership = false)]
    private void RequestHealServerRpc(int amount) => ApplyHealOnServer(amount);

    [ServerRpc(RequireOwnership = false)]
    private void InitializeHealthServerRpc(int newMax)
    {
        netMaxHealth.Value     = Mathf.Max(1, newMax);
        netCurrentHealth.Value = netMaxHealth.Value;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Server-side logic
    // ─────────────────────────────────────────────────────────────────────

    private void ApplyDamageOnServer(int amount)
    {
        if (IsDead || amount <= 0) return;

        netCurrentHealth.Value = Mathf.Max(0, netCurrentHealth.Value - amount);

        // Trigger damage visuals on all clients
        TriggerDamageVisualsClientRpc(amount);

        if (netCurrentHealth.Value == 0)
            TriggerDeathClientRpc();
    }

    private void ApplyHealOnServer(int amount)
    {
        if (IsDead || amount <= 0) return;
        netCurrentHealth.Value = Mathf.Min(netMaxHealth.Value, netCurrentHealth.Value + amount);
    }

    // ─────────────────────────────────────────────────────────────────────
    // ClientRpcs — visuals/events on all clients
    // ─────────────────────────────────────────────────────────────────────

    [ClientRpc]
    private void TriggerDamageVisualsClientRpc(int amount)
    {
        SpawnDamageNumber(amount);
        TriggerDamageFlash();
    }

    [ClientRpc]
    private void TriggerDeathClientRpc()
    {
        OnDeath?.Invoke();

        if (deathDelay > 0f)
            Invoke(nameof(ExecuteDeath), deathDelay);
        else
            ExecuteDeath();
    }

    // ─────────────────────────────────────────────────────────────────────
    // NetworkVariable callback — fires on all clients when health changes
    // ─────────────────────────────────────────────────────────────────────

    private void HandleNetHealthChanged(int oldVal, int newVal)
    {
        _currentHealthDebug = newVal;
        OnHealthChanged?.Invoke(newVal, netMaxHealth.Value);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Local visual effects (same as old HealthComponent)
    // ─────────────────────────────────────────────────────────────────────

    private void SpawnDamageNumber(int amount)
    {
        if (!damageNumberPrefab) return;
        DamageNumber dmg = Instantiate(
            damageNumberPrefab,
            transform.position + damageNumberOffset,
            Quaternion.identity);
        dmg.Initialize(amount);
    }

    private void TriggerDamageFlash()
    {
        if (!flashRenderer) return;
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(DamageFlashRoutine());
    }

    private System.Collections.IEnumerator DamageFlashRoutine()
    {
        float half = flashDuration * 0.5f;
        for (int i = 0; i < flashCount; i++)
        {
            yield return FadeFlash(0f, 1f, half);
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
            SetFlashAlpha(Mathf.Lerp(from, to, t / duration));
            yield return null;
        }
        SetFlashAlpha(to);
    }

    private void SetFlashAlpha(float alpha)
    {
        if (flashRenderer == null) return;
        Color c = flashRenderer.color;
        c.a = alpha;
        flashRenderer.color = c;
    }

    private void ExecuteDeath()
    {
        if (destroyOnDeath)
        {
            // On server, despawn the network object (this also destroys on clients)
            if (IsServer && TryGetComponent<NetworkObject>(out var netObj))
                netObj.Despawn();
            else if (!IsServer)
                gameObject.SetActive(false); // Wait for server to despawn
        }
        else
        {
            gameObject.SetActive(false);
        }
    }
}
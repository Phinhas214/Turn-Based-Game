using System;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class NetworkedHealthComponent : NetworkBehaviour
{
    private NetworkVariable<int> netCurrentHealth = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> netMaxHealth = new NetworkVariable<int>(
        100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> netIsDown = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Damage Numbers")]
    [SerializeField] private DamageNumber damageNumberPrefab;
    [SerializeField] private Vector3 damageNumberOffset = new Vector3(0f, 1.5f, 0f);
    [Header("Damage Flash")]
    [SerializeField] private GameObject damageFlashObject;
    [SerializeField] private float flashDuration = 0.15f;
    [SerializeField] private int flashCount = 1;
    [Header("Health Settings")]
    [Min(1)][SerializeField] private int maxHealth = 100;
    [Min(0)][SerializeField] private int startingHealth = 0;
    [Header("Death Behaviour")]
    [SerializeField] private bool destroyOnDeath = false;
    [SerializeField, Min(0f)] private float deathDelay = 0f;
    [Header("Debug (read-only in play mode)")]
    [SerializeField] private int _currentHealthDebug;

    public event Action<int, int> OnHealthChanged;
    public event Action OnDeath;
    public event Action OnRevived;

    [Header("Revive Settings")]
    [SerializeField, Range(0f, 1f)] private float reviveHealthPercent = 0.25f;

    private SpriteRenderer flashRenderer;
    private Coroutine flashRoutine;

    // FIX: Track whether we have already fired death visuals client-side.
    // TriggerDeathClientRpc fires on ALL clients including the host.
    // Without this guard the host fires ExecuteDeath twice, calling
    // NetworkObject.Despawn twice and triggering the "Invalid Destroy" error.
    private bool hasTriggeredDeathVisuals = false;

    public int CurrentHealth => netCurrentHealth.Value;
    public int MaxHealth => netMaxHealth.Value;
    public bool IsDead => netCurrentHealth.Value <= 0;
    public bool IsDown => netIsDown.Value;
    public float HealthPercent => netMaxHealth.Value > 0
        ? (float)netCurrentHealth.Value / netMaxHealth.Value : 0f;

    private void Awake()
    {
        if (damageFlashObject)
        {
            flashRenderer = damageFlashObject.GetComponent<SpriteRenderer>();
            damageFlashObject.SetActive(true);
            SetFlashAlpha(0f);
        }
        IHasHealth statsProvider = GetComponent<IHasHealth>();
        if (statsProvider != null)
            maxHealth = statsProvider.GetMaxHealth();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            netMaxHealth.Value = maxHealth;
            netCurrentHealth.Value = startingHealth > 0
                ? Mathf.Min(startingHealth, maxHealth) : maxHealth;
        }
        netCurrentHealth.OnValueChanged += HandleNetHealthChanged;
        _currentHealthDebug = netCurrentHealth.Value;
        hasTriggeredDeathVisuals = false;
    }

    public override void OnNetworkDespawn()
    {
        netCurrentHealth.OnValueChanged -= HandleNetHealthChanged;
    }

    public void TakeDamage(int amount)
    {
        if (!IsServer) { RequestTakeDamageServerRpc(amount); return; }
        ApplyDamageOnServer(amount);
    }

    public void Revive()
    {
        if (!IsServer) { ReviveServerRpc(); return; }
        ApplyReviveOnServer();
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReviveServerRpc() => ApplyReviveOnServer();

    private void ApplyReviveOnServer()
    {
        if (!IsDown) return;
        int reviveHp = Mathf.Max(1, Mathf.RoundToInt(netMaxHealth.Value * reviveHealthPercent));
        netCurrentHealth.Value = reviveHp;
        netIsDown.Value = false;
        TriggerReviveClientRpc();
    }

    [ClientRpc]
    private void TriggerReviveClientRpc()
    {
        OnRevived?.Invoke();
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
    }

    public void RequestTakeDamage(int amount) => RequestTakeDamageServerRpc(amount);

    public void Heal(int amount)
    {
        if (!IsServer) { RequestHealServerRpc(amount); return; }
        ApplyHealOnServer(amount);
    }

    public void RequestHeal(int amount) => RequestHealServerRpc(amount);

    public void InitializeHealth(int newMaxHealth)
    {
        if (!IsServer) { InitializeHealthServerRpc(newMaxHealth); return; }
        netMaxHealth.Value = Mathf.Max(1, newMaxHealth);
        netCurrentHealth.Value = netMaxHealth.Value;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestTakeDamageServerRpc(int amount) => ApplyDamageOnServer(amount);
    [ServerRpc(RequireOwnership = false)]
    private void RequestHealServerRpc(int amount) => ApplyHealOnServer(amount);
    [ServerRpc(RequireOwnership = false)]
    private void InitializeHealthServerRpc(int newMax)
    {
        netMaxHealth.Value = Mathf.Max(1, newMax);
        netCurrentHealth.Value = netMaxHealth.Value;
    }

    private void ApplyDamageOnServer(int amount)
    {
        if (IsDead || amount <= 0) return;
        netCurrentHealth.Value = Mathf.Max(0, netCurrentHealth.Value - amount);
        TriggerDamageVisualsClientRpc(amount);
        if (netCurrentHealth.Value == 0)
        {
            bool isPlayer = GetComponent<NetworkedUnit>() != null;
            if (isPlayer)
            {
                netIsDown.Value = true;
                TriggerDownedClientRpc();
            }
            else
            {
                // FIX: Reset the guard on the server before broadcasting death.
                // The host's ClientRpc callback will set it again, but we need
                // the server-side HandleDeath in NetworkedEnemyUnit to be able
                // to run its ONE despawn path without the guard blocking it.
                TriggerDeathClientRpc();
            }
        }
    }

    private void ApplyHealOnServer(int amount)
    {
        if (IsDead || amount <= 0) return;
        netCurrentHealth.Value = Mathf.Min(netMaxHealth.Value, netCurrentHealth.Value + amount);
    }

    [ClientRpc]
    private void TriggerDamageVisualsClientRpc(int amount)
    {
        // FIX: Guard against receiving RPCs for already-dead/destroyed objects.
        // This prevents the MissingReferenceException in RpcMessages.Deserialize
        // that occurs when damage visuals arrive after client-side object cleanup.
        if (this == null || !gameObject.activeInHierarchy) return;

        SpawnDamageNumber(amount);
        TriggerDamageFlash();
    }

    [ClientRpc]
    private void TriggerDownedClientRpc()
    {
        OnDeath?.Invoke();
        gameObject.SetActive(false);
    }

    [ClientRpc]
    private void TriggerDeathClientRpc()
    {
        // FIX: This RPC fires on ALL clients including the host (server).
        // Use hasTriggeredDeathVisuals to ensure we only run once per object lifetime.
        // Without this, the host runs ExecuteDeath twice:
        //   1. From NetworkedEnemyUnit.HandleDeath() → health.OnDeath event
        //   2. From this ClientRpc arriving at the host
        // The second call hits NetworkObject.Despawn on an already-despawned object,
        // producing the "Invalid Destroy" log error.
        if (hasTriggeredDeathVisuals) return;
        hasTriggeredDeathVisuals = true;

        OnDeath?.Invoke();

        // FIX: Do NOT call ExecuteDeath on clients. The server handles all
        // NetworkObject lifecycle. Clients just hide the object and wait for
        // the server's Despawn() call to propagate, which removes it cleanly.
        // Calling Destroy() or Despawn() from a client causes the error:
        // "[Invalid Destroy] Destroy a spawned NetworkObject on a non-host client is not valid"
        if (IsServer)
        {
            if (deathDelay > 0f)
                Invoke(nameof(ExecuteDeathOnServer), deathDelay);
            else
                ExecuteDeathOnServer();
        }
        else
        {
            // Client: just hide visually. The server's Despawn(true) will
            // destroy the object on all clients automatically via NGO.
            gameObject.SetActive(false);
        }
    }

    private void HandleNetHealthChanged(int oldVal, int newVal)
    {
        _currentHealthDebug = newVal;
        OnHealthChanged?.Invoke(newVal, netMaxHealth.Value);
    }

    // FIX: Renamed from ExecuteDeath — this only runs on the server now.
    // Clients never call this directly. Server calls Despawn(true) which
    // automatically destroys the object on all connected clients via NGO.
    private void ExecuteDeathOnServer()
    {
        if (!IsServer) return;

        if (TryGetComponent<NetworkObject>(out var netObj))
        {
            if (destroyOnDeath)
                netObj.Despawn(true);   // destroys on server AND all clients
            else
                gameObject.SetActive(false);
        }
        else
        {
            // Non-networked object — safe to destroy directly
            if (destroyOnDeath)
                Destroy(gameObject);
            else
                gameObject.SetActive(false);
        }
    }

    private void SpawnDamageNumber(int amount)
    {
        if (!damageNumberPrefab) return;
        DamageNumber dmg = Instantiate(damageNumberPrefab,
            transform.position + damageNumberOffset, Quaternion.identity);
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
}
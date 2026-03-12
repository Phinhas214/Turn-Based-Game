using System;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class NetworkedHealthComponent : NetworkBehaviour
{
    private NetworkVariable<int>  netCurrentHealth = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int>  netMaxHealth = new NetworkVariable<int>(
        100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> netIsDown = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Damage Numbers")]
    [SerializeField] private DamageNumber damageNumberPrefab;
    [SerializeField] private Vector3 damageNumberOffset = new Vector3(0f, 1.5f, 0f);
    [Header("Damage Flash")]
    [SerializeField] private GameObject damageFlashObject;
    [SerializeField] private float flashDuration = 0.15f;
    [SerializeField] private int   flashCount    = 1;
    [Header("Health Settings")]
    [Min(1)] [SerializeField] private int maxHealth      = 100;
    [Min(0)] [SerializeField] private int startingHealth = 0;
    [Header("Death Behaviour")]
    [SerializeField] private bool  destroyOnDeath = false;
    [SerializeField, Min(0f)] private float deathDelay  = 0f;
    [Header("Debug (read-only in play mode)")]
    [SerializeField] private int _currentHealthDebug;

    public event Action<int, int> OnHealthChanged;
    public event Action           OnDeath;
    public event Action           OnRevived;

    [Header("Revive Settings")]
    [SerializeField, Range(0f, 1f)] private float reviveHealthPercent = 0.25f;

    private SpriteRenderer flashRenderer;
    private Coroutine      flashRoutine;

    public int   CurrentHealth => netCurrentHealth.Value;
    public int   MaxHealth     => netMaxHealth.Value;
    // IsDead: used by enemies and turn system to skip this player entirely.
    // In single-player: health <= 0 means dead.
    // In multiplayer: player is "downed" (netIsDown) not dead — allies can revive them.
    // We treat downed as dead for AI and turn purposes; it just isn't permanent.
    public bool  IsDead  => netCurrentHealth.Value <= 0;
    public bool  IsDown  => netIsDown.Value;
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
            netMaxHealth.Value     = maxHealth;
            netCurrentHealth.Value = startingHealth > 0
                ? Mathf.Min(startingHealth, maxHealth) : maxHealth;
        }
        netCurrentHealth.OnValueChanged += HandleNetHealthChanged;
        _currentHealthDebug = netCurrentHealth.Value;
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

    /// <summary>
    /// Revives a downed player, restoring them to reviveHealthPercent of max HP.
    /// Call this from the reviving player's action (server or ServerRpc).
    /// </summary>
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
        netIsDown.Value        = false;
        TriggerReviveClientRpc();
    }

    [ClientRpc]
    private void TriggerReviveClientRpc()
    {
        OnRevived?.Invoke();
        // Re-enable the GameObject if it was hidden on death
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
        netMaxHealth.Value     = Mathf.Max(1, newMaxHealth);
        netCurrentHealth.Value = netMaxHealth.Value;
    }

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

    private void ApplyDamageOnServer(int amount)
    {
        if (IsDead || amount <= 0) return;
        netCurrentHealth.Value = Mathf.Max(0, netCurrentHealth.Value - amount);
        TriggerDamageVisualsClientRpc(amount);
        if (netCurrentHealth.Value == 0)
        {
            // Check if this is a player (has NetworkedUnit) or an enemy/prop
            bool isPlayer = GetComponent<NetworkedUnit>() != null;
            if (isPlayer)
            {
                // Players enter DOWNED state — allies can revive them
                netIsDown.Value = true;
                TriggerDownedClientRpc();
            }
            else
            {
                // Enemies and non-player objects die normally
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
        SpawnDamageNumber(amount);
        TriggerDamageFlash();
    }

    [ClientRpc]
    private void TriggerDownedClientRpc()
    {
        // Player is downed — fire OnDeath so UI/animations react the same way,
        // but the player object stays in the world so allies can revive them.
        OnDeath?.Invoke();
        // Hide or play downed animation — just set inactive for now
        gameObject.SetActive(false);
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

    private void HandleNetHealthChanged(int oldVal, int newVal)
    {
        _currentHealthDebug = newVal;
        OnHealthChanged?.Invoke(newVal, netMaxHealth.Value);
    }

    private void ExecuteDeath()
    {
        // NEVER call Destroy() on a spawned NetworkObject from a non-server client.
        // Server despawns it which automatically cleans up on all clients.
        bool isNetworked = TryGetComponent<NetworkObject>(out var netObj);

        if (isNetworked)
        {
            if (IsServer)
            {
                if (destroyOnDeath)
                    netObj.Despawn(true);
                else
                    gameObject.SetActive(false);
            }
            else
            {
                // Client — just hide, wait for server despawn
                gameObject.SetActive(false);
            }
        }
        else
        {
            // SP non-networked object — safe to destroy
            if (destroyOnDeath)
                Destroy(gameObject, deathDelay);
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
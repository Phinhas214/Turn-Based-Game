using System;
using System.Collections;
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
    [SerializeField] private int   flashCount    = 1;
    [Header("Health Settings")]
    [Min(1)] [SerializeField] private int maxHealth     = 100;
    [Min(0)] [SerializeField] private int startingHealth = 0;
    [Header("Death Behaviour")]
    [SerializeField] private bool  destroyOnDeath = false;
    [SerializeField, Min(0f)] private float deathDelay = 0f;
    [Header("Downed Visual")]
    [SerializeField] private Transform downedVisualRoot;
    [SerializeField] private float downedRotationZ = 90f;
    [SerializeField] private float downedTiltSpeed = 180f;
    [Header("Revive Settings")]
    [SerializeField, Range(0f, 1f)] private float reviveHealthPercent = 0.25f;
    [Header("Debug")]
    [SerializeField] private int _currentHealthDebug;

    public event Action<int, int> OnHealthChanged;
    public event Action           OnDeath;
    public event Action           OnRevived;

    private SpriteRenderer flashRenderer;
    private Coroutine      flashRoutine;
    private Coroutine      tiltRoutine;
    private Quaternion     originalLocalRotation;
    private bool           isPlayerUnit;

    public int   CurrentHealth => netCurrentHealth.Value;
    public int   MaxHealth     => netMaxHealth.Value;
    public bool  IsDead        => netCurrentHealth.Value <= 0;
    public bool  IsDown        => netIsDown.Value;
    public float HealthPercent => netMaxHealth.Value > 0
        ? (float)netCurrentHealth.Value / netMaxHealth.Value : 0f;

    private void Awake()
    {
        isPlayerUnit = GetComponent<NetworkedUnit>() != null;
        if (downedVisualRoot == null) downedVisualRoot = transform;
        originalLocalRotation = downedVisualRoot.localRotation;

        if (damageFlashObject)
        {
            flashRenderer = damageFlashObject.GetComponent<SpriteRenderer>();
            damageFlashObject.SetActive(true);
            SetFlashAlpha(0f);
        }

        IHasHealth statsProvider = GetComponent<IHasHealth>();
        if (statsProvider != null) maxHealth = statsProvider.GetMaxHealth();
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
        netIsDown.OnValueChanged        += HandleNetIsDownChanged;
        _currentHealthDebug = netCurrentHealth.Value;
        if (netIsDown.Value) ApplyDownedVisual(instant: true);
    }

    public override void OnNetworkDespawn()
    {
        netCurrentHealth.OnValueChanged -= HandleNetHealthChanged;
        netIsDown.OnValueChanged        -= HandleNetIsDownChanged;
    }

    public void TakeDamage(int amount)
    {
        if (!IsServer) { RequestTakeDamageServerRpc(amount); return; }
        ApplyDamageOnServer(amount);
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

    public void Revive()
    {
        if (!IsServer) { ReviveServerRpc(); return; }
        ApplyReviveOnServer();
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
    [ServerRpc(RequireOwnership = false)]
    private void ReviveServerRpc() => ApplyReviveOnServer();

    private void ApplyDamageOnServer(int amount)
    {
        if (IsDead || amount <= 0) return;
        netCurrentHealth.Value = Mathf.Max(0, netCurrentHealth.Value - amount);
        TriggerDamageVisualsClientRpc(amount);

        if (netCurrentHealth.Value <= 0)
        {
            if (isPlayerUnit)
            {
                netIsDown.Value = true;
                TriggerDownedClientRpc();
            }
            else
            {
                TriggerDeathClientRpc();
            }
        }
    }

    private void ApplyHealOnServer(int amount)
    {
        if (IsDead || amount <= 0) return;
        netCurrentHealth.Value = Mathf.Min(netMaxHealth.Value, netCurrentHealth.Value + amount);
    }

    private void ApplyReviveOnServer()
    {
        if (!netIsDown.Value) return;
        int reviveHp = Mathf.Max(1, Mathf.RoundToInt(netMaxHealth.Value * reviveHealthPercent));
        netCurrentHealth.Value = reviveHp;
        netIsDown.Value        = false;
        TriggerReviveClientRpc();
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
        OnDeath?.Invoke();
        ApplyDownedVisual(instant: false);
    }

    [ClientRpc]
    private void TriggerDeathClientRpc()
    {
        OnDeath?.Invoke();
        if (IsServer)
        {
            if (deathDelay > 0f) Invoke(nameof(ExecuteDeathServer), deathDelay);
            else                 ExecuteDeathServer();
        }
        else
        {
            if (deathDelay > 0f) Invoke(nameof(HideOnClient), deathDelay);
            else                 HideOnClient();
        }
    }

    [ClientRpc]
    private void TriggerReviveClientRpc()
    {
        OnRevived?.Invoke();
        ApplyStandingVisual();
    }

    private void ExecuteDeathServer()
    {
        if (!IsServer) return;
        if (TryGetComponent<NetworkObject>(out var netObj))
        {
            if (destroyOnDeath) netObj.Despawn(true);
            else                gameObject.SetActive(false);
        }
        else
        {
            if (destroyOnDeath) Destroy(gameObject);
            else                gameObject.SetActive(false);
        }
    }

    private void HideOnClient()
    {
        if (IsServer) return;
        gameObject.SetActive(false);
    }

    private void HandleNetHealthChanged(int oldVal, int newVal)
    {
        _currentHealthDebug = newVal;
        OnHealthChanged?.Invoke(newVal, netMaxHealth.Value);
    }

    private void HandleNetIsDownChanged(bool oldVal, bool newVal)
    {
        if (newVal) ApplyDownedVisual(instant: false);
        else        ApplyStandingVisual();
    }

    private void ApplyDownedVisual(bool instant)
    {
        if (tiltRoutine != null) { StopCoroutine(tiltRoutine); tiltRoutine = null; }
        Quaternion target = originalLocalRotation * Quaternion.Euler(0f, 0f, downedRotationZ);
        if (instant) downedVisualRoot.localRotation = target;
        else         tiltRoutine = StartCoroutine(TiltToRoutine(target));
    }

    private void ApplyStandingVisual()
    {
        if (tiltRoutine != null) { StopCoroutine(tiltRoutine); tiltRoutine = null; }
        tiltRoutine = StartCoroutine(TiltToRoutine(originalLocalRotation));
    }

    private IEnumerator TiltToRoutine(Quaternion target)
    {
        while (Quaternion.Angle(downedVisualRoot.localRotation, target) > 0.5f)
        {
            downedVisualRoot.localRotation = Quaternion.RotateTowards(
                downedVisualRoot.localRotation, target, downedTiltSpeed * Time.deltaTime);
            yield return null;
        }
        downedVisualRoot.localRotation = target;
        tiltRoutine = null;
    }

    private void TriggerDamageFlash()
    {
        if (!flashRenderer) return;
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(DamageFlashRoutine());
    }

    private IEnumerator DamageFlashRoutine()
    {
        float half = flashDuration * 0.5f;
        for (int i = 0; i < flashCount; i++)
        {
            yield return FadeFlash(0f, 1f, half);
            yield return FadeFlash(1f, 0f, half);
        }
        flashRoutine = null;
    }

    private IEnumerator FadeFlash(float from, float to, float duration)
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

    private void SpawnDamageNumber(int amount)
    {
        if (!damageNumberPrefab) return;
        DamageNumber dmg = Instantiate(damageNumberPrefab,
            transform.position + damageNumberOffset, Quaternion.identity);
        dmg.Initialize(amount);
    }
}
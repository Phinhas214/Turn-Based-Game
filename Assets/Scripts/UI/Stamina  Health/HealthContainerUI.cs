using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Shows the local player's health.
/// Works with both HealthComponent (single-player) and
/// NetworkedHealthComponent (multiplayer).
/// </summary>
public class HealthContainerUI : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    [Header("Fire States")]
    public GameObject fireMax;
    public GameObject fireHigh;
    public GameObject fireMedium;
    public GameObject fireLow;

    [Header("UI")]
    public GameObject hoverOverlay;
    public TextMeshProUGUI healthText;

    [Header("Damage Flash")]
    [SerializeField] private GameObject damageFlashUI;
    [SerializeField] private float flashDuration = 0.25f;

    // One of these will be set depending on which mode we're in
    private HealthComponent           spHealth;
    private NetworkedHealthComponent  mpHealth;

    private int lastHealth  = -1;
    private int currentTier = -1;

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady          += OnLevelReady;
        NetworkedLevelGenerator.OnLevelReady += OnLevelReady;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady          -= OnLevelReady;
        NetworkedLevelGenerator.OnLevelReady -= OnLevelReady;
        UnsubscribeAll();
    }

    private void OnLevelReady()
    {
        UnsubscribeAll();
        StartCoroutine(WaitForLocalUnitThenBind());
    }

    private System.Collections.IEnumerator WaitForLocalUnitThenBind()
    {
        float timeout = 10f;
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            Unit unit = FindLocalUnit();
            if (unit != null)
            {
                BindToUnit(unit);
                yield break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        Debug.LogWarning("[HealthContainerUI] Timed out waiting for local unit.");
    }

    private void BindToUnit(Unit unit)
    {
        // Try multiplayer health first
        mpHealth = unit.GetComponent<NetworkedHealthComponent>();
        if (mpHealth != null)
        {
            mpHealth.OnHealthChanged += HandleHealthChanged;
            lastHealth = mpHealth.CurrentHealth;
            UpdateHealthText(lastHealth);
            UpdateFireState((float)lastHealth / mpHealth.MaxHealth);
            return;
        }

        // Fall back to single-player health
        spHealth = unit.GetComponent<HealthComponent>();
        if (spHealth != null)
        {
            spHealth.OnHealthChanged += HandleHealthChanged;
            lastHealth = spHealth.CurrentHealth;
            UpdateHealthText(lastHealth);
            UpdateFireState(spHealth.HealthPercent);
        }
    }

    private void UnsubscribeAll()
    {
        if (mpHealth != null) { mpHealth.OnHealthChanged -= HandleHealthChanged; mpHealth = null; }
        if (spHealth != null) { spHealth.OnHealthChanged -= HandleHealthChanged; spHealth = null; }
    }

    private void HandleHealthChanged(int current, int max)
    {
        if (current < lastHealth)
            TriggerDamageFlash();

        lastHealth = current;
        UpdateHealthText(current);
        UpdateFireState((float)current / max);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Returns the Unit owned by this client (MP) or the only Unit (SP).</summary>
    private Unit FindLocalUnit()
    {
        foreach (Unit unit in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            var netObj = unit.GetComponent<Unity.Netcode.NetworkObject>();
            if (netObj != null)
            {
                if (netObj.IsOwner) return unit;
            }
            else
            {
                return unit; // single-player
            }
        }
        return null;
    }

    private void UpdateHealthText(int value)
    {
        if (healthText != null)
            healthText.text = value.ToString();
    }

    private void UpdateFireState(float hpPercent)
    {
        int newTier = GetHealthTier(hpPercent);
        if (newTier == currentTier) return;

        currentTier = newTier;
        fireMax?.SetActive(currentTier    == 3);
        fireHigh?.SetActive(currentTier   == 2);
        fireMedium?.SetActive(currentTier == 1);
        fireLow?.SetActive(currentTier    == 0);
    }

    private int GetHealthTier(float hp)
    {
        if (hp > 0.75f) return 3;
        if (hp > 0.50f) return 2;
        if (hp > 0.25f) return 1;
        return 0;
    }

    public void OnPointerEnter(PointerEventData eventData) => hoverOverlay?.SetActive(true);
    public void OnPointerExit(PointerEventData eventData)  => hoverOverlay?.SetActive(false);

    private void TriggerDamageFlash()
    {
        if (!damageFlashUI) return;
        StopAllCoroutines();
        StartCoroutine(DamageFlashRoutine());
    }

    private System.Collections.IEnumerator DamageFlashRoutine()
    {
        damageFlashUI.SetActive(true);
        yield return new WaitForSeconds(flashDuration);
        damageFlashUI.SetActive(false);
    }
}
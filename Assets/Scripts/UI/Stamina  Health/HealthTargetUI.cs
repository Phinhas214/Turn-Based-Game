using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Generic health UI that displays the health of a target HealthComponent.
/// Can be used for player UI, enemy UI, boss UI, etc.
/// Each instance tracks its own target safely.
/// </summary>
public class HealthTargetUI : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    [Header("Target")]
    [SerializeField] private HealthComponent targetHealth;

    [Header("Fire States")]
    [SerializeField] private GameObject fireMax;
    [SerializeField] private GameObject fireHigh;
    [SerializeField] private GameObject fireMedium;
    [SerializeField] private GameObject fireLow;

    [Header("UI")]
    [SerializeField] private GameObject hoverOverlay;     // background only
    [SerializeField] private TextMeshProUGUI healthText;  // always visible

    [Header("Damage Flash")]
    [SerializeField] private GameObject damageFlashUI;
    [SerializeField] private float flashDuration = 0.25f;

    private int lastHealth = -1;
    private int currentTier = -1;

    // ─────────────────────────────────────────────────────────────

    void OnEnable()
    {
        if (targetHealth != null)
            BindToTarget(targetHealth);
    }

    void OnDisable()
    {
        UnbindFromTarget();
    }

    // ─────────────────────────────────────────────────────────────
    // PUBLIC API — THIS IS THE IMPORTANT PART
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Assigns a new health target (player, enemy, etc).
    /// </summary>
    public void SetTarget(HealthComponent newTarget)
    {
        if (targetHealth == newTarget)
            return;

        UnbindFromTarget();
        targetHealth = newTarget;
        BindToTarget(newTarget);
    }

    /// <summary>
    /// Clears the current target and hides the UI.
    /// </summary>
    public void ClearTarget()
    {
        UnbindFromTarget();
        targetHealth = null;
        gameObject.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────

    private void BindToTarget(HealthComponent hc)
    {
        if (hc == null) return;

        gameObject.SetActive(true);

        lastHealth = hc.CurrentHealth;

        hc.OnHealthChanged += HandleHealthChanged;

        UpdateHealthText(hc.CurrentHealth);
        UpdateFireState(hc.HealthPercent);
    }

    private void UnbindFromTarget()
    {
        if (targetHealth != null)
            targetHealth.OnHealthChanged -= HandleHealthChanged;
    }

    // ─────────────────────────────────────────────────────────────

    private void HandleHealthChanged(int current, int max)
    {
        if (current < lastHealth)
            TriggerDamageFlash();

        lastHealth = current;

        UpdateHealthText(current);
        UpdateFireState((float)current / max);
    }

    // ─────────────────────────────────────────────────────────────

    private void UpdateHealthText(int value)
    {
        if (healthText != null)
            healthText.text = value.ToString();
    }

    private void UpdateFireState(float hpPercent)
    {
        int newTier = GetHealthTier(hpPercent);
        if (newTier == currentTier)
            return;

        currentTier = newTier;

        fireMax?.SetActive(currentTier == 3);
        fireHigh?.SetActive(currentTier == 2);
        fireMedium?.SetActive(currentTier == 1);
        fireLow?.SetActive(currentTier == 0);
    }

    private int GetHealthTier(float hp)
    {
        if (hp > 0.75f) return 3;
        if (hp > 0.50f) return 2;
        if (hp > 0.25f) return 1;
        if (hp > 0f) return 0;
        return 0;
    }

    // ─────────────────────────────────────────────────────────────
    // Hover behavior
    // ─────────────────────────────────────────────────────────────

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (hoverOverlay != null)
            hoverOverlay.SetActive(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (hoverOverlay != null)
            hoverOverlay.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    // Damage flash
    // ─────────────────────────────────────────────────────────────

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
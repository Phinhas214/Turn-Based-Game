using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

public class HealthContainerUI : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    [Header("References")]
    public PlayerStats playerStats;

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

private int lastHealth = -1;

    int currentTier = -1;

    void OnEnable()
    {
        LevelGenerator.OnLevelReady += OnLevelReady;
    }

    void OnDisable()
    {
        LevelGenerator.OnLevelReady -= OnLevelReady;
    }

    void OnLevelReady()
    {
        Unit unit = FindFirstObjectByType<Unit>();
        if (unit != null)
        {
            playerStats = unit.GetComponent<PlayerStats>();
            UpdateFireState(); // force initial state
        }
        else
        {
            Debug.LogWarning("HealthContainerUI: No Unit found after level ready!");
        }
    }

    void Update()
    {
        if (!playerStats) return;

        // Detect damage
        int currentHealth = playerStats.currentHealth;

        if (lastHealth != -1 && currentHealth < lastHealth)
        {
            TriggerDamageFlash();
        }

        lastHealth = currentHealth;

        UpdateFireState();

        if (hoverOverlay.activeSelf)
        {
            UpdateHealthText();
        }
    }

    void UpdateFireState()
    {
        float hpPercent = (float)playerStats.currentHealth / playerStats.maxHealth;
        int newTier = GetHealthTier(hpPercent);

        if (newTier == currentTier)
            return;

        currentTier = newTier;

        fireMax.SetActive(currentTier == 3);
        fireHigh.SetActive(currentTier == 2);
        fireMedium.SetActive(currentTier == 1);
        fireLow.SetActive(currentTier == 0);
    }

    int GetHealthTier(float hp)
    {
        if (hp > 0.75f) return 3;   // Max
        if (hp > 0.50f) return 2;   // High
        if (hp > 0.25f) return 1;   // Medium
        if (hp > 0f) return 0;   // Low
        return 0;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!playerStats) return;

        hoverOverlay.SetActive(true);
        UpdateHealthText();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hoverOverlay.SetActive(false);
    }

    void UpdateHealthText()
    {
        healthText.text = playerStats.currentHealth.ToString();
    }

    // Visual feedback of dmg taken

    void TriggerDamageFlash()
        {
            if (!damageFlashUI) return;

            StopAllCoroutines();
            StartCoroutine(DamageFlashRoutine());
        }

        System.Collections.IEnumerator DamageFlashRoutine()
        {
            damageFlashUI.SetActive(true);
            yield return new WaitForSeconds(flashDuration);
            damageFlashUI.SetActive(false);
        }
}
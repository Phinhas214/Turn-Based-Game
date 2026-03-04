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
    public GameObject hoverOverlay;          // background only
    public TextMeshProUGUI healthText;       // always visible

    [Header("Damage Flash")]
    [SerializeField] private GameObject damageFlashUI;
    [SerializeField] private float flashDuration = 0.25f;

    private int lastHealth = -1;
    private int currentTier = -1;

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

            // Force full initial sync
            lastHealth = playerStats.currentHealth;
            UpdateHealthText();
            UpdateFireState();
        }
        else
        {
            Debug.LogWarning("HealthContainerUI: No Unit found after level ready!");
        }
    }

    void Update()
    {
        if (!playerStats) return;

        int currentHealth = playerStats.currentHealth;

        // Health changed?
        if (currentHealth != lastHealth)
        {
            if (currentHealth < lastHealth)
                TriggerDamageFlash();

            lastHealth = currentHealth;

            UpdateHealthText();
            UpdateFireState();
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
        if (hp > 0.75f) return 3;
        if (hp > 0.50f) return 2;
        if (hp > 0.25f) return 1;
        if (hp > 0f) return 0;
        return 0;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hoverOverlay.SetActive(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hoverOverlay.SetActive(false);
    }

    void UpdateHealthText()
    {
        healthText.text = playerStats.currentHealth.ToString();
    }

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
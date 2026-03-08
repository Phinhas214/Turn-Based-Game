using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Shows stamina particles for the local player.
/// Works in both single-player and multiplayer — finds the locally-owned unit.
/// </summary>
public class StaminaContainerUI : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    public StaminaParticleUI particlePrefab;
    public RectTransform     particleLayer;

    [Header("Mouse Interaction")]
    private float mouseForceRadius   = 60f;
    private float mouseForceStrength = 300f;

    [Header("UI")]
    public GameObject        hoverOverlay;
    public TextMeshProUGUI   staminaText;

    private RectTransform         rect;
    private List<StaminaParticleUI> particles = new List<StaminaParticleUI>();
    private PlayerStats           playerStats;
    private int                   lastStamina = -1;

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady          += OnLevelReady;
        NetworkedLevelGenerator.OnLevelReady += OnLevelReady;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady          -= OnLevelReady;
        NetworkedLevelGenerator.OnLevelReady -= OnLevelReady;
    }

    private void OnLevelReady()
    {
        Unit unit = FindLocalUnit();
        if (unit == null)
        {
            Debug.LogWarning("[StaminaContainerUI] No local unit found.");
            return;
        }

        playerStats = unit.GetComponent<PlayerStats>();
        if (playerStats == null) return;

        lastStamina = playerStats.currentStamina;
        UpdateStaminaText();
        UpdateParticles(lastStamina);
    }

    private void Update()
    {
        if (!playerStats) return;

        if (playerStats.currentStamina != lastStamina)
        {
            lastStamina = playerStats.currentStamina;
            UpdateParticles(lastStamina);
            UpdateStaminaText();
        }

        DisturbParticlesWithMouse();
        ApplyParticleRepulsion();
    }

    public void OnPointerEnter(PointerEventData eventData) => hoverOverlay.SetActive(true);
    public void OnPointerExit(PointerEventData eventData)  => hoverOverlay.SetActive(false);

    // ── Helpers ───────────────────────────────────────────────────────────

    private Unit FindLocalUnit()
    {
        foreach (Unit unit in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            var netObj = unit.GetComponent<Unity.Netcode.NetworkObject>();
            if (netObj != null) { if (netObj.IsOwner) return unit; }
            else return unit; // single-player
        }
        return null;
    }

    private void UpdateStaminaText()
    {
        if (staminaText != null)
            staminaText.text = playerStats.currentStamina.ToString();
    }

    private void DisturbParticlesWithMouse()
    {
        Vector2 localMousePos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rect, Input.mousePosition, Camera.main, out localMousePos);

        if (!rect.rect.Contains(localMousePos)) return;

        foreach (var p in particles)
        {
            RectTransform pr  = p.GetComponent<RectTransform>();
            Vector2       dir = pr.anchoredPosition - localMousePos;
            float         dist = dir.magnitude;

            if (dist < mouseForceRadius)
            {
                float strength = 1f - (dist / mouseForceRadius);
                p.ApplyForce(dir.normalized * strength * mouseForceStrength * Time.deltaTime);
            }
        }
    }

    private void ApplyParticleRepulsion()
    {
        float repelRadius   = 18f;
        float repelStrength = 80f;

        for (int i = 0; i < particles.Count; i++)
        {
            RectTransform a = particles[i].GetComponent<RectTransform>();
            for (int j = i + 1; j < particles.Count; j++)
            {
                RectTransform b   = particles[j].GetComponent<RectTransform>();
                Vector2       dir = a.anchoredPosition - b.anchoredPosition;
                float         dist = dir.magnitude;

                if (dist < repelRadius && dist > 0.01f)
                {
                    float  strength = 1f - (dist / repelRadius);
                    Vector2 force   = dir.normalized * strength * repelStrength;
                    particles[i].ApplyForce( force * Time.deltaTime);
                    particles[j].ApplyForce(-force * Time.deltaTime);
                }
            }
        }
    }

    private void UpdateParticles(int targetCount)
    {
        while (particles.Count > targetCount)
        {
            Destroy(particles[particles.Count - 1].gameObject);
            particles.RemoveAt(particles.Count - 1);
        }

        while (particles.Count < targetCount)
            SpawnParticle();
    }

    private void SpawnParticle()
    {
        StaminaParticleUI p  = Instantiate(particlePrefab, particleLayer, false);
        RectTransform     pr = p.GetComponent<RectTransform>();

        Rect bounds = GetInnerBounds();
        pr.anchoredPosition = new Vector2(
            Random.Range(bounds.xMin, bounds.xMax),
            Random.Range(bounds.yMin, bounds.yMax));

        p.Initialize(bounds);
        particles.Add(p);
    }

    private Rect GetInnerBounds()
    {
        Vector2 size = rect.rect.size * 0.5f;
        return new Rect(-size, size * 2f);
    }
}
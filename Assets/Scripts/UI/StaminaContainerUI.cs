using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class StaminaContainerUI : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    public PlayerStats playerStats;
    public StaminaParticleUI particlePrefab;

    public RectTransform particleLayer;

    float mouseForceRadius = 60f;
    float mouseForceStrength = 300f;

    public GameObject hoverOverlay;
    public TMPro.TextMeshProUGUI staminaText;

    RectTransform rect;
    List<StaminaParticleUI> particles = new();

    int lastStamina = -1;

    void Awake()
    {
        rect = GetComponent<RectTransform>();
    }

    void Update()
    {
        if (!playerStats) return;

        if (playerStats.currentStamina != lastStamina)
        {
            UpdateParticles(playerStats.currentStamina);
            lastStamina = playerStats.currentStamina;
        }

        DisturbParticlesWithMouse();
        ApplyParticleRepulsion();

    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hoverOverlay.SetActive(true);
        staminaText.text = playerStats.currentStamina.ToString();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hoverOverlay.SetActive(false);
    }

    void DisturbParticlesWithMouse()
    {
        Vector2 localMousePos;

        // Convert screen mouse position to container-local space
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rect,
            Input.mousePosition,
            null,
            out localMousePos))
            return;

        // Only disturb if mouse is inside container
        if (!rect.rect.Contains(localMousePos))
            return;

        foreach (var p in particles)
        {
            RectTransform pr = p.GetComponent<RectTransform>();
            Vector2 dir = pr.anchoredPosition - localMousePos;
            float distance = dir.magnitude;

            if (distance < mouseForceRadius)
            {
                float strength = 1f - (distance / mouseForceRadius);
                Vector2 force = dir.normalized * strength * mouseForceStrength;

                p.ApplyForce(force * Time.deltaTime);
            }
        }
    }

    void ApplyParticleRepulsion()
    {
        float repelRadius = 18f;
        float repelStrength = 80f;

        for (int i = 0; i < particles.Count; i++)
        {
            RectTransform a = particles[i].GetComponent<RectTransform>();

            for (int j = i + 1; j < particles.Count; j++)
            {
                RectTransform b = particles[j].GetComponent<RectTransform>();

                Vector2 dir = a.anchoredPosition - b.anchoredPosition;
                float distance = dir.magnitude;

                if (distance < repelRadius && distance > 0.01f)
                {
                    float strength = 1f - (distance / repelRadius);
                    Vector2 force = dir.normalized * strength * repelStrength;

                    particles[i].ApplyForce(force * Time.deltaTime);
                    particles[j].ApplyForce(-force * Time.deltaTime);
                }
            }
        }
    }


    void UpdateParticles(int targetCount)
    {
        // Remove extra
        while (particles.Count > targetCount)
        {
            Destroy(particles[^1].gameObject);
            particles.RemoveAt(particles.Count - 1);
        }

        // Add missing
        while (particles.Count < targetCount)
        {
            SpawnParticle();
        }
    }

    void SpawnParticle()
    {
        StaminaParticleUI p = Instantiate(particlePrefab, particleLayer, false);
        RectTransform pr = p.GetComponent<RectTransform>();

        Rect bounds = GetInnerBounds();
        pr.anchoredPosition = new Vector2(
            Random.Range(bounds.xMin, bounds.xMax),
            Random.Range(bounds.yMin, bounds.yMax)
        );

        p.Initialize(bounds);
        particles.Add(p);
    }

    Rect GetInnerBounds()
    {
        Vector2 size = rect.rect.size * 0.5f;
        return new Rect(-size, size * 2f);
    }
}

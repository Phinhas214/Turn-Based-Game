using System.Collections.Generic;
using UnityEngine;

public class StaminaContainerUI : MonoBehaviour
{
    public PlayerStats playerStats;
    public StaminaParticleUI particlePrefab;

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
        StaminaParticleUI p = Instantiate(particlePrefab, transform);
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

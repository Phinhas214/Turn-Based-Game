using UnityEngine;

public class StaminaParticleUI : MonoBehaviour
{
    RectTransform rect;
    Vector2 velocity;
    Rect bounds;

    public void Initialize(Rect containerRect)
    {
        rect = GetComponent<RectTransform>();
        bounds = containerRect;

        velocity = Random.insideUnitCircle.normalized * Random.Range(20f, 40f);
    }

    void Update()
    {
        Vector2 pos = rect.anchoredPosition;
        pos += velocity * Time.deltaTime;

        // Bounce X
        if (pos.x < bounds.xMin || pos.x > bounds.xMax)
        {
            velocity.x *= -1;
            pos.x = Mathf.Clamp(pos.x, bounds.xMin, bounds.xMax);
        }

        // Bounce Y
        if (pos.y < bounds.yMin || pos.y > bounds.yMax)
        {
            velocity.y *= -1;
            pos.y = Mathf.Clamp(pos.y, bounds.yMin, bounds.yMax);
        }

        rect.anchoredPosition = pos;
    }
}

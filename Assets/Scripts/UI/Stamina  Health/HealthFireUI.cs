using UnityEngine;

/// <summary>
/// Drives health-tier fire animations for the local player.
/// Works with both HealthComponent (SP) and NetworkedHealthComponent (MP).
/// </summary>
public class HealthFireUI : MonoBehaviour
{
    public Animator animator;

    private HealthComponent          spHealth;
    private NetworkedHealthComponent mpHealth;
    private int lastTier = -1;

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

        // If this GameObject was hidden by the lose/pause screen, re-enable it
        // before starting the coroutine — Unity can't start coroutines on
        // inactive GameObjects even if they're about to become active.
        if (!gameObject.activeInHierarchy)
            gameObject.SetActive(true);

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
                mpHealth = unit.GetComponent<NetworkedHealthComponent>();
                if (mpHealth != null)
                {
                    mpHealth.OnHealthChanged += OnHealthChanged;
                    OnHealthChanged(mpHealth.CurrentHealth, mpHealth.MaxHealth);
                    yield break;
                }

                spHealth = unit.GetComponent<HealthComponent>();
                if (spHealth != null)
                {
                    spHealth.OnHealthChanged += OnHealthChanged;
                    OnHealthChanged(spHealth.CurrentHealth, spHealth.MaxHealth);
                    yield break;
                }
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        Debug.LogWarning("[HealthFireUI] Timed out waiting for local unit.");
    }

    private void UnsubscribeAll()
    {
        if (mpHealth != null) { mpHealth.OnHealthChanged -= OnHealthChanged; mpHealth = null; }
        if (spHealth != null) { spHealth.OnHealthChanged -= OnHealthChanged; spHealth = null; }
    }

    private void OnHealthChanged(int current, int max)
    {
        if (max <= 0 || !animator) return;
        float hpPercent = (float)current / max;
        int tier = CalculateTier(hpPercent);
        if (tier != lastTier)
        {
            animator.SetInteger("HealthTier", tier);
            lastTier = tier;
        }
    }

    private Unit FindLocalUnit()
    {
        foreach (Unit unit in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            var netObj = unit.GetComponent<Unity.Netcode.NetworkObject>();
            if (netObj != null) { if (netObj.IsOwner) return unit; }
            else return unit;
        }
        return null;
    }

    private int CalculateTier(float hp)
    {
        if (hp > 0.75f) return 3;
        if (hp > 0.50f) return 2;
        if (hp > 0.25f) return 1;
        return 0;
    }
}
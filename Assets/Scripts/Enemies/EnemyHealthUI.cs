using UnityEngine;

public class EnemyHealthUI : MonoBehaviour
{
    public static EnemyHealthUI Instance { get; private set; }

    [SerializeField] private HealthTargetUI healthUI;

    void Awake()
    {
        Instance = this;
    }

    public void SetTarget(HealthComponent health)
    {
        if (healthUI == null) return;

        healthUI.SetTarget(health);
    }

    public void ClearTarget()
    {
        if (healthUI == null) return;

        healthUI.ClearTarget();
    }
}
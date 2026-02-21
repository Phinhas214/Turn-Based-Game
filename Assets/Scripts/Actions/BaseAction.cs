using System;
using UnityEngine;

public abstract class BaseAction : MonoBehaviour
{
    protected Unit unit;
    protected bool isActive;
    protected Action onActionComplete;
    protected PlayerStats playerStats;

    protected virtual void Awake()
    {
        unit = GetComponent<Unit>();
        playerStats = GetComponent<PlayerStats>();
    }

    public abstract string GetActionName();
}
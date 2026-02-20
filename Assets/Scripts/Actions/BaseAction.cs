using System;
using UnityEngine;

// "abstract" here won't let us create instances of this class
// which is what we want
// we only want to instantitiate classes that inherit base class
public abstract class BaseAction : MonoBehaviour
{
    // classes that extend this class can access these fields
    protected Unit unit;
    protected bool isActive;
    protected Action onActionComplete;
    protected PlayerStats playerStats;

    protected virtual void Awake() {
      unit = GetComponent<Unit>();
      playerStats = GetComponent<PlayerStats>();
    }

    // abstract means that we'll be forced to implement this function
    // in all the other classes that inherit this BaseAction class
    public abstract string GetActionName();
}

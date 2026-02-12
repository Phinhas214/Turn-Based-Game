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

    protected virtual void Awake()
    {
        unit = GetComponent<Unit>();
    }
}

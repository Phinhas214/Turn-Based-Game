using UnityEngine;

/// <summary>
/// Works with both UnitActionSystem (single-player) and
/// NetworkedUnitActionSystem (multiplayer). Subscribes to whichever is present.
/// </summary>
public class ActionBusyUI : MonoBehaviour
{
    private void Start()
    {
        if (NetworkedUnitActionSystem.Instance != null)
            NetworkedUnitActionSystem.Instance.OnBusyChanged += OnBusyChanged;
        else if (UnitActionSystem.Instance != null)
            UnitActionSystem.Instance.OnBusyChanged += OnBusyChanged;

        Hide();
    }

    private void OnDestroy()
    {
        if (NetworkedUnitActionSystem.Instance != null)
            NetworkedUnitActionSystem.Instance.OnBusyChanged -= OnBusyChanged;
        else if (UnitActionSystem.Instance != null)
            UnitActionSystem.Instance.OnBusyChanged -= OnBusyChanged;
    }

    private void OnBusyChanged(object sender, bool isBusy)
    {
        if (isBusy) Show();
        else        Hide();
    }

    private void Show() => gameObject.SetActive(true);
    private void Hide() => gameObject.SetActive(false);
}
using System;
using UnityEngine;

/// <summary>
/// Shows the selection ring under the selected unit.
/// Works with both UnitActionSystem (single-player) and
/// NetworkedUnitActionSystem (multiplayer).
/// </summary>
public class UnitSelectedVisual : MonoBehaviour
{
    [SerializeField] private Unit unit;

    private MeshRenderer meshRenderer;

    private void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
    }

    private void Start()
    {
        if (NetworkedUnitActionSystem.Instance != null)
            NetworkedUnitActionSystem.Instance.OnSelectedUnitChange += OnSelectedUnitChange;
        else if (UnitActionSystem.Instance != null)
            UnitActionSystem.Instance.OnSelectedUnitChange += OnSelectedUnitChange;

        UpdateVisual();
    }

    private void OnDestroy()
    {
        if (NetworkedUnitActionSystem.Instance != null)
            NetworkedUnitActionSystem.Instance.OnSelectedUnitChange -= OnSelectedUnitChange;
        else if (UnitActionSystem.Instance != null)
            UnitActionSystem.Instance.OnSelectedUnitChange -= OnSelectedUnitChange;
    }

    private void OnSelectedUnitChange(object sender, EventArgs e) => UpdateVisual();

    private void UpdateVisual()
    {
        Unit selected = GetSelectedUnit();

        // In multiplayer: only show the ring on the locally-owned unit
        // In single-player: show on whichever unit is selected
        meshRenderer.enabled = (selected == unit);
    }

    private Unit GetSelectedUnit()
    {
        if (NetworkedUnitActionSystem.Instance != null)
            return NetworkedUnitActionSystem.Instance.GetSelectedUnit();

        return UnitActionSystem.Instance?.GetSelectedUnit();
    }
}
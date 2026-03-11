using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Populates the action bar with one button per action on the selected unit.
/// Works with both UnitActionSystem (single-player) and
/// NetworkedUnitActionSystem (multiplayer).
/// </summary>
public class UnitActionSystemUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform actionButtonPrefab;
    [SerializeField] private Transform actionButtonContainerTransform;

    [SerializeField] private float buttonSpacing = 8f;
    [SerializeField] private float buttonPadding = 4f;
    public enum ActionLayoutDirection
    {
        Horizontal,
        Vertical
    }

    [SerializeField] private ActionLayoutDirection layoutDirection = ActionLayoutDirection.Horizontal;

    private List<ActionButtonUI> actionButtonUIList = new List<ActionButtonUI>();

    private void Start()
    {
        if (NetworkedUnitActionSystem.Instance != null)
        {
            NetworkedUnitActionSystem.Instance.OnSelectedUnitChange   += OnSelectedUnitChanged;
            NetworkedUnitActionSystem.Instance.OnSelectedActionChange += OnSelectedActionChanged;
        }
        else if (UnitActionSystem.Instance != null)
        {
            UnitActionSystem.Instance.OnSelectedUnitChange += OnSelectedUnitChanged;
            UnitActionSystem.Instance.OnSelectedActionChange += OnSelectedActionChanged;
        }

        CreateUnitActionButtons();
        UpdateSelectedVisual();
    }

    private void OnDestroy()
    {
        if (NetworkedUnitActionSystem.Instance != null)
        {
            NetworkedUnitActionSystem.Instance.OnSelectedUnitChange   -= OnSelectedUnitChanged;
            NetworkedUnitActionSystem.Instance.OnSelectedActionChange -= OnSelectedActionChanged;
        }
        else if (UnitActionSystem.Instance != null)
        {
            UnitActionSystem.Instance.OnSelectedUnitChange   -= OnSelectedUnitChanged;
            UnitActionSystem.Instance.OnSelectedActionChange -= OnSelectedActionChanged;
        }
    }

    private void Update()
    {
        UpdateSelectedVisual();
    }

    private void CreateUnitActionButtons()
    {
        foreach (Transform child in actionButtonContainerTransform)
            Destroy(child.gameObject);

        actionButtonUIList.Clear();

        Unit selectedUnit = GetSelectedUnit();
        if (selectedUnit == null) return;

        foreach (BaseAction action in selectedUnit.GetBaseActionArray())
        {
            Transform buttonTransform = Instantiate(actionButtonPrefab, actionButtonContainerTransform);
            ActionButtonUI actionButtonUI = buttonTransform.GetComponent<ActionButtonUI>();
            actionButtonUI.SetBaseAction(action);
            actionButtonUIList.Add(actionButtonUI);
        }
    }

    private void UpdateSelectedVisual()
    {
        foreach (ActionButtonUI button in actionButtonUIList)
            button.UpdateSelectedVisual();
    }

    private Unit GetSelectedUnit()
    {
        if (NetworkedUnitActionSystem.Instance != null)
            return NetworkedUnitActionSystem.Instance.GetSelectedUnit();

        return UnitActionSystem.Instance?.GetSelectedUnit();
    }

    private void OnSelectedUnitChanged(object sender, EventArgs e)
    {
        CreateUnitActionButtons();
        UpdateSelectedVisual();
    }

    private void OnSelectedActionChanged(object sender, EventArgs e)
    {
        UpdateSelectedVisual();
    }

}
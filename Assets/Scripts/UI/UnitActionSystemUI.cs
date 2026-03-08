using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Populates the action bar with one button per action on the selected unit.
// Refreshes affordability state every frame so stamina cost badges stay accurate.
// Drop-in replacement for the original UnitActionSystemUI.
public class UnitActionSystemUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Prefab for a single action button. Must have an ActionButtonUI component.")]
    [SerializeField] private Transform actionButtonPrefab;

    [Tooltip("Parent transform that action buttons are spawned inside.")]
    [SerializeField] private Transform actionButtonContainerTransform;
    public enum ActionLayoutDirection
    {
        Horizontal,
        Vertical
    }

    [SerializeField] private ActionLayoutDirection layoutDirection = ActionLayoutDirection.Horizontal;

    private List<ActionButtonUI> actionButtonUIList = new List<ActionButtonUI>();


    private void Start()
    {
        if (UnitActionSystem.Instance != null)
        {
            UnitActionSystem.Instance.OnSelectedUnitChange   += OnSelectedUnitChanged;
            UnitActionSystem.Instance.OnSelectedActionChange += OnSelectedActionChanged;
        }

        CreateUnitActionButtons();
        UpdateSelectedVisual();
    }

    private void OnDestroy()
    {
        if (UnitActionSystem.Instance != null)
        {
            UnitActionSystem.Instance.OnSelectedUnitChange   -= OnSelectedUnitChanged;
            UnitActionSystem.Instance.OnSelectedActionChange -= OnSelectedActionChanged;
        }
    }

    // Refresh affordability every frame (stamina ticks down during moves etc.)
    private void Update()
    {
        UpdateSelectedVisual();
    }

    //  Button creation

    private void CreateUnitActionButtons()
    {
        // Clear existing buttons
        foreach (Transform child in actionButtonContainerTransform)
            Destroy(child.gameObject);

        actionButtonUIList.Clear();

        Unit selectedUnit = UnitActionSystem.Instance?.GetSelectedUnit();
        if (selectedUnit == null) return;

        foreach (BaseAction action in selectedUnit.GetBaseActionArray())
        {
            Transform buttonTransform = Instantiate(actionButtonPrefab, actionButtonContainerTransform);
            ActionButtonUI actionButtonUI = buttonTransform.GetComponent<ActionButtonUI>();
            actionButtonUI.SetBaseAction(action);
            actionButtonUIList.Add(actionButtonUI);
        }
    }

    //  Visual refresh

    private void UpdateSelectedVisual()
    {
        foreach (ActionButtonUI button in actionButtonUIList)
            button.UpdateSelectedVisual();
    }

    //  Event handlers

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
using System;
using UnityEngine;
using UnityEngine.EventSystems;

public class UnitActionSystem : MonoBehaviour
{
    public static UnitActionSystem Instance { get; private set; }
    
    public event EventHandler OnSelectedUnitChange;
    public event EventHandler OnSelectedActionChange;
    public event EventHandler<bool> OnBusyChanged;

    [SerializeField] private LayerMask unitLayerMask;
    
    private Unit selectedUnit;
    private BaseAction selectedAction;
    private bool isBusy;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        } 
        Instance = this;
    }

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady += OnLevelReady;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady -= OnLevelReady;
    }

    private void OnLevelReady()
    {
        Unit spawnedUnit = FindFirstObjectByType<Unit>();
        
        if (spawnedUnit != null)
        {
            SetSelectedUnit(spawnedUnit);
            Debug.Log($"UnitActionSystem: Auto-selected spawned unit {spawnedUnit.name}");
        }
        else
        {
            Debug.LogWarning("UnitActionSystem: No unit found after level ready!");
        }
    }

    private void Update()
    {
        if (LevelGrid.Instance == null || !LevelGrid.Instance.IsInitialized()) return;
        if (isBusy) return;
        if (selectedUnit == null) return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        if (TryHandleUnitSelection()) return;

        HandleSelectedAction();
    }

    private void HandleSelectedAction()
    {
        if (Input.GetMouseButtonDown(0))
        {
            GridPosition mouseGridPosition = LevelGrid.Instance.GetGridPosition(MouseWorld.GetPosition());

            switch (selectedAction)
            {
                case MoveAction moveAction:
                    if (moveAction.isValidActionGridPosition(mouseGridPosition))
                    {
                        SetBusy();
                        moveAction.Move(mouseGridPosition, ClearBusy);
                    }
                    break;
                case SpinAction spinAction:
                    SetBusy();
                    spinAction.Spin(ClearBusy);
                    break;
            }
        }
    }

    private void SetBusy()
    {
        isBusy = true;
        OnBusyChanged?.Invoke(this, isBusy);
    }

    private void ClearBusy()
    {
        isBusy = false;
        OnBusyChanged?.Invoke(this, isBusy);
    }

    private bool TryHandleUnitSelection()
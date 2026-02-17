using System;
using UnityEngine;

public class UnitActionSystem : MonoBehaviour
{
    public static UnitActionSystem Instance { get; private set; }
    
    public event EventHandler OnSelectedUnitChange;
    
    [SerializeField] private LayerMask unitLayerMask;
    
    private Unit selectedUnit;
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
        // Listen for when level is ready then find the spawned player
        LevelGenerator.OnLevelReady += OnLevelReady;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady -= OnLevelReady;
    }

    private void OnLevelReady()
    {
        // Find the player that was just spawned by LevelGenerator
        Unit spawnedUnit = FindFirstObjectByType<Unit>();
        
        if (spawnedUnit != null)
        {
            // Auto select the player when level is ready
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
        if (LevelGrid.Instance == null || !LevelGrid.Instance.IsInitialized())
        {
            return;
        }

        if (isBusy) return;

        if (selectedUnit == null)
        {
            if (Input.GetMouseButtonDown(0))
            {
                TryHandleUnitSelection();
            }
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {  
            if (TryHandleUnitSelection()) return;

            Vector3 mouseWorldPos = MouseWorld.GetPosition();
            GridPosition mouseGridPosition = LevelGrid.Instance.GetGridPosition(mouseWorldPos);
            
            MoveAction moveAction = selectedUnit.GetMoveAction();
            if (moveAction != null && moveAction.isValidActionGridPosition(mouseGridPosition))
            {
                SetBusy();
                moveAction.Move(mouseGridPosition, ClearBusy);
            }
        }

        if (Input.GetMouseButtonDown(1))
        {
            SpinAction spinAction = selectedUnit.GetSpinAction();
            if (spinAction != null)
            {
                SetBusy();
                spinAction.Spin(ClearBusy);
            }
        }
    }

    private void SetBusy()
    {
        isBusy = true;
    }

    private void ClearBusy()
    {
        isBusy = false;
    }

    private bool TryHandleUnitSelection()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit raycastHit, float.MaxValue, unitLayerMask))
        {
            if (raycastHit.transform.TryGetComponent<Unit>(out Unit unit))
            {
                SetSelectedUnit(unit);
                return true;
            }
        }
        return false;
    }

    private void SetSelectedUnit(Unit unit)
    {   
        selectedUnit = unit;
        OnSelectedUnitChange?.Invoke(this, EventArgs.Empty);
    }

    public Unit GetSelectedUnit()
    {
        return selectedUnit;
    }
}
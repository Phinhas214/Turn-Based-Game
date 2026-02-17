using System;
using UnityEngine;

public class UnitActionSystem : MonoBehaviour
{
    public static UnitActionSystem Instance { get; private set; }

    public event EventHandler OnSelectedUnitChange;

    [SerializeField] private Unit selectedUnit;
    [SerializeField] private LayerMask unitLayerMask;

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

            GridPosition mouseGridPosition = LevelGrid.Instance.GetGridPosition(MouseWorld.GetPosition());
            
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
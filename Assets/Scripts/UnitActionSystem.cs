using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// Central input handler for unit selection and action execution.
/// Handles MoveAction, SpinAction, and the new CombatAction.
/// Replaces the original UnitActionSystem — drop-in compatible.
public class UnitActionSystem : MonoBehaviour
{
    public static UnitActionSystem Instance { get; private set; }

    [Header("Selection")]
    [Tooltip("Layer mask used to detect clicks on unit GameObjects.")]
    [SerializeField] private LayerMask unitLayerMask;

    public event EventHandler         OnSelectedUnitChange;
    public event EventHandler         OnSelectedActionChange;
    public event EventHandler<bool>   OnBusyChanged;

    private Unit       selectedUnit;
    private BaseAction selectedAction;
    private bool       isBusy;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()  => LevelGenerator.OnLevelReady += OnLevelReady;
    private void OnDisable() => LevelGenerator.OnLevelReady -= OnLevelReady;

    private void OnLevelReady()
    {
        Unit spawnedUnit = FindFirstObjectByType<Unit>();
        if (spawnedUnit != null)
        {
            SetSelectedUnit(spawnedUnit);
            Debug.Log($"[UnitActionSystem] Auto-selected {spawnedUnit.name}.");
        }
        else
        {
            Debug.LogWarning("[UnitActionSystem] No Unit found after level ready!");
        }
    }

    private void Update()
    {
        if (LevelGrid.Instance == null || !LevelGrid.Instance.IsInitialized()) return;
        if (isBusy)      return;
        if (selectedUnit == null) return;

        // NEW: block all player input while enemies are taking their turns
        if (TurnSystem.Instance != null && !TurnSystem.Instance.IsPlayerTurn) return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        if (TryHandleUnitSelection()) return;
        HandleSelectedAction();
    }

    private void HandleSelectedAction()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        GridPosition mouseGridPos = LevelGrid.Instance.GetGridPosition(MouseWorld.GetPosition());

        switch (selectedAction)
        {
            case MoveAction moveAction:
                if (moveAction.isValidActionGridPosition(mouseGridPos))
                {
                    SetBusy();
                    moveAction.Move(mouseGridPos, ClearBusy);
                }
                break;

            case SpinAction spinAction:
                SetBusy();
                spinAction.Spin(ClearBusy);
                break;

            case CombatAction combatAction:
                if (!combatAction.CanAfford())
                {
                    Debug.Log("[UnitActionSystem] Not enough stamina for that action.");
                    return;
                }
                if (combatAction.IsValidTarget(mouseGridPos))
                {
                    SetBusy();
                    combatAction.PerformAttack(mouseGridPos, ClearBusy);
                }
                break;
        }
    }

    private bool TryHandleUnitSelection()
    {
        if (!Input.GetMouseButtonDown(0)) return false;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, float.MaxValue, unitLayerMask))
        {
            if (hit.transform.TryGetComponent<Unit>(out Unit unit))
            {
                SetSelectedUnit(unit);
                return true;
            }
        }
        return false;
    }

    private void SetBusy()
    {
        isBusy = true;
        OnBusyChanged?.Invoke(this, true);
    }

    private void ClearBusy()
    {
        isBusy = false;
        OnBusyChanged?.Invoke(this, false);
    }

    private void SetSelectedUnit(Unit unit)
    {
        selectedUnit = unit;
        SetSelectedAction(unit.GetMoveAction());
        OnSelectedUnitChange?.Invoke(this, EventArgs.Empty);
    }

    public void SetSelectedAction(BaseAction baseAction)
    {
        selectedAction = baseAction;
        OnSelectedActionChange?.Invoke(this, EventArgs.Empty);
    }

    public Unit       GetSelectedUnit()   => selectedUnit;
    public BaseAction GetSelectedAction() => selectedAction;
}
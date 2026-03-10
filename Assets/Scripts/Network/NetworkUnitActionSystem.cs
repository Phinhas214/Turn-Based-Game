using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Networked UnitActionSystem.
///
/// KEY CHANGE from original:
///   - Before processing any input or showing the selected unit, check if
///     the unit belongs to the LOCAL player (IsOwner / IsLocalPlayer).
///   - Non-local units don't get highlights, click handling, or action buttons.
///
/// SETUP:
///   - Replace UnitActionSystem with this script (or patch the original).
///   - All existing event subscriptions (OnSelectedUnitChange, etc.) still work.
/// </summary>
public class NetworkedUnitActionSystem : MonoBehaviour
{
    public static NetworkedUnitActionSystem Instance { get; private set; }

    [Header("Selection")]
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

    private void OnEnable()
    {
        // Listen for when the level is ready so we can auto-select the local unit
        NetworkedLevelGenerator.OnLevelReady += OnLevelReady;
    }

    private void OnDisable()
    {
        NetworkedLevelGenerator.OnLevelReady -= OnLevelReady;
    }

    private void OnLevelReady()
    {
        // Find the Unit that belongs to the local client
        TrySelectLocalUnit();
    }

    private void TrySelectLocalUnit()
    {
        // Find all units in the scene and pick the one owned by this client
        foreach (Unit unit in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            NetworkObject netObj = unit.GetComponent<NetworkObject>();

            // If NetworkObject exists, only select the one we own
            if (netObj != null)
            {
                if (netObj.IsOwner)
                {
                    SetSelectedUnit(unit);
                    Debug.Log($"[NetworkedUnitActionSystem] Auto-selected local unit: {unit.name}");
                    return;
                }
            }
            else
            {
                // Fallback for non-networked testing: select any unit
                SetSelectedUnit(unit);
                return;
            }
        }

        Debug.LogWarning("[NetworkedUnitActionSystem] Could not find local player unit.");
    }

    private void Update()
    {
        if (LevelGrid.Instance == null || !LevelGrid.Instance.IsInitialized()) return;
        if (isBusy)      return;
        if (selectedUnit == null) return;

        // CRITICAL: Block input during enemy turns
        if (MultiplayerTurnSystem.Instance != null && !MultiplayerTurnSystem.Instance.IsPlayerTurn) return;

        // CRITICAL: Block input if the selected unit is not owned by this client
        NetworkObject netObj = selectedUnit.GetComponent<NetworkObject>();
        if (netObj != null && !netObj.IsOwner) return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        if (TryHandleUnitSelection()) return;
        HandleSelectedAction();
    }

    private void HandleSelectedAction()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        // FIX: Use the room the mouse is actually over, not the global current room.
        // LevelGrid.Instance.GetGridPosition() uses whatever room was last set globally,
        // which is wrong after moving rooms. Instead find the room under the mouse and
        // use that room's own GetGridPosition().
        Vector3  mouseWorld  = MouseWorld.GetPosition();
        RoomGrid mouseRoom   = LevelGrid.Instance.GetRoomAtPosition(mouseWorld);
        if (mouseRoom == null) return;

        GridPosition mouseGridPos = mouseRoom.GetGridPosition(mouseWorld);

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
                    Debug.Log("[NetworkedUnitActionSystem] Not enough stamina.");
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
                // Only allow selecting the local player's unit
                NetworkObject netObj = unit.GetComponent<NetworkObject>();
                if (netObj != null && !netObj.IsOwner) return false;

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
using UnityEngine;

public class ClickDebugger : MonoBehaviour
{
    private void Update()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        // Test 1: Does raycast hit ANYTHING at all
        if (Physics.Raycast(ray, out RaycastHit hitAll, float.MaxValue))
        {
            Debug.Log($"Hit something: {hitAll.transform.name} " +
                      $"on layer: {LayerMask.LayerToName(hitAll.transform.gameObject.layer)}");
        }
        else
        {
            Debug.LogWarning("Hit NOTHING - check your floor has a collider!");
        }

        // Test 2: Does raycast hit the Unit layer specifically
        LayerMask unitMask = LayerMask.GetMask("Unit");
        if (Physics.Raycast(ray, out RaycastHit hitUnit, float.MaxValue, unitMask))
        {
            Debug.Log($"Hit UNIT layer object: {hitUnit.transform.name}");
        }
        else
        {
            Debug.LogWarning("Hit nothing on Unit layer - " +
                            "player needs collider AND Unit layer!");
        }

        // Test 3: Is LevelGrid initialized
        if (LevelGrid.Instance != null)
        {
            Debug.Log($"LevelGrid initialized: {LevelGrid.Instance.IsInitialized()}");

            Vector3 mousePos = MouseWorld.GetPosition();
            Debug.Log($"Mouse world position: {mousePos}");

            GridPosition gridPos = LevelGrid.Instance.GetGridPosition(mousePos);
            Debug.Log($"Grid position clicked: {gridPos}");

            bool isValid = LevelGrid.Instance.isValidGridPosition(gridPos);
            Debug.Log($"Is valid grid position: {isValid}");
        }
        else
        {
            Debug.LogError("LevelGrid.Instance is NULL!");
        }

        // Test 4: Is there a selected unit
        if (UnitActionSystem.Instance != null)
        {
            Unit selected = UnitActionSystem.Instance.GetSelectedUnit();
            Debug.Log($"Selected unit: {(selected != null ? selected.name : "NONE")}");

            if (selected != null)
            {
                Debug.Log($"Unit initialized: {selected.IsInitialized()}");
                Debug.Log($"Unit grid position: {selected.GetGridPosition()}");
                Debug.Log($"Unit has room grid: {selected.GetCurrentRoomGrid() != null}");

                MoveAction move = selected.GetMoveAction();
                if (move != null)
                {
                    var validPositions = move.GetValidActionGridPositionList();
                    Debug.Log($"Valid move positions: {validPositions.Count}");
                }
            }
        }
        else
        {
            Debug.LogError("UnitActionSystem.Instance is NULL!");
        }
    }
}
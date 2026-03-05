// DebugMoveAction.cs
using System.Collections.Generic;
using UnityEngine;

public class DebugMoveAction : MonoBehaviour
{
    private void Update()
    {
        // Only debug when spacebar pressed
        if (!Input.GetKeyDown(KeyCode.Space)) return;

        Debug.Log("\n=== MOVEACTION DEBUG ===");

        // Get player
        Unit player = FindFirstObjectByType<Unit>();
        if (player == null)
        {
            Debug.LogError("[DEBUG] NO PLAYER!");
            return;
        }
        Debug.Log($"[DEBUG] Player: {player.name}");

        // Get player's room
        RoomGrid playerRoom = player.GetCurrentRoomGrid();
        if (playerRoom == null)
        {
            Debug.LogError("[DEBUG] Player has NO ROOM!");
            return;
        }
        Debug.Log($"[DEBUG] Player room: {playerRoom.gameObject.name}");

        // Get TilemapRoomGrid
        TilemapRoomGrid tilemapGrid = playerRoom.GetTilemapRoomGrid();
        if (tilemapGrid == null)
        {
            Debug.LogError("[DEBUG] Room has NO TilemapRoomGrid!");
            return;
        }
        Debug.Log($"[DEBUG] TilemapRoomGrid: ✓");

        // Get MoveAction
        MoveAction moveAction = player.GetMoveAction();
        if (moveAction == null)
        {
            Debug.LogError("[DEBUG] Player has NO MoveAction!");
            return;
        }
        Debug.Log($"[DEBUG] MoveAction: ✓");

        // Get valid positions
        List<GridPosition> validPositions = moveAction.GetValidActionGridPositionList();
        Debug.Log($"[DEBUG] Valid positions: {validPositions.Count}");
        
        if (validPositions.Count == 0)
        {
            Debug.LogError("[DEBUG] ❌ NO VALID POSITIONS!");
            
            // Debug why
            GridPosition playerPos = player.GetGridPosition();
            Debug.Log($"[DEBUG] Player grid position: {playerPos}");
            
            int moveDistance = playerRoom.GetWidth(); // fallback
            Debug.Log($"[DEBUG] Move distance: {moveDistance}");
            
            // Check one adjacent tile
            GridPosition testPos = new GridPosition(playerPos.x + 1, playerPos.z);
            bool isValid = playerRoom.IsValidGridPosition(testPos);
            bool hasUnit = playerRoom.HasAnyUnitOnGridPosition(testPos);
            bool isWall = tilemapGrid.IsWallAtPosition(testPos);
            
            Debug.Log($"[DEBUG] Test tile ({testPos.x}, {testPos.z}):");
            Debug.Log($"  - Valid: {isValid}");
            Debug.Log($"  - Has unit: {hasUnit}");
            Debug.Log($"  - Is wall: {isWall}");
            Debug.Log($"  - Walkable: {isValid && !hasUnit && !isWall}");
        }
        else
        {
            Debug.Log($"[DEBUG] ✅ Found {validPositions.Count} valid positions");
            for (int i = 0; i < Mathf.Min(5, validPositions.Count); i++)
            {
                Debug.Log($"  - {validPositions[i]}");
            }
        }

        // Check GridSystemVisual
        GridSystemVisual gridVisual = FindFirstObjectByType<GridSystemVisual>();
        if (gridVisual == null)
        {
            Debug.LogError("[DEBUG] NO GridSystemVisual in scene!");
        }
        else
        {
            Debug.Log($"[DEBUG] GridSystemVisual: ✓");
        }

        // Check UnitActionSystem
        if (UnitActionSystem.Instance == null)
        {
            Debug.LogError("[DEBUG] NO UnitActionSystem!");
        }
        else
        {
            Debug.Log($"[DEBUG] UnitActionSystem: ✓");
            var selected = UnitActionSystem.Instance.GetSelectedAction();
            Debug.Log($"[DEBUG] Selected action: {selected?.GetActionName()}");
        }

        Debug.Log("=== END DEBUG ===\n");
    }
}
// DebugRoomCoords.cs — attach to any GameObject, remove after fixing
using UnityEngine;
using UnityEngine.Tilemaps;

public class DebugRoomCoords : MonoBehaviour
{
    private void Start()
    {
        // Wait for level to generate
        LevelGenerator.OnLevelReady += OnLevelReady;
    }

    private void OnLevelReady()
    {
        LevelGenerator levelGen = FindFirstObjectByType<LevelGenerator>();
        if (levelGen == null) return;

        foreach (var room in levelGen.GetAllRooms())
        {
            if (room.roomGrid == null) continue;

            TilemapRoomGrid trg = room.roomGrid.GetTilemapRoomGrid();
            if (trg == null) continue;

            Tilemap floor = trg.GetFloorTilemap();
            if (floor == null) continue;

            // Room root world position
            Debug.Log($"[DEBUG] Room: {room.roomInstance.name}");
            Debug.Log($"[DEBUG]   Room root world pos: {room.roomInstance.transform.position}");
            Debug.Log($"[DEBUG]   Floor tilemap world pos: {floor.transform.position}");
            Debug.Log($"[DEBUG]   Floor cell bounds: {floor.cellBounds}");

            // What does cell (0,0,0) return as world position?
            Vector3 cell00 = floor.GetCellCenterWorld(new Vector3Int(0, 0, 0));
            Debug.Log($"[DEBUG]   Cell(0,0,0) world: {cell00}");

            // What does the center cell return?
            BoundsInt b = floor.cellBounds;
            Vector3Int centerCell = new Vector3Int(
                b.xMin + b.size.x / 2,
                b.yMin + b.size.y / 2, 0);
            Vector3 centerWorld = floor.GetCellCenterWorld(centerCell);
            Debug.Log($"[DEBUG]   Center cell {centerCell} world: {centerWorld}");

            // What does GetWorldPosition return for center?
            GridPosition centerGridPos = new GridPosition(centerCell.x, centerCell.y);
            Vector3 ourWorldPos = room.roomGrid.GetWorldPosition(centerGridPos);
            Debug.Log($"[DEBUG]   Our GetWorldPosition({centerGridPos}): {ourWorldPos}");
        }
    }
}
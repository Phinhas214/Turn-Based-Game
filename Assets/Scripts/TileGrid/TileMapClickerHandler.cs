using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Detects clicks on tilemap tiles and converts them to grid positions.
/// Works with MoveAction and CombatAction.
/// </summary>
public class TilemapClickHandler : MonoBehaviour
{
    [SerializeField] private Tilemap floorTilemap;
    [SerializeField] private Tilemap wallsTilemap;

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            HandleTileClick();
        }
    }

    private void HandleTileClick()
    {
        Vector3 worldPos = MouseWorld.GetPosition();
        Tilemap tilemap = GetTilemapAtPosition(worldPos);

        if (tilemap == null) return;

        // Convert world position to grid position
        Vector3Int cellPos = tilemap.WorldToCell(worldPos);
        GridPosition gridPos = new GridPosition(cellPos.x, cellPos.y);

        Debug.Log($"[TilemapClickHandler] Clicked tile at {gridPos}");

        // UnitActionSystem handles the action execution
        // (it already does this via MouseWorld.GetPosition)
    }

    private Tilemap GetTilemapAtPosition(Vector3 worldPos)
    {
        // Check floor tilemap first (it's on top)
        if (floorTilemap != null)
        {
            Vector3Int cellPos = floorTilemap.WorldToCell(worldPos);
            if (floorTilemap.HasTile(cellPos))
                return floorTilemap;
        }

        // Check walls tilemap
        if (wallsTilemap != null)
        {
            Vector3Int cellPos = wallsTilemap.WorldToCell(worldPos);
            if (wallsTilemap.HasTile(cellPos))
                return wallsTilemap;
        }

        return null;
    }
}
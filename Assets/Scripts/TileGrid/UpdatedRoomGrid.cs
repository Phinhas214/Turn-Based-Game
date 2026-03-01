using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Adapter layer between the old GridSystem interface and the new Tilemap-based system.
/// Most logic is now in TilemapRoomGrid; this class provides backward compatibility
/// and room-level grid management.
/// </summary>
public class RoomGrid : MonoBehaviour
{
    private TilemapRoomGrid tilemapGrid;
    private bool isInitialized = false;

    //  Initialization (kept for compatibility, but delegated to TilemapRoomGrid)

    public void Initialize(int width, int height, float cellSize, 
                          Vector3 worldPosition, Vector3 gridOffset, 
                          Transform debugPrefab = null)
    {
        
        tilemapGrid = GetComponent<TilemapRoomGrid>();
        if (tilemapGrid == null)
        {
            Debug.LogWarning($"[RoomGrid] No TilemapRoomGrid on {gameObject.name} — " +
                           "make sure RoomTilemapSetup.Initialize() was called");
            return;
        }

        isInitialized = true;
        Debug.Log($"[RoomGrid] Initialized {gameObject.name}");
    }

    //  Coordinate Conversion

    public Vector3 GetWorldPosition(GridPosition gridPosition)
    {
        if (tilemapGrid == null) return Vector3.zero;
        return tilemapGrid.GetWorldPosition(gridPosition);
    }

    public GridPosition GetGridPosition(Vector3 worldPosition)
    {
        if (tilemapGrid == null) return new GridPosition(0, 0);
        return tilemapGrid.GetGridPosition(worldPosition);
    }

    public bool IsPositionInRoom(Vector3 worldPosition)
    {
        if (tilemapGrid == null) return false;
        return tilemapGrid.IsPositionInRoom(worldPosition);
    }

    //  Unit Management (delegated to TilemapRoomGrid)

    public void AddUnitAtGridPosition(GridPosition gridPosition, Unit unit)
    {
        tilemapGrid?.AddUnitAtGridPosition(gridPosition, unit);
    }

    public void RemoveUnitAtGridPosition(GridPosition gridPosition, Unit unit)
    {
        tilemapGrid?.RemoveUnitAtGridPosition(gridPosition, unit);
    }

    public bool HasAnyUnitOnGridPosition(GridPosition gridPosition)
    {
        if (tilemapGrid == null) return false;
        return tilemapGrid.HasAnyUnitOnGridPosition(gridPosition);
    }

    //  Enemy Management

    public void AddEnemyAtGridPosition(GridPosition gridPosition, EnemyUnit enemy)
    {
        tilemapGrid?.AddEnemyAtGridPosition(gridPosition, enemy);
    }

    public void RemoveEnemyAtGridPosition(GridPosition gridPosition, EnemyUnit enemy)
    {
        tilemapGrid?.RemoveEnemyAtGridPosition(gridPosition, enemy);
    }

    public bool HasAnyEnemyOnGridPosition(GridPosition gridPosition)
    {
        if (tilemapGrid == null) return false;
        return tilemapGrid.HasAnyEnemyOnGridPosition(gridPosition);
    }

    //  Validation

    public bool IsValidGridPosition(GridPosition gridPosition)
    {
        if (tilemapGrid == null) return false;
        return tilemapGrid.IsValidGridPosition(gridPosition);
    }

    //  Grid Properties

    public int GetWidth()
    {
        if (tilemapGrid == null) return 0;
        return tilemapGrid.GetWidth();
    }

    public int GetHeight()
    {
        if (tilemapGrid == null) return 0;
        return tilemapGrid.GetHeight();
    }

    public Vector3 GetGridOffset()
    {
        return Vector3.zero; // Tilemap handles offset internally
    }

    //  Tilemap Access

    public TilemapRoomGrid GetTilemapRoomGrid()
    {
        return tilemapGrid;
    }

    public Tilemap GetWallsTilemap()
    {
        return tilemapGrid?.GetWallsTilemap();
    }

    public Tilemap GetFloorTilemap()
    {
        return tilemapGrid?.GetFloorTilemap();
    }


    public bool IsInitialized()
    {
        return isInitialized && tilemapGrid != null;
    }
}
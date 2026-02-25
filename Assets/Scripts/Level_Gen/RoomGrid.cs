using UnityEngine;

public class RoomGrid : MonoBehaviour
{
    private GridSystem gridSystem;
    private Vector3 roomWorldPosition;
    private Vector3 gridOffset;
    private int width;
    private int height;
    private float cellSize;

    public void Initialize(int width, int height, float cellSize, Vector3 worldPosition, Vector3 gridOffset, Transform debugPrefab = null)
    {
        this.width             = width;
        this.height            = height;
        this.cellSize          = cellSize;
        this.roomWorldPosition = worldPosition;
        this.gridOffset        = gridOffset;

        gridSystem = new GridSystem(width, height, cellSize);

        if (debugPrefab != null)
            CreateDebugObjects(debugPrefab);
    }

    private void CreateDebugObjects(Transform debugPrefab)
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                GridPosition gridPosition = new GridPosition(x, z);
                Vector3 worldPos = GetWorldPosition(gridPosition);

                Transform debugTransform = Instantiate(debugPrefab, worldPos, Quaternion.identity, transform);
                debugTransform.name = $"DebugGrid_{x}_{z}";

                GridDebugObject gridDebugObject = debugTransform.GetComponent<GridDebugObject>();
                if (gridDebugObject != null)
                    gridDebugObject.SetGridObject(gridSystem.GetGridObject(gridPosition));
            }
        }
    }

    public GridSystem GetGridSystem() => gridSystem;

    /// Returns the world-space center of the tile at gridPosition,
    /// accounting for the room's world position and grid offset.
    public Vector3 GetWorldPosition(GridPosition gridPosition)
    {
        Vector3 localPos = gridSystem.GetWorldPosition(gridPosition);
        return roomWorldPosition
             + new Vector3(gridOffset.x, gridOffset.y, gridOffset.z)
             + new Vector3(localPos.x, 0f, localPos.z);
    }

    /// Converts a world position to the nearest grid position within this room.
    /// Strips Y before conversion so floor-collider height never causes mismatches.
    public GridPosition GetGridPosition(Vector3 worldPosition)
    {
        Vector3 localPosition = worldPosition - roomWorldPosition - gridOffset;
        localPosition.y = 0f;
        return gridSystem.GetGridPosition(localPosition);
    }

    /// Returns true if worldPosition lies within this room's grid bounds.
    public bool IsPositionInRoom(Vector3 worldPosition)
    {
        Vector3 localPos = worldPosition - roomWorldPosition - gridOffset;
        localPos.y = 0f;
        GridPosition gridPos = gridSystem.GetGridPosition(localPos);
        return gridSystem.IsValidGridPosition(gridPos);
    }

    // ── Existing player unit methods (unchanged) ──────────────────────────

    public void AddUnitAtGridPosition(GridPosition gridPosition, Unit unit)
    {
        if (!gridSystem.IsValidGridPosition(gridPosition)) return;
        gridSystem.GetGridObject(gridPosition).AddUnit(unit);
    }

    public void RemoveUnitAtGridPosition(GridPosition gridPosition, Unit unit)
    {
        if (!gridSystem.IsValidGridPosition(gridPosition)) return;
        gridSystem.GetGridObject(gridPosition).RemoveUnit(unit);
    }

    /// Returns true if a player Unit OR an EnemyUnit is on this tile.
    /// This means enemies block player movement and pathfinding treats both as obstacles.
    public bool HasAnyUnitOnGridPosition(GridPosition gridPosition)
    {
        if (!gridSystem.IsValidGridPosition(gridPosition)) return false;
        GridObject obj = gridSystem.GetGridObject(gridPosition);
        return obj.HasAnyUnit() || obj.HasAnyEnemy(); // UPDATED — was just HasAnyUnit()
    }

    public bool IsValidGridPosition(GridPosition gridPosition)
    {
        return gridSystem.IsValidGridPosition(gridPosition);
    }

    // ── New enemy methods ─────────────────────────────────────────────────

    public void AddEnemyAtGridPosition(GridPosition gridPosition, EnemyUnit enemy)
    {
        if (!gridSystem.IsValidGridPosition(gridPosition)) return;
        gridSystem.GetGridObject(gridPosition).AddEnemy(enemy);
    }

    public void RemoveEnemyAtGridPosition(GridPosition gridPosition, EnemyUnit enemy)
    {
        if (!gridSystem.IsValidGridPosition(gridPosition)) return;
        gridSystem.GetGridObject(gridPosition).RemoveEnemy(enemy);
    }

    public bool HasAnyEnemyOnGridPosition(GridPosition gridPosition)
    {
        if (!gridSystem.IsValidGridPosition(gridPosition)) return false;
        return gridSystem.GetGridObject(gridPosition).HasAnyEnemy();
    }

    public EnemyUnit GetEnemyAtGridPosition(GridPosition gridPosition)
    {
        if (!gridSystem.IsValidGridPosition(gridPosition)) return null;
        return gridSystem.GetGridObject(gridPosition).GetEnemy();
    }

    public int     GetWidth()      => width;
    public int     GetHeight()     => height;
    public Vector3 GetGridOffset() => gridOffset;
}
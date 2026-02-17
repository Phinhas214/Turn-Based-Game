using UnityEngine;

public class RoomGrid : MonoBehaviour
{
    private GridSystem gridSystem;
    private Vector3 roomWorldPosition;
    private int width;
    private int height;
    private float cellSize;

    // Add to RoomGrid.cs in the Initialize method, after gridSystem is created:
    public void Initialize(int width, int height, float cellSize, Vector3 worldPosition, Transform debugPrefab = null)
    {
        this.width = width;
        this.height = height;
        this.cellSize = cellSize;
        this.roomWorldPosition = worldPosition;
        
        gridSystem = new GridSystem(width, height, cellSize);
        
        Debug.Log($"✓✓✓ RoomGrid initialized: {width}x{height}, cellSize: {cellSize}, at: {worldPosition}");
        
        if (debugPrefab != null)
        {
            CreateDebugObjects(debugPrefab);
            Debug.Log($"✓ Created debug objects for grid");
        }
        else
        {
            Debug.LogWarning("⚠ No debug prefab provided - grid is invisible but functional");
        }
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
                GridDebugObject gridDebugObject = debugTransform.GetComponent<GridDebugObject>();
                gridDebugObject.SetGridObject(gridSystem.GetGridObject(gridPosition));
            }
        }
    }

    public GridSystem GetGridSystem()
    {
        return gridSystem;
    }

    public Vector3 GetWorldPosition(GridPosition gridPosition)
    {
        return roomWorldPosition + gridSystem.GetWorldPosition(gridPosition);
    }

    public GridPosition GetGridPosition(Vector3 worldPosition)
    {
        Vector3 localPosition = worldPosition - roomWorldPosition;
        return gridSystem.GetGridPosition(localPosition);
    }

    public bool IsPositionInRoom(Vector3 worldPosition)
    {
        Vector3 localPos = worldPosition - roomWorldPosition;
        GridPosition gridPos = gridSystem.GetGridPosition(localPos);
        return gridSystem.isValidGridPosition(gridPos);
    }

    public void AddUnitAtGridPosition(GridPosition gridPosition, Unit unit)
    {
        GridObject gridObject = gridSystem.GetGridObject(gridPosition);
        gridObject.AddUnit(unit);
    }

    public void RemoveUnitAtGridPosition(GridPosition gridPosition, Unit unit)
    {
        GridObject gridObject = gridSystem.GetGridObject(gridPosition);
        gridObject.RemoveUnit(unit);
    }

    public bool HasAnyUnitOnGridPosition(GridPosition gridPosition)
    {
        GridObject gridObject = gridSystem.GetGridObject(gridPosition);
        return gridObject.HasAnyUnit();
    }

    public bool IsValidGridPosition(GridPosition gridPosition)
    {
        return gridSystem.isValidGridPosition(gridPosition);
    }

    public int GetWidth()
    {
        return width;
    }

    public int GetHeight()
    {
        return height;
    }
}
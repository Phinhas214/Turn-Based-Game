using UnityEngine;

public class RoomGrid : MonoBehaviour
{
    private GridSystem gridSystem;
    private Vector3 roomWorldPosition;
    private int width;
    private int height;
    private float cellSize;
    private float heightOffset;

    public void Initialize(int width, int height, float cellSize, Vector3 worldPosition, float heightOffset = 0.1f, Transform debugPrefab = null)
    {
        this.width = width;
        this.height = height;
        this.cellSize = cellSize;
        this.heightOffset = heightOffset;
        this.roomWorldPosition = worldPosition;

        gridSystem = new GridSystem(width, height, cellSize);

        if (debugPrefab != null)
        {
            CreateDebugObjects(debugPrefab);
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
                debugTransform.name = $"DebugGrid_{x}_{z}";

                GridDebugObject gridDebugObject = debugTransform.GetComponent<GridDebugObject>();
                if (gridDebugObject != null)
                {
                    gridDebugObject.SetGridObject(gridSystem.GetGridObject(gridPosition));
                }
            }
        }
    }

    public GridSystem GetGridSystem() => gridSystem;

    // Height offset is applied here so grid sits on the floor
    public Vector3 GetWorldPosition(GridPosition gridPosition)
    {
        Vector3 localPos = gridSystem.GetWorldPosition(gridPosition);
        return roomWorldPosition + new Vector3(localPos.x, heightOffset, localPos.z);
    }

    public GridPosition GetGridPosition(Vector3 worldPosition)
    {
        // Strip out the height offset before calculating grid position
        Vector3 localPosition = worldPosition - roomWorldPosition;
        localPosition.y = 0;
        return gridSystem.GetGridPosition(localPosition);
    }

    public bool IsPositionInRoom(Vector3 worldPosition)
    {
        Vector3 localPos = worldPosition - roomWorldPosition;
        localPos.y = 0;
        GridPosition gridPos = gridSystem.GetGridPosition(localPos);
        return gridSystem.IsValidGridPosition(gridPos);
    }

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

    public bool HasAnyUnitOnGridPosition(GridPosition gridPosition)
    {
        if (!gridSystem.IsValidGridPosition(gridPosition)) return false;
        return gridSystem.GetGridObject(gridPosition).HasAnyUnit();
    }

    public bool IsValidGridPosition(GridPosition gridPosition)
    {
        return gridSystem.IsValidGridPosition(gridPosition);
    }

    public int GetWidth() => width;
    public int GetHeight() => height;
    public float GetHeightOffset() => heightOffset;
}
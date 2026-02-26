using UnityEngine;

// Core grid math. Tiles are CENTER-anchored — GetWorldPosition returns the
// center of each tile, and GetGridPosition snaps to the nearest tile center.
// This ensures the mouse highlight always lands on the tile you're hovering.
public class GridSystem
{
    private int width;
    private int height;
    private float cellSize;
    private GridObject[,] gridObjectArray;

    public GridSystem(int width, int height, float cellSize)
    {
        this.width    = width;
        this.height   = height;
        this.cellSize = cellSize;

        gridObjectArray = new GridObject[width, height];

        for (int x = 0; x < width; x++)
            for (int z = 0; z < height; z++)
            {
                GridPosition gp = new GridPosition(x, z);
                gridObjectArray[x, z] = new GridObject(this, gp);
            }
    }

    public int GetWidth()  => width;
    public int GetHeight() => height;

    // Returns the WORLD-SPACE CENTER of the tile at gridPosition.
    // Tiles are cellSize apart, starting at (0,0).
    public Vector3 GetWorldPosition(GridPosition gridPosition)
    {
        return new Vector3(gridPosition.x, 0, gridPosition.z) * cellSize;
    }


    // Converts a world position to the nearest grid position.
    // Rounds to the nearest tile center so hovering anywhere on a tile
    // returns that tile's grid position.
    public GridPosition GetGridPosition(Vector3 worldPosition)
    {
        return new GridPosition(
            Mathf.RoundToInt(worldPosition.x / cellSize),
            Mathf.RoundToInt(worldPosition.z / cellSize)
        );
    }

    public GridObject GetGridObject(GridPosition gridPosition)
    {
        return gridObjectArray[gridPosition.x, gridPosition.z];
    }

    public bool IsValidGridPosition(GridPosition gridPosition)
    {
        return gridPosition.x >= 0 &&
               gridPosition.z >= 0 &&
               gridPosition.x < width &&
               gridPosition.z < height;
    }

    // Kept for compatibility
    public bool isValidGridPosition(GridPosition gridPosition) => IsValidGridPosition(gridPosition);
}
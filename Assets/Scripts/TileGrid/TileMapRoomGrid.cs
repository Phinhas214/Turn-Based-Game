using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Wraps Unity's Tilemap system to provide the same interface as the old GridSystem.
/// Each room has one TilemapRoomGrid that manages unit/enemy placement.
/// 
/// Features:
/// • Converts between GridPosition and tilemap coordinates
/// • Stores units/enemies in TilemapCell dictionaries
/// • Checks wall tiles for pathfinding
/// • Provides world ↔ grid position conversions
/// </summary>
public class TilemapRoomGrid : MonoBehaviour
{
    [Header("Tilemaps")]
    [Tooltip("Tilemap containing wall tiles. Used for collision and bounds.")]
    [SerializeField] private Tilemap wallsTilemap;

    [Tooltip("Optional tilemap for floor/walkable area visualization.")]
    [SerializeField] private Tilemap floorTilemap;

    // ─────────────────────────────────────────────────────────────────────
    //  Runtime state
    // ─────────────────────────────────────────────────────────────────────
    private Tilemap primaryTilemap; // Used for bounds and coordinate system
    private Dictionary<Vector3Int, TilemapCell> cells = new Dictionary<Vector3Int, TilemapCell>();
    private bool isInitialized = false;

    // ─────────────────────────────────────────────────────────────────────
    //  Initialization
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Initialize with tilemap references. Call from RoomTilemapSetup.
    /// </summary>
    public void Initialize(Tilemap walls, Tilemap floor = null)
    {
        wallsTilemap = walls;
        floorTilemap = floor;
        primaryTilemap = walls != null ? walls : floor;

        if (primaryTilemap == null)
        {
            Debug.LogError($"[TilemapRoomGrid] No tilemaps assigned to {gameObject.name}");
            return;
        }

        // Pre-populate cell dictionary for fast lookups
        BoundsInt bounds = primaryTilemap.cellBounds;
        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                Vector3Int pos = new Vector3Int(x, y, 0);
                cells[pos] = new TilemapCell();
            }
        }

        isInitialized = true;
        Debug.Log($"[TilemapRoomGrid] Initialized {gameObject.name} with bounds {bounds}");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Unit Management
    // ─────────────────────────────────────────────────────────────────────

    public void AddUnitAtGridPosition(GridPosition gridPos, Unit unit)
    {
        if (!isInitialized) return;
        Vector3Int pos = ToVector3Int(gridPos);
        if (!cells.ContainsKey(pos))
            cells[pos] = new TilemapCell();
        cells[pos].AddUnit(unit);
    }

    public void RemoveUnitAtGridPosition(GridPosition gridPos, Unit unit)
    {
        if (!isInitialized) return;
        Vector3Int pos = ToVector3Int(gridPos);
        if (cells.TryGetValue(pos, out var cell))
            cell.RemoveUnit(unit);
    }

    public List<Unit> GetUnitsAtGridPosition(GridPosition gridPos)
    {
        Vector3Int pos = ToVector3Int(gridPos);
        if (cells.TryGetValue(pos, out var cell))
            return cell.GetUnitList();
        return new List<Unit>();
    }

    public bool HasAnyUnitOnGridPosition(GridPosition gridPosition)
    {
        if (!isInitialized) return false;
        Vector3Int pos = ToVector3Int(gridPosition);
        if (cells.TryGetValue(pos, out var cell))
            return cell.HasAnyUnit() || cell.HasAnyEnemy();
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Enemy Management
    // ─────────────────────────────────────────────────────────────────────

    public void AddEnemyAtGridPosition(GridPosition gridPos, EnemyUnit enemy)
    {
        if (!isInitialized) return;
        Vector3Int pos = ToVector3Int(gridPos);
        if (!cells.ContainsKey(pos))
            cells[pos] = new TilemapCell();
        cells[pos].AddEnemy(enemy);
    }

    public void RemoveEnemyAtGridPosition(GridPosition gridPos, EnemyUnit enemy)
    {
        if (!isInitialized) return;
        Vector3Int pos = ToVector3Int(gridPos);
        if (cells.TryGetValue(pos, out var cell))
            cell.RemoveEnemy(enemy);
    }

    public List<EnemyUnit> GetEnemiesAtGridPosition(GridPosition gridPos)
    {
        Vector3Int pos = ToVector3Int(gridPos);
        if (cells.TryGetValue(pos, out var cell))
            return cell.GetEnemyList();
        return new List<EnemyUnit>();
    }

    public bool HasAnyEnemyOnGridPosition(GridPosition gridPos)
    {
        if (!isInitialized) return false;
        Vector3Int pos = ToVector3Int(gridPos);
        return cells.TryGetValue(pos, out var cell) && cell.HasAnyEnemy();
    }

    /// <summary>
    /// Returns true if there is any unit or enemy on this grid position.
    /// Used for pathfinding and movement blocking.
    /// </summary>
    // public bool HasAnyUnitOnGridPosition(GridPosition gridPos)
    // {
    //     if (!isInitialized) return false;
    //     Vector3Int pos = ToVector3Int(gridPos);
    //     return cells.TryGetValue(pos, out var cell) && cell.HasAnyUnitOrEnemy();
    // }

    // ─────────────────────────────────────────────────────────────────────
    //  Coordinate Conversion
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts world position to grid position using tilemap's coordinate system.
    /// </summary>
    public GridPosition GetGridPosition(Vector3 worldPos)
    {
        if (primaryTilemap == null) return new GridPosition(0, 0);
        Vector3Int cellPos = primaryTilemap.WorldToCell(worldPos);
        return new GridPosition(cellPos.x, cellPos.y);
    }

    /// <summary>
    /// Converts grid position to world position (center of the cell).
    /// </summary>
    public Vector3 GetWorldPosition(GridPosition gridPos)
    {
        if (primaryTilemap == null) return Vector3.zero;
        Vector3Int cellPos = ToVector3Int(gridPos);
        return primaryTilemap.GetCellCenterWorld(cellPos);
    }

    /// <summary>
    /// Returns true if the grid position is within tilemap bounds.
    /// </summary>
    public bool IsValidGridPosition(GridPosition gridPos)
    {
        if (!isInitialized || primaryTilemap == null) return false;
        Vector3Int pos = ToVector3Int(gridPos);
        return primaryTilemap.cellBounds.Contains(pos);
    }

    /// <summary>
    /// Returns true if a world position falls within this room's tilemap bounds.
    /// </summary>
    public bool IsPositionInRoom(Vector3 worldPos)
    {
        return IsValidGridPosition(GetGridPosition(worldPos));
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Wall/Collision Checking
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if there is a wall tile at this position.
    /// Used by pathfinder to avoid walls.
    /// </summary>
    public bool IsWallAtPosition(GridPosition gridPos)
    {
        if (wallsTilemap == null) return false;
        Vector3Int pos = ToVector3Int(gridPos);
        return wallsTilemap.HasTile(pos);
    }

    /// <summary>
    /// Returns true if a position is walkable (no wall, no unit/enemy).
    /// Used during pathfinding.
    /// </summary>
    public bool IsWalkable(GridPosition gridPos)
    {
        if (!IsValidGridPosition(gridPos)) return false;
        if (IsWallAtPosition(gridPos)) return false;
        if (HasAnyUnitOnGridPosition(gridPos)) return false;
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Tilemap Access
    // ─────────────────────────────────────────────────────────────────────

    public Tilemap GetWallsTilemap()      => wallsTilemap;
    public Tilemap GetFloorTilemap()      => floorTilemap;
    public Tilemap GetPrimaryTilemap()    => primaryTilemap;
    public BoundsInt GetCellBounds()      => primaryTilemap?.cellBounds ?? default;
    public int GetWidth()                 => primaryTilemap?.cellBounds.size.x ?? 0;
    public int GetHeight()                => primaryTilemap?.cellBounds.size.y ?? 0;
    public bool IsInitialized()           => isInitialized;

    // ─────────────────────────────────────────────────────────────────────
    //  Helper Conversions
    // ─────────────────────────────────────────────────────────────────────

    private Vector3Int ToVector3Int(GridPosition gp) 
        => new Vector3Int(gp.x, gp.z, 0);

    private GridPosition FromVector3Int(Vector3Int v3) 
        => new GridPosition(v3.x, v3.y);

    // ─────────────────────────────────────────────────────────────────────
    //  Debug/Gizmos
    // ─────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (primaryTilemap == null) return;

        BoundsInt bounds = primaryTilemap.cellBounds;
        Gizmos.color = Color.cyan;

        // Draw grid lines
        for (int x = bounds.xMin; x <= bounds.xMax; x++)
        {
            Vector3 start = primaryTilemap.GetCellCenterWorld(new Vector3Int(x, bounds.yMin, 0));
            Vector3 end = primaryTilemap.GetCellCenterWorld(new Vector3Int(x, bounds.yMax, 0));
            Gizmos.DrawLine(start, end);
        }

        for (int y = bounds.yMin; y <= bounds.yMax; y++)
        {
            Vector3 start = primaryTilemap.GetCellCenterWorld(new Vector3Int(bounds.xMin, y, 0));
            Vector3 end = primaryTilemap.GetCellCenterWorld(new Vector3Int(bounds.xMax, y, 0));
            Gizmos.DrawLine(start, end);
        }
    }
#endif
}
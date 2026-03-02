using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TilemapRoomGrid : MonoBehaviour
{
    [Header("Tilemaps")]
    [SerializeField] private Tilemap wallsTilemap;
    [SerializeField] private Tilemap floorTilemap;

    private Tilemap primaryTilemap;
    private Dictionary<Vector3Int, TilemapCell> cells = new Dictionary<Vector3Int, TilemapCell>();
    private bool isInitialized = false;

    // ── Init ───────────────────────────────────────────────────────────────

    public void Initialize(Tilemap walls, Tilemap floor = null)
    {
        wallsTilemap   = walls;
        floorTilemap   = floor;
        // Use floor as primary — it defines the walkable area
        primaryTilemap = floor != null ? floor : walls;

        if (primaryTilemap == null)
        {
            Debug.LogError($"[TilemapRoomGrid] No tilemaps on {gameObject.name}");
            return;
        }

        BoundsInt bounds = primaryTilemap.cellBounds;
        for (int x = bounds.xMin; x < bounds.xMax; x++)
            for (int y = bounds.yMin; y < bounds.yMax; y++)
                cells[new Vector3Int(x, y, 0)] = new TilemapCell();

        isInitialized = true;
        Debug.Log($"[TilemapRoomGrid] Initialized {gameObject.name} " +
                  $"bounds {bounds} primary:{primaryTilemap.gameObject.name}");
    }

    // ── CRITICAL: Coordinate conversion for 3D X/Z game ──────────────────
    //
    // Unity Tilemap internally uses X/Y.
    // Our game uses X/Z (Y is up, we look down).
    // Tilemap cell (x, y, 0) = GridPosition(x, z) where z = tilemap y.
    //
    // GetCellCenterWorld returns a Vector3 where:
    //   result.x = world X  ✓
    //   result.y = world Y (tilemap plane, usually 0 or small offset)
    //   result.z = world Z (this is 0 for 2D tilemaps!)
    //
    // For a 3D X/Z game the tilemap lies flat. Cell Y maps to world Z.
    // We must use GetCellCenterLocal then transform to world ourselves.
public Vector3 GetWorldPosition(GridPosition gridPos)
{
    if (primaryTilemap == null) return Vector3.zero;

    Vector3Int cell = new Vector3Int(gridPos.x, gridPos.z, 0);
    
    // GetCellCenterWorld already accounts for the tilemap's world position
    Vector3 worldPos = primaryTilemap.GetCellCenterWorld(cell);
    
    // For 3D X/Z game: tilemap X = world X, tilemap Y = world Z
    // Use the room root Y so things land on the floor
    return new Vector3(worldPos.x, transform.position.y, worldPos.y);
}

public GridPosition GetGridPosition(Vector3 worldPos)
{
    if (primaryTilemap == null) return new GridPosition(0, 0);
    
    // Feed X and Z into the tilemap as X and Y since tilemap is flat
    Vector3 tilemapPos = new Vector3(worldPos.x, worldPos.z, 0);
    Vector3Int cell = primaryTilemap.WorldToCell(tilemapPos);
    return new GridPosition(cell.x, cell.y);
}

public bool IsPositionInRoom(Vector3 worldPos)
{
    if (primaryTilemap == null) return false;
    Vector3 tilemapPos = new Vector3(worldPos.x, worldPos.z, 0);
    Vector3Int cell = primaryTilemap.WorldToCell(tilemapPos);
    return primaryTilemap.HasTile(cell);
}

    // public GridPosition GetGridPosition(Vector3 worldPos)
    // {
    //     if (primaryTilemap == null) return new GridPosition(0, 0);
    //     // WorldToCell handles world position correctly
    //     Vector3Int cell = primaryTilemap.WorldToCell(worldPos);
    //     return new GridPosition(cell.x, cell.y);
    // }

    public bool IsValidGridPosition(GridPosition gridPos)
    {
        if (!isInitialized || primaryTilemap == null) return false;
        Vector3Int pos = new Vector3Int(gridPos.x, gridPos.z, 0);
        // Valid = within bounds AND has a floor tile painted there
        return primaryTilemap.cellBounds.Contains(pos) && primaryTilemap.HasTile(pos);
    }

    // public bool IsPositionInRoom(Vector3 worldPos)
    // {
    //     if (primaryTilemap == null) return false;
    //     Vector3Int cell = primaryTilemap.WorldToCell(worldPos);
    //     return primaryTilemap.HasTile(cell);
    // }

    // ── Wall checking ──────────────────────────────────────────────────────

    public bool IsWallAtPosition(GridPosition gridPos)
    {
        if (wallsTilemap == null) return false;
        return wallsTilemap.HasTile(new Vector3Int(gridPos.x, gridPos.z, 0));
    }

    public bool IsWalkable(GridPosition gridPos)
    {
        if (!IsValidGridPosition(gridPos)) return false;
        if (IsWallAtPosition(gridPos)) return false;
        if (HasAnyUnitOnGridPosition(gridPos)) return false;
        return true;
    }

    // ── Unit management ────────────────────────────────────────────────────

    public void AddUnitAtGridPosition(GridPosition gridPos, Unit unit)
    {
        if (!isInitialized) return;
        Vector3Int pos = new Vector3Int(gridPos.x, gridPos.z, 0);
        if (!cells.ContainsKey(pos)) cells[pos] = new TilemapCell();
        cells[pos].AddUnit(unit);
    }

    public void RemoveUnitAtGridPosition(GridPosition gridPos, Unit unit)
    {
        if (!isInitialized) return;
        Vector3Int pos = new Vector3Int(gridPos.x, gridPos.z, 0);
        if (cells.TryGetValue(pos, out var cell)) cell.RemoveUnit(unit);
    }

    public List<Unit> GetUnitsAtGridPosition(GridPosition gridPos)
    {
        Vector3Int pos = new Vector3Int(gridPos.x, gridPos.z, 0);
        return cells.TryGetValue(pos, out var cell) ? cell.GetUnitList() : new List<Unit>();
    }

    public bool HasAnyUnitOnGridPosition(GridPosition gridPos)
    {
        if (!isInitialized) return false;
        Vector3Int pos = new Vector3Int(gridPos.x, gridPos.z, 0);
        return cells.TryGetValue(pos, out var cell) && cell.HasAnyUnitOrEnemy();
    }

    // ── Enemy management ───────────────────────────────────────────────────

    public void AddEnemyAtGridPosition(GridPosition gridPos, EnemyUnit enemy)
    {
        if (!isInitialized) return;
        Vector3Int pos = new Vector3Int(gridPos.x, gridPos.z, 0);
        if (!cells.ContainsKey(pos)) cells[pos] = new TilemapCell();
        cells[pos].AddEnemy(enemy);
    }

    public void RemoveEnemyAtGridPosition(GridPosition gridPos, EnemyUnit enemy)
    {
        if (!isInitialized) return;
        Vector3Int pos = new Vector3Int(gridPos.x, gridPos.z, 0);
        if (cells.TryGetValue(pos, out var cell)) cell.RemoveEnemy(enemy);
    }

    public List<EnemyUnit> GetEnemiesAtGridPosition(GridPosition gridPos)
    {
        Vector3Int pos = new Vector3Int(gridPos.x, gridPos.z, 0);
        return cells.TryGetValue(pos, out var cell) ? cell.GetEnemyList() : new List<EnemyUnit>();
    }

    public bool HasAnyEnemyOnGridPosition(GridPosition gridPos)
    {
        if (!isInitialized) return false;
        Vector3Int pos = new Vector3Int(gridPos.x, gridPos.z, 0);
        return cells.TryGetValue(pos, out var cell) && cell.HasAnyEnemy();
    }

    // ── Tilemap access ─────────────────────────────────────────────────────

    public Tilemap GetWallsTilemap()   => wallsTilemap;
    public Tilemap GetFloorTilemap()   => floorTilemap;
    public Tilemap GetPrimaryTilemap() => primaryTilemap;
    public int GetWidth()              => primaryTilemap?.cellBounds.size.x ?? 0;
    public int GetHeight()             => primaryTilemap?.cellBounds.size.y ?? 0;
    public bool IsInitialized()        => isInitialized;
}
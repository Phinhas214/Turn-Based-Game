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

public Vector3 GetWorldPosition(GridPosition gridPos)
{
    if (primaryTilemap == null) return Vector3.zero;

    Vector3Int cell = new Vector3Int(gridPos.x, gridPos.z, 0);
    
    // GetCellCenterWorld returns (worldX, worldY, worldZ)
    // For a flat tilemap in a 3D X/Z game:
    //   result.x = correct world X  ✓
    //   result.y = tiny offset (tilemap plane height, e.g. -0.5)
    //   result.z = correct world Z  ✓
    // So we just use x and z directly, set y to the room's floor height
    Vector3 cellWorld = primaryTilemap.GetCellCenterWorld(cell);
    
    return new Vector3(cellWorld.x, transform.position.y, cellWorld.z);
}

public GridPosition GetGridPosition(Vector3 worldPos)
{
    if (primaryTilemap == null) return new GridPosition(0, 0);
    
    // WorldToCell works correctly in 3D — just pass the world position directly
    Vector3Int cell = primaryTilemap.WorldToCell(worldPos);
    
    // cell.x = grid X, cell.y = grid Z (tilemap Y = our game Z)
    return new GridPosition(cell.x, cell.y);
}

public bool IsValidGridPosition(GridPosition gridPos)
{
    if (!isInitialized || primaryTilemap == null) return false;
    Vector3Int pos = new Vector3Int(gridPos.x, gridPos.z, 0);
    return primaryTilemap.HasTile(pos);
}

public bool IsPositionInRoom(Vector3 worldPos)
{
    if (primaryTilemap == null) return false;
    Vector3Int cell = primaryTilemap.WorldToCell(worldPos);
    return primaryTilemap.HasTile(cell);
}

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

    public EnemyUnit GetEnemyAtGridPosition(GridPosition gridPos)
    {
        Debug.Log($"[GridQuery] Checking for enemy at grid position: ({gridPos.x}, {gridPos.z})");

        Vector3Int pos = new Vector3Int(gridPos.x, gridPos.z, 0);

        if (!cells.TryGetValue(pos, out var cell))
        {
            Debug.Log($"[GridQuery] ❌ No cell found at {pos}");
            return null;
        }

        Debug.Log($"[GridQuery] ✅ Cell found at {pos}");

        List<EnemyUnit> enemies = cell.GetEnemyList();

        if (enemies == null)
        {
            Debug.Log($"[GridQuery] ❌ Enemy list is NULL at {pos}");
            return null;
        }

        if (enemies.Count == 0)
        {
            Debug.Log($"[GridQuery] ❌ Enemy list empty at {pos}");
            return null;
        }

        Debug.Log($"[GridQuery] 🎯 Enemy FOUND at {pos}: {enemies[0].name}");

        return enemies[0];
    }

    // ── Tilemap access ─────────────────────────────────────────────────────

    public Tilemap GetWallsTilemap()   => wallsTilemap;
    public Tilemap GetFloorTilemap()   => floorTilemap;
    public Tilemap GetPrimaryTilemap() => primaryTilemap;
    public int GetWidth()              => primaryTilemap?.cellBounds.size.x ?? 0;
    public int GetHeight()             => primaryTilemap?.cellBounds.size.y ?? 0;
    public bool IsInitialized()        => isInitialized;
}
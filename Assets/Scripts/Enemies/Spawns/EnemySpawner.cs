using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Spawns enemies using EnemySpawnTable assets filtered by current level.
/// Falls back to legacy SpawnEntry list if no tables are assigned.
/// Validates world positions before spawning to prevent out-of-bounds enemies.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    // ── Legacy (kept so existing setups don't break) ───────────────────────

    [System.Serializable]
    public class SpawnEntry
    {
        public GameObject prefab;
        [Min(1)] public int count = 1;
        public LevelGenerator.RoomType roomType = LevelGenerator.RoomType.Normal;
        public bool preferEdgeTiles = false;
    }

    // ── Table-based ────────────────────────────────────────────────────────

    [Header("Table-Based Spawning (recommended)")]
    [Tooltip("Drag EnemySpawnTable assets here. Tables whose level range matches the current level will be used.")]
    [SerializeField] private List<EnemySpawnTable> spawnTables = new List<EnemySpawnTable>();

    [Header("Legacy Spawn Entries (used only if no tables assigned)")]
    [SerializeField] private List<SpawnEntry> spawnEntries = new List<SpawnEntry>();

    [Header("Settings")]
    [SerializeField, Min(1)] private int borderPadding = 2;
    [SerializeField] private bool spawnOnLevelReady = true;

    // ── Events ─────────────────────────────────────────────────────────────

    private void OnEnable()  => LevelGenerator.OnLevelReady += OnLevelReady;
    private void OnDisable() => LevelGenerator.OnLevelReady -= OnLevelReady;

    private void OnLevelReady()
    {
        if (spawnOnLevelReady) SpawnAll();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void SpawnAll()
    {
        LevelGenerator levelGen = FindFirstObjectByType<LevelGenerator>();
        if (levelGen == null) { Debug.LogError("[EnemySpawner] No LevelGenerator found."); return; }

        if (spawnTables != null && spawnTables.Count > 0)
            SpawnFromTables(levelGen);
        else
            SpawnFromLegacyEntries(levelGen);
    }

    // ── Table spawning ─────────────────────────────────────────────────────

    private void SpawnFromTables(LevelGenerator levelGen)
    {
        int currentLevel = WaveManager.Instance != null ? WaveManager.Instance.CurrentLevel : 1;
        int budget       = WaveManager.Instance != null ? WaveManager.Instance.GetTotalEnemyBudget() : 10;

        Debug.Log($"[EnemySpawner] Level {currentLevel} — budget: {budget} enemies.");

        int tablesUsed = 0;

        foreach (EnemySpawnTable table in spawnTables)
        {
            if (table == null) continue;

            if (!table.IsActiveForLevel(currentLevel))
            {
                Debug.Log($"[EnemySpawner] Skipping '{table.name}' (levels {table.minLevel}–{table.maxLevel}, current={currentLevel}).");
                continue;
            }

            List<LevelGenerator.PlacedRoom> matchingRooms = levelGen.GetAllRooms()
                .FindAll(r => r.prefabData.roomType == table.roomType
                           && r.roomGrid != null
                           && r.roomGrid.IsInitialized());

            if (matchingRooms.Count == 0)
            {
                Debug.LogWarning($"[EnemySpawner] No valid rooms of type {table.roomType} for table '{table.name}'.");
                continue;
            }

            List<(GameObject prefab, int count)> spawns = table.CalculateSpawns(budget);
            tablesUsed++;

            foreach (var (prefab, count) in spawns)
            {
                for (int i = 0; i < count; i++)
                {
                    LevelGenerator.PlacedRoom targetRoom = matchingRooms[Random.Range(0, matchingRooms.Count)];
                    GridPosition? spawnPos = GetRandomWalkableTile(targetRoom.roomGrid);

                    if (spawnPos == null)
                    {
                        Debug.LogWarning($"[EnemySpawner] No walkable tile in {targetRoom.roomInstance.name}.");
                        continue;
                    }

                    SpawnEnemy(prefab, targetRoom.roomGrid, spawnPos.Value);
                }
            }
        }

        if (tablesUsed == 0)
            Debug.LogWarning($"[EnemySpawner] No tables active for level {currentLevel}. Check minLevel/maxLevel settings.");
    }

    // ── Legacy spawning ────────────────────────────────────────────────────

    private void SpawnFromLegacyEntries(LevelGenerator levelGen)
    {
        foreach (SpawnEntry entry in spawnEntries)
        {
            if (entry.prefab == null) continue;

            List<LevelGenerator.PlacedRoom> matching = levelGen.GetAllRooms()
                .FindAll(r => r.prefabData.roomType == entry.roomType
                           && r.roomGrid != null
                           && r.roomGrid.IsInitialized());

            if (matching.Count == 0)
            {
                Debug.LogWarning($"[EnemySpawner] No valid rooms of type {entry.roomType}.");
                continue;
            }

            for (int i = 0; i < entry.count; i++)
            {
                LevelGenerator.PlacedRoom targetRoom = matching[Random.Range(0, matching.Count)];
                GridPosition? spawnPos = GetRandomWalkableTile(targetRoom.roomGrid);

                if (spawnPos == null)
                {
                    Debug.LogWarning($"[EnemySpawner] No walkable tile in {targetRoom.roomInstance.name}.");
                    continue;
                }

                SpawnEnemy(entry.prefab, targetRoom.roomGrid, spawnPos.Value);
            }
        }
    }

    // ── Core spawn ─────────────────────────────────────────────────────────

    public EnemyUnit SpawnEnemy(GameObject prefab, RoomGrid roomGrid, GridPosition position)
    {
        if (prefab == null || roomGrid == null) return null;

        TilemapRoomGrid tilemapGrid = roomGrid.GetTilemapRoomGrid();
        if (tilemapGrid == null) { Debug.LogError("[EnemySpawner] No TilemapRoomGrid."); return null; }

        if (!tilemapGrid.IsWalkable(position))
        {
            Debug.LogWarning($"[EnemySpawner] Position {position} not walkable.");
            return null;
        }

        Vector3 worldPos = roomGrid.GetWorldPosition(position);

        // Validate the world position is actually inside this room
        // This catches cases where the grid coord is valid in the tilemap data
        // but the world conversion lands outside the room's actual footprint
        if (!roomGrid.IsPositionInRoom(worldPos))
        {
            Debug.LogWarning($"[EnemySpawner] World pos {worldPos} for grid {position} is outside room bounds — skipping.");
            return null;
        }

        GameObject go = Instantiate(prefab, worldPos, Quaternion.identity);
        EnemyUnit enemyUnit = go.GetComponent<EnemyUnit>();

        if (enemyUnit == null)
        {
            Debug.LogError($"[EnemySpawner] {prefab.name} missing EnemyUnit component.");
            Destroy(go);
            return null;
        }

        enemyUnit.PlaceOnGrid(roomGrid, position);
        EnemyManager.Instance?.RegisterEnemy(enemyUnit);

        Debug.Log($"[EnemySpawner] Spawned {prefab.name} at {position} world {worldPos}");
        return enemyUnit;
    }

    // ── Tile selection ─────────────────────────────────────────────────────

    /// <summary>
    /// Finds a random walkable tile whose world position is confirmed to be
    /// inside the room. This prevents enemies spawning in void space when
    /// tilemap grid coords extend beyond the room's actual footprint.
    /// </summary>
    private GridPosition? GetRandomWalkableTile(RoomGrid roomGrid)
    {
        TilemapRoomGrid tilemapGrid = roomGrid.GetTilemapRoomGrid();
        if (tilemapGrid == null) return null;

        Tilemap floor = tilemapGrid.GetFloorTilemap();
        if (floor == null) return null;

        BoundsInt bounds = floor.cellBounds;
        List<GridPosition> candidates = new List<GridPosition>();

        // Try with padding first
        for (int x = bounds.xMin + borderPadding; x < bounds.xMax - borderPadding; x++)
            for (int y = bounds.yMin + borderPadding; y < bounds.yMax - borderPadding; y++)
            {
                GridPosition pos = new GridPosition(x, y);
                if (!tilemapGrid.IsWalkable(pos)) continue;

                // Key fix: verify the world position lands inside the room
                Vector3 worldPos = roomGrid.GetWorldPosition(pos);
                if (!roomGrid.IsPositionInRoom(worldPos)) continue;

                candidates.Add(pos);
            }

        // Fallback without padding if nothing found
        if (candidates.Count == 0)
        {
            for (int x = bounds.xMin; x < bounds.xMax; x++)
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    GridPosition pos = new GridPosition(x, y);
                    if (!tilemapGrid.IsWalkable(pos)) continue;

                    Vector3 worldPos = roomGrid.GetWorldPosition(pos);
                    if (!roomGrid.IsPositionInRoom(worldPos)) continue;

                    candidates.Add(pos);
                }
        }

        if (candidates.Count == 0)
        {
            Debug.LogWarning("[EnemySpawner] No valid in-bounds walkable tiles found.");
            return null;
        }

        return candidates[Random.Range(0, candidates.Count)];
    }

    private int ManhattanDist(GridPosition a, GridPosition b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);
}
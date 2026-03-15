using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class EnemySpawner : MonoBehaviour
{
    [System.Serializable]
    public class SpawnEntry
    {
        public GameObject prefab;
        [Min(1)] public int count = 1;
        public LevelGenerator.RoomType roomType = LevelGenerator.RoomType.Normal;
        public bool preferEdgeTiles = false;
    }

    [Header("Spawn Configuration")]
    [SerializeField] private List<SpawnEntry> spawnEntries = new List<SpawnEntry>();
    [SerializeField, Min(1)] private int borderPadding = 2;
    [SerializeField] private bool spawnOnLevelReady = true;

    private void OnEnable()  => LevelGenerator.OnLevelReady += OnLevelReady;
    private void OnDisable() => LevelGenerator.OnLevelReady -= OnLevelReady;

    private void OnLevelReady()
    {
        if (spawnOnLevelReady) SpawnAll();
    }

    public void SpawnAll()
    {
        LevelGenerator levelGen = FindFirstObjectByType<LevelGenerator>();
        if (levelGen == null) { Debug.LogError("[EnemySpawner] No LevelGenerator."); return; }

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
                GridPosition? spawnPos = GetRandomWalkableTile(targetRoom.roomGrid, entry.preferEdgeTiles);

                if (spawnPos == null)
                {
                    Debug.LogWarning($"[EnemySpawner] No walkable tile in {targetRoom.roomInstance.name}.");
                    continue;
                }

                SpawnEnemy(entry.prefab, targetRoom.roomGrid, spawnPos.Value);
            }
        }
    }

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

        // Safety check — if world position is at origin something went wrong
        if (worldPos == Vector3.zero)
        {
            Debug.LogWarning($"[EnemySpawner] GetWorldPosition returned zero for {position} — skipping spawn.");
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

    /// <summary>
    /// Gets a random walkable tile using actual tilemap cell bounds instead of
    /// assuming positions start at (0,0). This fixes enemies spawning at wrong
    /// positions when tilemap coords are offset.
    /// </summary>
    private GridPosition? GetRandomWalkableTile(RoomGrid roomGrid, bool preferEdge)
    {
        TilemapRoomGrid tilemapGrid = roomGrid.GetTilemapRoomGrid();
        if (tilemapGrid == null) return null;

        Tilemap floor = tilemapGrid.GetFloorTilemap();
        if (floor == null) return null;

        // Use actual tilemap bounds instead of (0,0) to (width,height)
        BoundsInt bounds = floor.cellBounds;
        List<GridPosition> candidates = new List<GridPosition>();

        for (int x = bounds.xMin + borderPadding; x < bounds.xMax - borderPadding; x++)
        {
            for (int y = bounds.yMin + borderPadding; y < bounds.yMax - borderPadding; y++)
            {
                GridPosition pos = new GridPosition(x, y);
                if (tilemapGrid.IsWalkable(pos))
                    candidates.Add(pos);
            }
        }

        if (candidates.Count == 0)
        {
            Debug.LogWarning($"[EnemySpawner] No walkable candidates found in bounds {bounds} with padding {borderPadding}. Trying without padding.");

            // Fallback: try without padding
            for (int x = bounds.xMin; x < bounds.xMax; x++)
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    GridPosition pos = new GridPosition(x, y);
                    if (tilemapGrid.IsWalkable(pos))
                        candidates.Add(pos);
                }

            if (candidates.Count == 0) return null;
        }

        if (preferEdge)
        {
            GridPosition center = new GridPosition(
                (bounds.xMin + bounds.xMax) / 2,
                (bounds.yMin + bounds.yMax) / 2);

            candidates.Sort((a, b) =>
                ManhattanDist(b, center).CompareTo(ManhattanDist(a, center)));
            return candidates[Random.Range(0, Mathf.Max(1, candidates.Count / 3))];
        }

        return candidates[Random.Range(0, candidates.Count)];
    }

    private int ManhattanDist(GridPosition a, GridPosition b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);
}
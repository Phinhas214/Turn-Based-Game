using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns enemies into rooms when the level is ready.
/// Enemies are placed on random valid, unoccupied tiles within their target room.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [System.Serializable]
    public class SpawnEntry
    {
        [Tooltip("The enemy prefab. Must have EnemyUnit, EnemyAI, and HealthComponent.")]
        public GameObject prefab;

        [Tooltip("How many of this enemy to spawn.")]
        [Min(1)]
        public int count = 1;

        [Tooltip("Which room type to spawn in.")]
        public LevelGenerator.RoomType roomType = LevelGenerator.RoomType.Normal;

        [Tooltip("If true, tries to spawn away from the room center (edges preferred).\n" +
                 "If false, any random valid tile is used.")]
        public bool preferEdgeTiles = false;
    }

    [Header("Spawn Configuration")]
    [SerializeField] private List<SpawnEntry> spawnEntries = new List<SpawnEntry>();

    [Tooltip("Tiles from the room border that are excluded from spawning.\n" +
             "Prevents enemies spawning inside walls.")]
    [SerializeField, Min(0)] private int borderPadding = 1;

    [Tooltip("If true enemies spawn as soon as the level is ready.")]
    [SerializeField] private bool spawnOnLevelReady = true;

    private void OnEnable()  => LevelGenerator.OnLevelReady += OnLevelReady;
    private void OnDisable() => LevelGenerator.OnLevelReady -= OnLevelReady;

    private void OnLevelReady()
    {
        if (spawnOnLevelReady)
            SpawnAll();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void SpawnAll()
    {
        LevelGenerator levelGen = FindFirstObjectByType<LevelGenerator>();
        if (levelGen == null)
        {
            Debug.LogError("[EnemySpawner] No LevelGenerator found.");
            return;
        }

        foreach (SpawnEntry entry in spawnEntries)
        {
            if (entry.prefab == null) continue;

            // Find all rooms of the requested type
            List<LevelGenerator.PlacedRoom> matchingRooms = levelGen.GetAllRooms()
                .FindAll(r => r.prefabData.roomType == entry.roomType);

            if (matchingRooms.Count == 0)
            {
                Debug.LogWarning($"[EnemySpawner] No rooms of type {entry.roomType} found.");
                continue;
            }

            // Spread spawns across matching rooms
            for (int i = 0; i < entry.count; i++)
            {
                // Pick a random matching room
                LevelGenerator.PlacedRoom targetRoom =
                    matchingRooms[Random.Range(0, matchingRooms.Count)];

                if (targetRoom.roomGrid == null) continue;

                GridPosition? spawnPos = GetRandomValidTile(targetRoom.roomGrid, entry.preferEdgeTiles);

                if (spawnPos == null)
                {
                    Debug.LogWarning($"[EnemySpawner] Could not find a valid tile in {targetRoom.roomInstance.name}.");
                    continue;
                }

                SpawnEnemy(entry.prefab, targetRoom.roomGrid, spawnPos.Value);
            }
        }
    }

    /// <summary>Spawn a single enemy at a specific grid position.</summary>
    public EnemyUnit SpawnEnemy(GameObject prefab, RoomGrid roomGrid, GridPosition position)
    {
        if (prefab == null || roomGrid == null) return null;

        if (!roomGrid.IsValidGridPosition(position))
        {
            Debug.LogWarning($"[EnemySpawner] Position {position} is out of bounds.");
            return null;
        }

        if (roomGrid.HasAnyUnitOnGridPosition(position))
        {
            Debug.LogWarning($"[EnemySpawner] Position {position} is already occupied.");
            return null;
        }

        Vector3 worldPos  = roomGrid.GetWorldPosition(position);
        GameObject go     = Instantiate(prefab, worldPos, Quaternion.identity);

        EnemyUnit enemyUnit = go.GetComponent<EnemyUnit>();
        if (enemyUnit == null)
        {
            Debug.LogError($"[EnemySpawner] {prefab.name} has no EnemyUnit component.");
            Destroy(go);
            return null;
        }

        enemyUnit.PlaceOnGrid(roomGrid, position);
        EnemyManager.Instance?.RegisterEnemy(enemyUnit);

        return enemyUnit;
    }

    // ── Tile selection ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns a random valid, unoccupied tile in the room.
    /// Respects borderPadding to avoid wall tiles.
    /// </summary>
    private GridPosition? GetRandomValidTile(RoomGrid roomGrid, bool preferEdge)
    {
        int w = roomGrid.GetWidth();
        int h = roomGrid.GetHeight();

        // Build candidate list
        List<GridPosition> candidates = new List<GridPosition>();

        for (int x = borderPadding; x < w - borderPadding; x++)
        {
            for (int z = borderPadding; z < h - borderPadding; z++)
            {
                GridPosition pos = new GridPosition(x, z);
                if (!roomGrid.IsValidGridPosition(pos)) continue;
                if (roomGrid.HasAnyUnitOnGridPosition(pos)) continue;
                candidates.Add(pos);
            }
        }

        if (candidates.Count == 0) return null;

        if (preferEdge)
        {
            // Sort by distance from center — furthest first
            GridPosition center = new GridPosition(w / 2, h / 2);
            candidates.Sort((a, b) =>
                ManhattanDist(b, center).CompareTo(ManhattanDist(a, center)));

            // Pick from the furthest 30% of tiles
            int edgePoolSize = Mathf.Max(1, candidates.Count / 3);
            return candidates[Random.Range(0, edgePoolSize)];
        }

        return candidates[Random.Range(0, candidates.Count)];
    }

    private int ManhattanDist(GridPosition a, GridPosition b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);
    }
}
using System.Collections.Generic;
using UnityEngine;

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
                .FindAll(r => r.prefabData.roomType == entry.roomType && r.roomGrid != null);

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

        // GetWorldPosition now returns the correct world position per room
        Vector3 worldPos = roomGrid.GetWorldPosition(position);

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

    private GridPosition? GetRandomWalkableTile(RoomGrid roomGrid, bool preferEdge)
    {
        TilemapRoomGrid tilemapGrid = roomGrid.GetTilemapRoomGrid();
        if (tilemapGrid == null) return null;

        int w = roomGrid.GetWidth();
        int h = roomGrid.GetHeight();
        List<GridPosition> candidates = new List<GridPosition>();

        for (int x = borderPadding; x < w - borderPadding; x++)
            for (int z = borderPadding; z < h - borderPadding; z++)
            {
                GridPosition pos = new GridPosition(x, z);
                if (tilemapGrid.IsWalkable(pos))
                    candidates.Add(pos);
            }

        if (candidates.Count == 0) return null;

        if (preferEdge)
        {
            GridPosition center = new GridPosition(w / 2, h / 2);
            candidates.Sort((a, b) =>
                ManhattanDist(b, center).CompareTo(ManhattanDist(a, center)));
            return candidates[Random.Range(0, Mathf.Max(1, candidates.Count / 3))];
        }

        return candidates[Random.Range(0, candidates.Count)];
    }

    private int ManhattanDist(GridPosition a, GridPosition b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);
}
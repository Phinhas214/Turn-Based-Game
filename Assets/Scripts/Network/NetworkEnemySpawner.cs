using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked EnemySpawner — runs on SERVER ONLY.
///
/// Differences from original EnemySpawner:
///   - After Instantiate(), calls NetworkObject.Spawn() so NGO syncs the object
///     to all clients.
///   - Registers with NetworkedEnemyManager instead of EnemyManager.
///   - Enemy prefabs must have: NetworkObject, NetworkTransform, NetworkedEnemyUnit,
///     NetworkedEnemyAI, NetworkedHealthComponent.
///
/// SETUP:
///   - Attach to a persistent manager GameObject.
///   - Fill spawnEntries with your enemy prefabs (same as before).
///   - Subscribe to NetworkedLevelGenerator.OnLevelReady.
/// </summary>
public class NetworkedEnemySpawner : NetworkBehaviour
{
    [System.Serializable]
    public class SpawnEntry
    {
        public GameObject prefab;
        [Min(1)] public int count = 1;
        public LevelGenerator.RoomType roomType    = LevelGenerator.RoomType.Normal;
        public bool preferEdgeTiles = false;
    }

    [Header("Spawn Configuration")]
    [SerializeField] private List<SpawnEntry> spawnEntries  = new List<SpawnEntry>();
    [SerializeField, Min(1)] private int      borderPadding = 2;
    [SerializeField] private bool             spawnOnLevelReady = true;

    private void OnEnable()
    {
        // Use the networked generator's event
        NetworkedLevelGenerator.OnLevelReady += OnLevelReady;
    }

    private void OnDisable()
    {
        NetworkedLevelGenerator.OnLevelReady -= OnLevelReady;
    }

    private void OnLevelReady()
    {
        // Only the server spawns enemies
        if (!IsServer) return;
        if (spawnOnLevelReady) SpawnAll();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Spawn all entries
    // ─────────────────────────────────────────────────────────────────────

    public void SpawnAll()
    {
        if (!IsServer) return;

        NetworkedLevelGenerator levelGen = FindFirstObjectByType<NetworkedLevelGenerator>();
        if (levelGen == null)
        {
            Debug.LogError("[NetworkedEnemySpawner] No NetworkedLevelGenerator found.");
            return;
        }

        foreach (SpawnEntry entry in spawnEntries)
        {
            if (entry.prefab == null) continue;

            // Collect rooms matching the desired type
            List<NetworkedLevelGenerator.PlacedRoom> matching = levelGen.GetAllRooms()
                .FindAll(r => r.prefabData.roomType == entry.roomType && r.roomGrid != null);

            if (matching.Count == 0)
            {
                Debug.LogWarning($"[NetworkedEnemySpawner] No rooms of type {entry.roomType}.");
                continue;
            }

            for (int i = 0; i < entry.count; i++)
            {
                NetworkedLevelGenerator.PlacedRoom targetRoom =
                    matching[Random.Range(0, matching.Count)];

                GridPosition? spawnPos = GetRandomWalkableTile(targetRoom.roomGrid, entry.preferEdgeTiles);

                if (spawnPos == null)
                {
                    Debug.LogWarning($"[NetworkedEnemySpawner] No walkable tile in {targetRoom.roomInstance.name}.");
                    continue;
                }

                SpawnEnemy(entry.prefab, targetRoom.roomGrid, spawnPos.Value);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Spawn single enemy
    // ─────────────────────────────────────────────────────────────────────

    public NetworkedEnemyUnit SpawnEnemy(GameObject prefab, RoomGrid roomGrid, GridPosition position)
    {
        if (!IsServer || prefab == null || roomGrid == null) return null;

        TilemapRoomGrid tilemapGrid = roomGrid.GetTilemapRoomGrid();
        if (tilemapGrid == null || !tilemapGrid.IsWalkable(position))
        {
            Debug.LogWarning($"[NetworkedEnemySpawner] Position {position} not walkable.");
            return null;
        }

        Vector3 worldPos = roomGrid.GetWorldPosition(position);
        GameObject go = Instantiate(prefab, worldPos, Quaternion.identity);

        NetworkObject netObj = go.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError($"[NetworkedEnemySpawner] Enemy prefab {prefab.name} missing NetworkObject!");
            Destroy(go);
            return null;
        }

        // Spawn over the network — clients will instantiate a copy automatically
        netObj.Spawn(destroyWithScene: true);

        NetworkedEnemyUnit enemyUnit = go.GetComponent<NetworkedEnemyUnit>();
        if (enemyUnit == null)
        {
            Debug.LogError($"[NetworkedEnemySpawner] Enemy prefab {prefab.name} missing NetworkedEnemyUnit!");
            netObj.Despawn();
            return null;
        }

        enemyUnit.PlaceOnGrid(roomGrid, position);
        NetworkedEnemyManager.Instance?.RegisterEnemy(enemyUnit);

        Debug.Log($"[NetworkedEnemySpawner] Spawned {prefab.name} at {position}");
        return enemyUnit;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Tile selection
    // ─────────────────────────────────────────────────────────────────────

    private GridPosition? GetRandomWalkableTile(RoomGrid roomGrid, bool preferEdge)
    {
        TilemapRoomGrid tilemapGrid = roomGrid.GetTilemapRoomGrid();
        if (tilemapGrid == null) return null;

        int w = roomGrid.GetWidth();
        int h = roomGrid.GetHeight();
        var candidates = new List<GridPosition>();

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
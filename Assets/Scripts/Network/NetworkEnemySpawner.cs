using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkedEnemySpawner : NetworkBehaviour
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
    [SerializeField] private List<SpawnEntry> spawnEntries      = new List<SpawnEntry>();
    [SerializeField, Min(1)] private int      borderPadding     = 2;
    [SerializeField] private bool             spawnOnLevelReady = true;

    private void OnEnable()  { NetworkedLevelGenerator.OnLevelReady += OnLevelReady; }
    private void OnDisable() { NetworkedLevelGenerator.OnLevelReady -= OnLevelReady; }

    private void OnLevelReady()
    {
        if (!IsServer) return;
        if (spawnOnLevelReady) SpawnAll();
    }

    public void SpawnAll()
    {
        if (!IsServer) return;
        NetworkedLevelGenerator levelGen = FindFirstObjectByType<NetworkedLevelGenerator>();
        if (levelGen == null) { Debug.LogError("[NetworkedEnemySpawner] No NetworkedLevelGenerator."); return; }

        foreach (SpawnEntry entry in spawnEntries)
        {
            if (entry.prefab == null) continue;
            var matching = levelGen.GetAllRooms()
                .FindAll(r => r.prefabData.roomType == entry.roomType && r.roomGrid != null);
            if (matching.Count == 0) continue;
            for (int i = 0; i < entry.count; i++)
            {
                var targetRoom = matching[Random.Range(0, matching.Count)];
                GridPosition? pos = GetRandomWalkableTile(targetRoom.roomGrid, entry.preferEdgeTiles);
                if (pos != null) SpawnEnemy(entry.prefab, targetRoom.roomGrid, pos.Value);
            }
        }
    }

    public NetworkedEnemyUnit SpawnEnemy(GameObject prefab, RoomGrid roomGrid, GridPosition position)
    {
        if (!IsServer || prefab == null || roomGrid == null) return null;
        TilemapRoomGrid tilemapGrid = roomGrid.GetTilemapRoomGrid();
        if (tilemapGrid == null || !tilemapGrid.IsWalkable(position)) return null;

        GameObject    go      = Instantiate(prefab, roomGrid.GetWorldPosition(position), Quaternion.identity);
        NetworkObject netObj  = go.GetComponent<NetworkObject>();
        if (netObj == null) { Destroy(go); return null; }

        netObj.Spawn(destroyWithScene: true);

        NetworkedEnemyUnit enemyUnit = go.GetComponent<NetworkedEnemyUnit>();
        if (enemyUnit == null) { netObj.Despawn(); return null; }

        enemyUnit.PlaceOnGrid(roomGrid, position);
        NetworkedEnemyManager.Instance?.RegisterEnemy(enemyUnit);
        enemyUnit.SyncRoomToClientsClientRpc(
            go.transform.position.x, go.transform.position.y, go.transform.position.z,
            position.x, position.z, roomGrid.gameObject.name);

        return enemyUnit;
    }

    private GridPosition? GetRandomWalkableTile(RoomGrid roomGrid, bool preferEdge)
    {
        TilemapRoomGrid tilemapGrid = roomGrid.GetTilemapRoomGrid();
        if (tilemapGrid == null) return null;

        int w = roomGrid.GetWidth(), h = roomGrid.GetHeight();
        var candidates = new List<GridPosition>();
        for (int x = borderPadding; x < w - borderPadding; x++)
            for (int z = borderPadding; z < h - borderPadding; z++)
            {
                GridPosition pos = new GridPosition(x, z);
                if (tilemapGrid.IsWalkable(pos)) candidates.Add(pos);
            }
        if (candidates.Count == 0) return null;

        if (preferEdge)
        {
            GridPosition center = new GridPosition(w / 2, h / 2);
            candidates.Sort((a, b) => ManhattanDist(b, center).CompareTo(ManhattanDist(a, center)));
            return candidates[Random.Range(0, Mathf.Max(1, candidates.Count / 3))];
        }
        return candidates[Random.Range(0, candidates.Count)];
    }

    private int ManhattanDist(GridPosition a, GridPosition b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);
}
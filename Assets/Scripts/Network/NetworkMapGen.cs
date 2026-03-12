using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Tilemaps;

public class NetworkedLevelGenerator : NetworkBehaviour
{
    [Serializable]
    public struct RoomSyncData : INetworkSerializable
    {
        public int   PrefabIndex;
        public float WorldX, WorldY, WorldZ;
        public int   GridX, GridZ;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PrefabIndex);
            serializer.SerializeValue(ref WorldX);
            serializer.SerializeValue(ref WorldY);
            serializer.SerializeValue(ref WorldZ);
            serializer.SerializeValue(ref GridX);
            serializer.SerializeValue(ref GridZ);
        }
    }

    [Header("Room Prefabs")]
    [SerializeField] private List<RoomPrefabData> roomPrefabs;
    [SerializeField] private GameObject hallwayPrefab;

    [Header("Player Prefabs")]
    [SerializeField] private List<GameObject> playerPrefabs;

    [Header("Generation Settings")]
    [SerializeField] private int   minRooms          = 5;
    [SerializeField] private int   maxRooms          = 10;
    [SerializeField] private float specialRoomChance = 0.3f;
    [SerializeField] private float cellSize          = 2f;

    [Header("Fallback")]
    [SerializeField] private GameObject fallbackPlayerPrefab;

    [Serializable]
    public class RoomPrefabData
    {
        public GameObject prefab;
        public LevelGenerator.RoomType roomType;
        [Range(0f, 1f)] public float spawnWeight = 1f;
        [HideInInspector] public int   width      = 10;
        [HideInInspector] public int   height     = 10;
        [HideInInspector] public Vector3 gridOffset = Vector3.zero;
    }

    public static Action OnLevelReady;

    private List<PlacedRoom>                                                    placedRooms;
    private Dictionary<Vector2Int, PlacedRoom>                                  roomGrid;
    private Dictionary<(PlacedRoom, LevelGenerator.Direction), PlacedRoom>      roomConnections;
    private int                                                                  generationSeed;
    private System.Random                                                        isolatedRandom;
    private HashSet<ulong>                                                       clientsConfirmedReady = new HashSet<ulong>();

    public class PlacedRoom
    {
        public GameObject     roomInstance;
        public RoomPrefabData prefabData;
        public RoomConnector  connector;
        public Vector3        worldPosition;
        public Vector2Int     gridPosition;
        public RoomGrid       roomGrid;
        public int            prefabIndex;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport != null)
        {
            transport.MaxPayloadSize   = 4 * 1024 * 1024;
            transport.MaxSendQueueSize = 4 * 1024 * 1024;
        }

        StartCoroutine(WaitThenGenerate());
    }

    private IEnumerator WaitThenGenerate()
    {
        yield return new WaitForEndOfFrame();
        ReadRoomPrefabDefinitions();
        GenerateLevelOnServer();
    }

    private void GenerateLevelOnServer()
    {
        generationSeed = UnityEngine.Random.Range(0, int.MaxValue);
        UnityEngine.Random.InitState(generationSeed);
        isolatedRandom = null;

        ClearLevel();
        placedRooms     = new List<PlacedRoom>();
        roomGrid        = new Dictionary<Vector2Int, PlacedRoom>();
        roomConnections = new Dictionary<(PlacedRoom, LevelGenerator.Direction), PlacedRoom>();

        GenerateRoomLayout();
        InitializeRoomGrids();
        InitializeDoors();

        SyncLevelToClientsClientRpc(generationSeed, null);
        StartCoroutine(WaitForAllClientsReadyThenSpawn());
    }

    [ServerRpc(RequireOwnership = false)]
    public void ClientLevelReadyServerRpc(ServerRpcParams rpcParams = default)
    {
        clientsConfirmedReady.Add(rpcParams.Receive.SenderClientId);
    }

    private IEnumerator WaitForAllClientsReadyThenSpawn()
    {
        int nonHostClients = NetworkManager.Singleton.ConnectedClientsList.Count - 1;

        if (nonHostClients <= 0)
        {
            SpawnAllPlayers();
            OnLevelReady?.Invoke();
            yield break;
        }

        while (clientsConfirmedReady.Count < nonHostClients)
        {
            nonHostClients = NetworkManager.Singleton.ConnectedClientsList.Count - 1;
            yield return null;
        }

        SpawnAllPlayers();
        OnLevelReady?.Invoke();
    }

    [ClientRpc]
    private void SyncLevelToClientsClientRpc(int seed, RoomSyncData[] rooms)
    {
        if (IsServer) return;
        RegenerateLevelFromSeed(seed);
    }

    private void RegenerateLevelFromSeed(int seed)
    {
        isolatedRandom = null;
        ReadRoomPrefabDefinitions();
        UnityEngine.Random.InitState(seed);

        ClearLevel();
        placedRooms     = new List<PlacedRoom>();
        roomGrid        = new Dictionary<Vector2Int, PlacedRoom>();
        roomConnections = new Dictionary<(PlacedRoom, LevelGenerator.Direction), PlacedRoom>();

        GenerateRoomLayout();
        InitializeRoomGrids();
        InitializeDoors();
        ReconstructConnectionsFromGrid();

        PlacedRoom startRoom = placedRooms.Find(r => r.prefabData.roomType == LevelGenerator.RoomType.Start);
        if (startRoom != null)
        {
            RoomManager.Instance?.SetCurrentRoom(ConvertToOldPlacedRoom(startRoom));
            LevelGrid.Instance?.SetCurrentRoomGrid(startRoom.roomGrid);
        }

        ClientLevelReadyServerRpc();
        StartCoroutine(WaitForLocalPlayerThenFireReady());
    }

    private void ReconstructConnectionsFromGrid()
    {
        foreach (PlacedRoom a in placedRooms)
        {
            foreach (LevelGenerator.Direction dir in System.Enum.GetValues(typeof(LevelGenerator.Direction)))
            {
                if (roomConnections.ContainsKey((a, dir))) continue;

                Vector2Int neighbourGrid = a.gridPosition + GetDirectionOffset(dir);
                if (!roomGrid.TryGetValue(neighbourGrid, out PlacedRoom b)) continue;
                if (a.connector == null || b.connector == null) continue;
                if (!a.connector.HasConnectionPoint(dir)) continue;
                if (!b.connector.HasConnectionPoint(GetOppositeDirection(dir))) continue;

                roomConnections[(a, dir)]                       = b;
                roomConnections[(b, GetOppositeDirection(dir))] = a;
            }
        }
    }

    private IEnumerator WaitForLocalPlayerThenFireReady()
    {
        float timeout = 15f;
        float elapsed = 0f;
        int   frame   = 0;

        while (elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            if (++frame % 10 == 0)
            {
                foreach (Unit unit in FindObjectsByType<Unit>(FindObjectsSortMode.None))
                {
                    var netObj = unit.GetComponent<NetworkObject>();
                    if (netObj != null && netObj.IsOwner)
                    {
                        OnLevelReady?.Invoke();
                        yield break;
                    }
                }
            }
            yield return null;
        }

        OnLevelReady?.Invoke();
    }

    private void SpawnAllPlayers()
    {
        PlacedRoom startRoom = placedRooms.Find(r => r.prefabData.roomType == LevelGenerator.RoomType.Start);
        if (startRoom == null) { Debug.LogError("[NetworkedLevelGenerator] No start room!"); return; }

        RoomManager.Instance?.SetCurrentRoom(ConvertToOldPlacedRoom(startRoom));
        LevelGrid.Instance?.SetCurrentRoomGrid(startRoom.roomGrid);

        var connectedClients = NetworkManager.Singleton.ConnectedClientsList;
        int centerX = startRoom.roomGrid.GetWidth()  / 2;
        int centerZ = startRoom.roomGrid.GetHeight() / 2;

        for (int i = 0; i < connectedClients.Count; i++)
        {
            ulong clientId = connectedClients[i].ClientId;
            int charIndex  = LobbySync.Instance != null
                ? LobbySync.Instance.GetCharacterIndex(clientId) : 0;

            GameObject prefabToSpawn = GetPlayerPrefab(charIndex);
            if (prefabToSpawn == null) continue;

            GridPosition spawnPos = new GridPosition(
                centerX + (i % 2 == 0 ? -1 : 1),
                centerZ + (i / 2 == 0 ? -1 : 1));

            if (!startRoom.roomGrid.IsValidGridPosition(spawnPos))
                spawnPos = new GridPosition(centerX, centerZ);

            Vector3 spawnWorldPos = startRoom.roomGrid.GetWorldPosition(spawnPos);

            GameObject    playerGO = Instantiate(prefabToSpawn, spawnWorldPos, Quaternion.identity);
            NetworkObject netObj   = playerGO.GetComponent<NetworkObject>();

            if (netObj == null) { Debug.LogError("[NetworkedLevelGenerator] Player prefab missing NetworkObject!"); Destroy(playerGO); continue; }

            netObj.SpawnAsPlayerObject(clientId, destroyWithScene: true);

            Unit unit = playerGO.GetComponent<Unit>();
            if (unit != null) unit.PlaceInRoom(startRoom.roomGrid, spawnPos);

            NetworkedUnit netUnit = playerGO.GetComponent<NetworkedUnit>();
            if (netUnit != null) netUnit.PlaceInRoom(startRoom.roomGrid, spawnPos);

            RoomManager.Instance?.SetCurrentRoom(ConvertToOldPlacedRoom(startRoom), clientId);

            ulong         capturedClientId  = clientId;
            GridPosition  capturedSpawnPos  = spawnPos;
            RoomGrid      capturedRoomGrid  = startRoom.roomGrid;
            NetworkedUnit capturedNetUnit   = netUnit;
            StartCoroutine(SendInitRpcAfterDelay(capturedNetUnit, capturedRoomGrid, capturedSpawnPos, capturedClientId));
        }
    }

    private IEnumerator SendInitRpcAfterDelay(NetworkedUnit netUnit, RoomGrid roomGrid,
                                               GridPosition spawnPos, ulong clientId)
    {
        yield return new WaitForSeconds(0.5f);
        if (netUnit == null || roomGrid == null) yield break;

        Vector3 spawnWorldPos = roomGrid.GetWorldPosition(spawnPos);
        ClientRpcParams ownerOnly = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { clientId } }
        };

        netUnit.InitialiseUnitOnClientClientRpc(
            spawnPos.x, spawnPos.z,
            spawnWorldPos.x, spawnWorldPos.y, spawnWorldPos.z,
            roomGrid.gameObject.name,
            ownerOnly);
    }

    private GameObject GetPlayerPrefab(int charIndex)
    {
        if (playerPrefabs != null && charIndex >= 0 && charIndex < playerPrefabs.Count)
            return playerPrefabs[charIndex];
        if (fallbackPlayerPrefab != null) return fallbackPlayerPrefab;
        Debug.LogError("[NetworkedLevelGenerator] No valid player prefab!");
        return null;
    }

    private RoomSyncData[] BuildSyncData()
    {
        var list = new List<RoomSyncData>();
        foreach (PlacedRoom room in placedRooms)
        {
            list.Add(new RoomSyncData
            {
                PrefabIndex = room.prefabIndex,
                WorldX = room.worldPosition.x,
                WorldY = room.worldPosition.y,
                WorldZ = room.worldPosition.z,
                GridX  = room.gridPosition.x,
                GridZ  = room.gridPosition.y
            });
        }
        return list.ToArray();
    }

    private void ReadRoomPrefabDefinitions()
    {
        foreach (RoomPrefabData data in roomPrefabs)
        {
            if (data.prefab == null) continue;
            RoomTilemapSetup setup = data.prefab.GetComponent<RoomTilemapSetup>();
            if (setup != null) { data.width = setup.GetWidth(); data.height = setup.GetHeight(); data.gridOffset = setup.GetGridOffset(); }
            else               { data.width = 10; data.height = 10; }
        }
    }

    private int   GenRange(int min, int max) => isolatedRandom != null ? isolatedRandom.Next(min, max + 1) : UnityEngine.Random.Range(min, max + 1);
    private float GenValue()                 => isolatedRandom != null ? (float)isolatedRandom.NextDouble() : UnityEngine.Random.value;

    private void ShuffleListGen<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int j = GenRange(i, list.Count - 1);
            T tmp = list[i]; list[i] = list[j]; list[j] = tmp;
        }
    }

    private void GenerateRoomLayout()
    {
        PlacedRoom startRoom = PlaceRoom(LevelGenerator.RoomType.Start, Vector2Int.zero, Vector3.zero);
        if (startRoom == null) { Debug.LogError("Failed to place start room!"); return; }

        var queue = new Queue<PlacedRoom>();
        queue.Enqueue(startRoom);

        int roomCount        = 1;
        int target           = GenRange(minRooms, maxRooms);
        int totalAttempts    = 0;
        int maxTotalAttempts = target * 20;

        while (queue.Count > 0 && roomCount < target && totalAttempts < maxTotalAttempts)
        {
            PlacedRoom current = queue.Dequeue();
            var dirs   = GetAvailableDirections(current);
            ShuffleListGen(dirs);
            int toMake = Mathf.Min(GenRange(1, 2), dirs.Count);

            for (int i = 0; i < dirs.Count && roomCount < target; i++)
            {
                totalAttempts++;
                var dir      = dirs[i];
                var roomType = DetermineRoomType(roomCount, target);
                PlacedRoom newRoom = PlaceRoomInDirection(current, dir, roomType);

                if (newRoom != null)
                {
                    CreateHallway(current, newRoom, dir);
                    current.connector.MarkConnectionUsed(dir);
                    newRoom.connector.MarkConnectionUsed(GetOppositeDirection(dir));
                    if (roomType != LevelGenerator.RoomType.End) queue.Enqueue(newRoom);
                    roomCount++;
                    if (roomCount % toMake == 0) break;
                }
                else
                {
                    if (!queue.Contains(current)) queue.Enqueue(current);
                }
            }
        }
    }

    private PlacedRoom PlaceRoom(LevelGenerator.RoomType roomType, Vector2Int gridPos, Vector3 worldPos)
    {
        if (roomGrid.ContainsKey(gridPos)) return null;
        RoomPrefabData prefabData = GetRandomRoomPrefab(roomType);
        if (prefabData == null) return null;
        return InstantiateRoom(prefabData, roomPrefabs.IndexOf(prefabData), gridPos, worldPos);
    }

    private PlacedRoom InstantiateRoom(RoomPrefabData prefabData, int prefabIndex, Vector2Int gridPos, Vector3 worldPos)
    {
        GameObject instance = Instantiate(prefabData.prefab, worldPos, Quaternion.identity, transform);
        instance.name = $"{prefabData.roomType}Room_({gridPos.x},{gridPos.y})";

        RoomConnector connector = instance.GetComponent<RoomConnector>();
        if (connector == null) { Debug.LogError($"[NetworkedLevelGenerator] {prefabData.prefab.name} missing RoomConnector!"); Destroy(instance); return null; }

        var placed = new PlacedRoom
        {
            roomInstance  = instance,
            prefabData    = prefabData,
            connector     = connector,
            worldPosition = worldPos,
            gridPosition  = gridPos,
            prefabIndex   = prefabIndex
        };

        placedRooms.Add(placed);
        roomGrid[gridPos] = placed;
        return placed;
    }

    private void InitializeRoomGrids()
    {
        foreach (PlacedRoom room in placedRooms)
        {
            RoomTilemapSetup setup = room.roomInstance.GetComponent<RoomTilemapSetup>()
                                    ?? room.roomInstance.AddComponent<RoomTilemapSetup>();
            setup.Initialize();

            RoomGrid rg = room.roomInstance.GetComponent<RoomGrid>()
                          ?? room.roomInstance.AddComponent<RoomGrid>();

            rg.Initialize(setup.GetWidth(), setup.GetHeight(), setup.GetCellSize(),
                          room.worldPosition, setup.GetGridOffset(), null);
            room.roomGrid = rg;

            LevelGrid.Instance?.RegisterRoomGrid(rg);
        }
    }

    private void InitializeDoors()
    {
        foreach (PlacedRoom room in placedRooms)
            foreach (RoomDoor door in room.roomInstance.GetComponentsInChildren<RoomDoor>())
                door.Initialize(ConvertToOldPlacedRoom(room));
    }

    private void ClearLevel()
    {
        foreach (Transform child in transform) Destroy(child.gameObject);
    }

    private PlacedRoom PlaceRoomInDirection(PlacedRoom existing, LevelGenerator.Direction dir, LevelGenerator.RoomType type)
    {
        var exit = existing.connector.GetConnectionPoint(dir);
        if (exit?.transform == null) return null;

        RoomPrefabData newPrefab = GetRandomRoomPrefab(type);
        if (newPrefab == null) return null;

        RoomConnector tempConn = newPrefab.prefab.GetComponent<RoomConnector>();
        if (tempConn == null) return null;

        var oppDir = GetOppositeDirection(dir);
        if (!tempConn.HasConnectionPoint(oppDir)) return null;

        var    entry  = tempConn.GetConnectionPoint(oppDir);
        Vector3    newPos  = exit.transform.position - entry.transform.localPosition;
        Vector2Int newGrid = existing.gridPosition + GetDirectionOffset(dir);

        return PlaceRoom(type, newGrid, newPos);
    }

    private void CreateHallway(PlacedRoom a, PlacedRoom b, LevelGenerator.Direction dir)
    {
        roomConnections[(a, dir)]                       = b;
        roomConnections[(b, GetOppositeDirection(dir))] = a;

        if (hallwayPrefab == null) return;

        var exit  = a.connector.GetConnectionPoint(dir);
        var entry = b.connector.GetConnectionPoint(GetOppositeDirection(dir));
        if (exit?.transform == null || entry?.transform == null) return;

        Vector3    pos = (exit.transform.position + entry.transform.position) * 0.5f;
        Quaternion rot = (dir == LevelGenerator.Direction.East || dir == LevelGenerator.Direction.West)
            ? Quaternion.Euler(0, 90, 0) : Quaternion.identity;

        Instantiate(hallwayPrefab, pos, rot, transform);
    }

    private List<LevelGenerator.Direction> GetAvailableDirections(PlacedRoom room)
    {
        var available = new List<LevelGenerator.Direction>();
        if (room.connector == null) return available;
        foreach (LevelGenerator.Direction dir in Enum.GetValues(typeof(LevelGenerator.Direction)))
        {
            if (room.connector.IsDirectionAvailable(dir) &&
                !roomGrid.ContainsKey(room.gridPosition + GetDirectionOffset(dir)))
                available.Add(dir);
        }
        return available;
    }

    private LevelGenerator.RoomType DetermineRoomType(int current, int target)
    {
        if (current == target - 1) return LevelGenerator.RoomType.End;
        if (GenValue() < specialRoomChance) return LevelGenerator.RoomType.Special;
        return LevelGenerator.RoomType.Normal;
    }

    private RoomPrefabData GetRandomRoomPrefab(LevelGenerator.RoomType type)
    {
        var valid = roomPrefabs.FindAll(p => p.roomType == type);
        if (valid.Count == 0) return null;

        float total = 0f;
        foreach (var p in valid) total += p.spawnWeight;
        float rand = GenValue() * total;
        float curr = 0f;
        foreach (var p in valid) { curr += p.spawnWeight; if (rand <= curr) return p; }
        return valid[0];
    }

    private Vector2Int GetDirectionOffset(LevelGenerator.Direction dir)
    {
        switch (dir)
        {
            case LevelGenerator.Direction.North: return new Vector2Int(0,  1);
            case LevelGenerator.Direction.South: return new Vector2Int(0, -1);
            case LevelGenerator.Direction.East:  return new Vector2Int(1,  0);
            case LevelGenerator.Direction.West:  return new Vector2Int(-1, 0);
            default: return Vector2Int.zero;
        }
    }

    public LevelGenerator.Direction GetOppositeDirection(LevelGenerator.Direction dir)
    {
        switch (dir)
        {
            case LevelGenerator.Direction.North: return LevelGenerator.Direction.South;
            case LevelGenerator.Direction.South: return LevelGenerator.Direction.North;
            case LevelGenerator.Direction.East:  return LevelGenerator.Direction.West;
            case LevelGenerator.Direction.West:  return LevelGenerator.Direction.East;
            default: return LevelGenerator.Direction.North;
        }
    }

    public PlacedRoom             GetPlacedRoomAt(Vector2Int gridPos) { roomGrid.TryGetValue(gridPos, out PlacedRoom room); return room; }
    public List<PlacedRoom>       GetAllRooms()                       => placedRooms;
    public PlacedRoom             GetConnectedRoom(PlacedRoom room, LevelGenerator.Direction dir) { roomConnections.TryGetValue((room, dir), out PlacedRoom connected); return connected; }

    public LevelGenerator.PlacedRoom ConvertToOldPlacedRoom(PlacedRoom room)
    {
        return new LevelGenerator.PlacedRoom
        {
            roomInstance  = room.roomInstance,
            prefabData    = ConvertPrefabData(room.prefabData),
            connector     = room.connector,
            worldPosition = room.worldPosition,
            gridPosition  = room.gridPosition,
            roomGrid      = room.roomGrid
        };
    }

    private LevelGenerator.RoomPrefabData ConvertPrefabData(RoomPrefabData data)
    {
        return new LevelGenerator.RoomPrefabData
        {
            prefab      = data.prefab,
            roomType    = data.roomType,
            spawnWeight = data.spawnWeight,
            width       = data.width,
            height      = data.height,
            gridOffset  = data.gridOffset
        };
    }
}
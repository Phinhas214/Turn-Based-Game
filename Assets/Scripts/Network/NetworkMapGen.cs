using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Networked replacement for LevelGenerator.
///
/// HOW IT WORKS:
///   HOST:
///     - Generates the level using a random seed (same algorithm as before).
///     - After generation, sends a compact RoomSyncData[] to all clients via ClientRpc.
///     - Spawns one NetworkObject player prefab per connected client.
///     - Spawns enemies as NetworkObjects (server-owned).
///
///   CLIENTS:
///     - Receive RoomSyncData[] (prefab index + world position for each room).
///     - Instantiate the same room prefabs at the same positions locally.
///     - Do NOT run any generation logic — they just build from the host's data.
///     - Clients never spawn players or enemies themselves.
///
/// CHARACTER SELECTION:
///   - Before the game starts, each player picks a character index in the lobby.
///   - NetworkGameManager.GetCharacterSelections() returns a Dictionary<UGS_ID, int>.
///   - This generator reads that and maps UGS IDs to connected client IDs.
///
/// SETUP:
///   - Replace LevelGenerator with this script on your level generator GameObject.
///   - Add NetworkObject component to this GameObject.
///   - Fill playerPrefabs list (one per character class).
///   - Fill roomPrefabs as before.
/// </summary>
public class NetworkedLevelGenerator : NetworkBehaviour
{
    // ── Sync data — minimal footprint sent over the network ───────────────
    [Serializable]
    public struct RoomSyncData : INetworkSerializable
    {
        public int   PrefabIndex;   // index into roomPrefabs list
        public float WorldX;
        public float WorldY;
        public float WorldZ;
        public int   GridX;         // layout grid position (not world)
        public int   GridZ;

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

    // ── Inspector ─────────────────────────────────────────────────────────
    [Header("Room Prefabs (same order on all clients)")]
    [SerializeField] private List<RoomPrefabData> roomPrefabs;
    [SerializeField] private GameObject hallwayPrefab;

    [Header("Player Prefabs (index matches character selection)")]
    [Tooltip("Index 0 = Knight, 1 = Rogue, 2 = Mage, 3 = Cleric — must match CharacterSelectUI order.")]
    [SerializeField] private List<GameObject> playerPrefabs;

    [Header("Generation Settings")]
    [SerializeField] private int   minRooms        = 5;
    [SerializeField] private int   maxRooms        = 10;
    [SerializeField] private float specialRoomChance = 0.3f;
    [SerializeField] private float cellSize        = 2f;

    [Header("Fallback (if no character selection data)")]
    [SerializeField] private GameObject fallbackPlayerPrefab;

    // ── Shared types (keep parity with old LevelGenerator) ────────────────
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

    // ── Events ────────────────────────────────────────────────────────────
    public static Action OnLevelReady;

    // ── Runtime ───────────────────────────────────────────────────────────
    private List<PlacedRoom>                        placedRooms;
    private Dictionary<Vector2Int, PlacedRoom>      roomGrid;
    private Dictionary<(PlacedRoom, LevelGenerator.Direction), PlacedRoom> roomConnections;
    private int                                     generationSeed;

    public class PlacedRoom
    {
        public GameObject                roomInstance;
        public RoomPrefabData            prefabData;
        public RoomConnector             connector;
        public Vector3                   worldPosition;
        public Vector2Int                gridPosition;
        public RoomGrid                  roomGrid;
        public int                       prefabIndex; // needed for sync
    }

    // ─────────────────────────────────────────────────────────────────────
    // NGO Spawn
    // ─────────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        // Wait a frame for all clients to connect before generating
        StartCoroutine(WaitThenGenerate());
    }

    private IEnumerator WaitThenGenerate()
    {
        yield return new WaitForSeconds(0.5f);
        ReadRoomPrefabDefinitions();
        GenerateLevelOnServer();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Server: Generate
    // ─────────────────────────────────────────────────────────────────────

    private void GenerateLevelOnServer()
    {
        generationSeed = UnityEngine.Random.Range(0, int.MaxValue);
        UnityEngine.Random.InitState(generationSeed);

        ClearLevel();
        placedRooms    = new List<PlacedRoom>();
        roomGrid       = new Dictionary<Vector2Int, PlacedRoom>();
        roomConnections = new Dictionary<(PlacedRoom, LevelGenerator.Direction), PlacedRoom>();

        GenerateRoomLayout();
        InitializeRoomGrids();
        InitializeDoors();

        // Build sync data
        RoomSyncData[] syncData = BuildSyncData();

        // Send to all clients
        SyncLevelToClientsClientRpc(generationSeed, syncData);

        // Spawn players
        StartCoroutine(SpawnPlayersAfterDelay());
    }

    private IEnumerator SpawnPlayersAfterDelay()
    {
        // Give clients a moment to reconstruct the level
        yield return new WaitForSeconds(1.0f);
        SpawnAllPlayers();
        OnLevelReady?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────────────
    // ClientRpc: Sync level layout
    // ─────────────────────────────────────────────────────────────────────

    [ClientRpc]
    private void SyncLevelToClientsClientRpc(int seed, RoomSyncData[] rooms)
    {
        if (IsServer) return; // Host already has the level built

        Debug.Log($"[NetworkedLevelGenerator] Client received level data: {rooms.Length} rooms.");

        // Reconstruct the same level from the sync data
        ReconstructLevelOnClient(seed, rooms);
    }

    private void ReconstructLevelOnClient(int seed, RoomSyncData[] rooms)
    {
        UnityEngine.Random.InitState(seed); // same seed = same random choices

        ClearLevel();
        placedRooms     = new List<PlacedRoom>();
        roomGrid        = new Dictionary<Vector2Int, PlacedRoom>();
        roomConnections = new Dictionary<(PlacedRoom, LevelGenerator.Direction), PlacedRoom>();

        foreach (RoomSyncData data in rooms)
        {
            if (data.PrefabIndex < 0 || data.PrefabIndex >= roomPrefabs.Count)
            {
                Debug.LogError($"[NetworkedLevelGenerator] Invalid prefab index {data.PrefabIndex}");
                continue;
            }

            RoomPrefabData prefabData = roomPrefabs[data.PrefabIndex];
            Vector3 worldPos = new Vector3(data.WorldX, data.WorldY, data.WorldZ);
            Vector2Int gridPos = new Vector2Int(data.GridX, data.GridZ);

            PlacedRoom room = InstantiateRoom(prefabData, data.PrefabIndex, gridPos, worldPos);
            if (room != null)
            {
                placedRooms.Add(room);
                roomGrid[gridPos] = room;
            }
        }

        InitializeRoomGrids();
        InitializeDoors();

        // Set the start room
        PlacedRoom startRoom = placedRooms.Find(r => r.prefabData.roomType == LevelGenerator.RoomType.Start);
        if (startRoom != null)
        {
            RoomManager.Instance?.SetCurrentRoom(ConvertToOldPlacedRoom(startRoom));
            LevelGrid.Instance?.SetCurrentRoomGrid(startRoom.roomGrid);
        }

        OnLevelReady?.Invoke();
        Debug.Log("[NetworkedLevelGenerator] Client level reconstruction complete.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Player spawning (Server only)
    // ─────────────────────────────────────────────────────────────────────

    private void SpawnAllPlayers()
    {
        PlacedRoom startRoom = placedRooms.Find(r => r.prefabData.roomType == LevelGenerator.RoomType.Start);
        if (startRoom == null)
        {
            Debug.LogError("[NetworkedLevelGenerator] No start room found for player spawning!");
            return;
        }

        // Set start room for server-side systems
        RoomManager.Instance?.SetCurrentRoom(ConvertToOldPlacedRoom(startRoom));
        LevelGrid.Instance?.SetCurrentRoomGrid(startRoom.roomGrid);

        // Read character selections directly from LobbySync — it persists from the menu scene
        // via DontDestroyOnLoad so the data is guaranteed to be here when we spawn.
        var connectedClients = NetworkManager.Singleton.ConnectedClientsList;

        int centerX = startRoom.roomGrid.GetWidth() / 2;
        int centerZ = startRoom.roomGrid.GetHeight() / 2;

        for (int i = 0; i < connectedClients.Count; i++)
        {
            ulong clientId = connectedClients[i].ClientId;

            // LobbySync holds the char index each client chose in the menu.
            // Falls back to 0 if LobbySync is missing or the client never registered.
            int charIndex = (LobbySync.Instance != null)
                ? LobbySync.Instance.GetCharacterIndex(clientId)
                : 0;

            Debug.Log($"[NetworkedLevelGenerator] Client {clientId} → charIndex {charIndex}");

            GameObject prefabToSpawn = GetPlayerPrefab(charIndex);

            // Offset each player slightly so they don't overlap
            GridPosition spawnPos = new GridPosition(
                centerX + (i % 2 == 0 ? -1 : 1),
                centerZ + (i / 2 == 0 ? -1 : 1));

            if (!startRoom.roomGrid.IsValidGridPosition(spawnPos))
                spawnPos = new GridPosition(centerX, centerZ);

            Vector3 worldPos = startRoom.roomGrid.GetWorldPosition(spawnPos);

            GameObject playerGO = Instantiate(prefabToSpawn, worldPos, Quaternion.identity);
            NetworkObject netObj = playerGO.GetComponent<NetworkObject>();

            if (netObj == null)
            {
                Debug.LogError($"[NetworkedLevelGenerator] Player prefab missing NetworkObject!");
                Destroy(playerGO);
                continue;
            }

            netObj.SpawnAsPlayerObject(clientId, destroyWithScene: true);

            // Place on grid
            Unit unit = playerGO.GetComponent<Unit>();
            if (unit != null)
                unit.PlaceInRoom(startRoom.roomGrid, spawnPos);

            Debug.Log($"[NetworkedLevelGenerator] Spawned class {charIndex} for client {clientId} at {spawnPos}");
        }
    }

    private GameObject GetPlayerPrefab(int charIndex)
    {
        if (playerPrefabs != null && charIndex >= 0 && charIndex < playerPrefabs.Count)
            return playerPrefabs[charIndex];
        if (fallbackPlayerPrefab != null)
            return fallbackPlayerPrefab;
        Debug.LogError("[NetworkedLevelGenerator] No valid player prefab found!");
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Sync data builder
    // ─────────────────────────────────────────────────────────────────────

    private RoomSyncData[] BuildSyncData()
    {
        var list = new List<RoomSyncData>();
        foreach (PlacedRoom room in placedRooms)
        {
            list.Add(new RoomSyncData
            {
                PrefabIndex = room.prefabIndex,
                WorldX      = room.worldPosition.x,
                WorldY      = room.worldPosition.y,
                WorldZ      = room.worldPosition.z,
                GridX       = room.gridPosition.x,
                GridZ       = room.gridPosition.y
            });
        }
        return list.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Room generation (server only — same logic as original LevelGenerator)
    // ─────────────────────────────────────────────────────────────────────

    private void ReadRoomPrefabDefinitions()
    {
        foreach (RoomPrefabData data in roomPrefabs)
        {
            if (data.prefab == null) continue;
            RoomTilemapSetup setup = data.prefab.GetComponent<RoomTilemapSetup>();
            if (setup != null)
            {
                data.width      = setup.GetWidth();
                data.height     = setup.GetHeight();
                data.gridOffset = setup.GetGridOffset();
            }
            else
            {
                data.width  = 10;
                data.height = 10;
            }
        }
    }

    private void GenerateRoomLayout()
    {
        PlacedRoom startRoom = PlaceRoom(LevelGenerator.RoomType.Start, Vector2Int.zero, Vector3.zero);
        if (startRoom == null) { Debug.LogError("Failed to place start room!"); return; }

        var queue = new Queue<PlacedRoom>();
        queue.Enqueue(startRoom);

        int roomCount   = 1;
        int target      = UnityEngine.Random.Range(minRooms, maxRooms + 1);
        int attempts    = 0;

        while (queue.Count > 0 && roomCount < target && attempts < 100)
        {
            attempts++;
            PlacedRoom current = queue.Dequeue();

            var dirs = GetAvailableDirections(current);
            ShuffleList(dirs);
            int toMake = Mathf.Min(UnityEngine.Random.Range(1, 3), dirs.Count);

            for (int i = 0; i < toMake && roomCount < target; i++)
            {
                var dir      = dirs[i];
                var roomType = DetermineRoomType(roomCount, target);
                PlacedRoom newRoom = PlaceRoomInDirection(current, dir, roomType);

                if (newRoom != null)
                {
                    CreateHallway(current, newRoom, dir);
                    current.connector.MarkConnectionUsed(dir);
                    newRoom.connector.MarkConnectionUsed(GetOppositeDirection(dir));

                    if (roomType != LevelGenerator.RoomType.End)
                        queue.Enqueue(newRoom);

                    roomCount++;
                }
            }
        }

        Debug.Log($"[NetworkedLevelGenerator] Generated {placedRooms.Count} rooms.");
    }

    private PlacedRoom PlaceRoom(LevelGenerator.RoomType roomType, Vector2Int gridPos, Vector3 worldPos)
    {
        if (roomGrid.ContainsKey(gridPos)) return null;

        RoomPrefabData prefabData = GetRandomRoomPrefab(roomType);
        if (prefabData == null) return null;

        int prefabIndex = roomPrefabs.IndexOf(prefabData);
        return InstantiateRoom(prefabData, prefabIndex, gridPos, worldPos);
    }

    private PlacedRoom InstantiateRoom(RoomPrefabData prefabData, int prefabIndex, Vector2Int gridPos, Vector3 worldPos)
    {
        GameObject instance = Instantiate(prefabData.prefab, worldPos, Quaternion.identity, transform);
        instance.name = $"{prefabData.roomType}Room_({gridPos.x},{gridPos.y})";

        RoomConnector connector = instance.GetComponent<RoomConnector>();
        if (connector == null)
        {
            Debug.LogError($"[NetworkedLevelGenerator] {prefabData.prefab.name} missing RoomConnector!");
            Destroy(instance);
            return null;
        }

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
            RoomTilemapSetup setup = room.roomInstance.GetComponent<RoomTilemapSetup>();
            if (setup == null)
                setup = room.roomInstance.AddComponent<RoomTilemapSetup>();
            setup.Initialize();

            RoomGrid rg = room.roomInstance.GetComponent<RoomGrid>();
            if (rg == null)
                rg = room.roomInstance.AddComponent<RoomGrid>();

            rg.Initialize(setup.GetWidth(), setup.GetHeight(), setup.GetCellSize(),
                          room.worldPosition, setup.GetGridOffset(), null);
            room.roomGrid = rg;

            LevelGrid.Instance?.RegisterRoomGrid(rg);
        }
    }

    private void InitializeDoors()
    {
        foreach (PlacedRoom room in placedRooms)
        {
            foreach (RoomDoor door in room.roomInstance.GetComponentsInChildren<RoomDoor>())
                door.Initialize(ConvertToOldPlacedRoom(room));
        }
    }

    private void ClearLevel()
    {
        foreach (Transform child in transform)
            Destroy(child.gameObject);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

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

        var entry    = tempConn.GetConnectionPoint(oppDir);
        Vector3 newPos = exit.transform.position - entry.transform.localPosition;
        Vector2Int newGrid = existing.gridPosition + GetDirectionOffset(dir);

        return PlaceRoom(type, newGrid, newPos);
    }

    private void CreateHallway(PlacedRoom a, PlacedRoom b, LevelGenerator.Direction dir)
    {
        roomConnections[(a, dir)]                    = b;
        roomConnections[(b, GetOppositeDirection(dir))] = a;

        if (hallwayPrefab == null) return;

        var exit  = a.connector.GetConnectionPoint(dir);
        var entry = b.connector.GetConnectionPoint(GetOppositeDirection(dir));
        if (exit?.transform == null || entry?.transform == null) return;

        Vector3 pos = (exit.transform.position + entry.transform.position) * 0.5f;
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
            if (room.connector.IsDirectionAvailable(dir))
            {
                Vector2Int check = room.gridPosition + GetDirectionOffset(dir);
                if (!roomGrid.ContainsKey(check))
                    available.Add(dir);
            }
        }
        return available;
    }

    private LevelGenerator.RoomType DetermineRoomType(int current, int target)
    {
        if (current == target - 1) return LevelGenerator.RoomType.End;
        if (UnityEngine.Random.value < specialRoomChance) return LevelGenerator.RoomType.Special;
        return LevelGenerator.RoomType.Normal;
    }

    private RoomPrefabData GetRandomRoomPrefab(LevelGenerator.RoomType type)
    {
        var valid = roomPrefabs.FindAll(p => p.roomType == type);
        if (valid.Count == 0) return null;

        float total = 0f;
        foreach (var p in valid) total += p.spawnWeight;
        float rand = UnityEngine.Random.value * total;
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

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int j = UnityEngine.Random.Range(i, list.Count);
            T tmp = list[i]; list[i] = list[j]; list[j] = tmp;
        }
    }

    public PlacedRoom GetPlacedRoomAt(Vector2Int gridPos)
    {
        roomGrid.TryGetValue(gridPos, out PlacedRoom room);
        return room;
    }

    public List<PlacedRoom> GetAllRooms() => placedRooms;

    public PlacedRoom GetConnectedRoom(PlacedRoom room, LevelGenerator.Direction dir)
    {
        roomConnections.TryGetValue((room, dir), out PlacedRoom connected);
        return connected;
    }

    // ── Converts internal PlacedRoom to LevelGenerator.PlacedRoom for compatibility ─
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
            prefab       = data.prefab,
            roomType     = data.roomType,
            spawnWeight  = data.spawnWeight,
            width        = data.width,
            height       = data.height,
            gridOffset   = data.gridOffset
        };
    }
}
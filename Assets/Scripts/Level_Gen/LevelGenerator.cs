using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps; 

public class LevelGenerator : MonoBehaviour
{
    [System.Serializable]
    public class RoomPrefabData
    {
        public GameObject prefab;
        public RoomType roomType;
        [Range(0f, 1f)] public float spawnWeight = 1f;

        [HideInInspector] public int width = 10;
        [HideInInspector] public int height = 10;
        [HideInInspector] public Vector3 gridOffset = new Vector3(0, 0.1f, 0);
    }

    public enum RoomType { Start, End, Normal, Special }
    public enum Direction { North, South, East, West }

    [Header("Room Prefabs")]
    [SerializeField] private List<RoomPrefabData> roomPrefabs;
    [SerializeField] private GameObject hallwayPrefab;

    [Header("Generation Settings")]
    [SerializeField] private int minRooms = 5;
    [SerializeField] private int maxRooms = 10;
    [SerializeField] private float specialRoomChance = 0.3f;

    [Header("Grid Settings")]
    [SerializeField] private float cellSize = 2f;
    [SerializeField] private Transform gridDebugObjectPrefab;

    [Header("Player Spawn")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private bool spawnPlayerOnGenerate = true;

    private List<PlacedRoom> placedRooms;
    private Dictionary<Vector2Int, PlacedRoom> roomGrid;
    private Dictionary<(PlacedRoom, Direction), PlacedRoom> roomConnections;
    private GameObject spawnedPlayer;

    public static System.Action OnLevelReady;

    public class PlacedRoom
    {
        public GameObject roomInstance;
        public RoomPrefabData prefabData;
        public RoomConnector connector;
        public Vector3 worldPosition;
        public Vector2Int gridPosition;
        public RoomGrid roomGrid;
    }

    private void Start()
    {
        ReadRoomPrefabDefinitions();
        Invoke(nameof(GenerateLevel), 0.1f);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // METHOD 1: ReadRoomPrefabDefinitions() - READS TILEMAP DIMENSIONS
    // ════════════════════════════════════════════════════════════════════════════════
    
    private void ReadRoomPrefabDefinitions()
    {
        foreach (RoomPrefabData data in roomPrefabs)
        {
            if (data.prefab == null) continue;

            // ─────────────────────────────────────────────────────────────────────────
            // 🔄 TILEMAP CHANGE: Check for RoomTilemapSetup instead of RoomGridDefinition
            // ─────────────────────────────────────────────────────────────────────────
            
            RoomTilemapSetup tilemapSetup = data.prefab.GetComponent<RoomTilemapSetup>();
            if (tilemapSetup != null)
            {
                data.width = tilemapSetup.GetWidth();
                data.height = tilemapSetup.GetHeight();
                data.gridOffset = tilemapSetup.GetGridOffset();
                Debug.Log($"[LevelGenerator] Read room '{data.prefab.name}': {data.width}x{data.height}");
            }
            else
            {
                // Fallback if RoomTilemapSetup not found
                data.width = 10;
                data.height = 10;
                data.gridOffset = new Vector3(0, 0.1f, 0);
                Debug.LogWarning($"[LevelGenerator] Room '{data.prefab.name}' has no RoomTilemapSetup! Using defaults.");
            }
        }
    }

    public void GenerateLevel()
    {
        ClearLevel();

        placedRooms = new List<PlacedRoom>();
        roomGrid = new Dictionary<Vector2Int, PlacedRoom>();
        roomConnections = new Dictionary<(PlacedRoom, Direction), PlacedRoom>();

        GenerateRoomLayout();
        InitializeRoomGrids();
        InitializeDoors();

        if (spawnPlayerOnGenerate && playerPrefab != null)
        {
            SpawnPlayer();
        }

        OnLevelReady?.Invoke();
    }

    private void ClearLevel()
    {
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }

        if (spawnedPlayer != null)
        {
            Destroy(spawnedPlayer);
            spawnedPlayer = null;
        }
    }

    private void GenerateRoomLayout()
    {
        PlacedRoom startRoom = PlaceRoom(RoomType.Start, Vector2Int.zero, Vector3.zero);

        if (startRoom == null)
        {
            Debug.LogError("Failed to place start room!");
            return;
        }

        Queue<PlacedRoom> roomsToConnect = new Queue<PlacedRoom>();
        roomsToConnect.Enqueue(startRoom);

        int roomCount = 1;
        int targetRoomCount = Random.Range(minRooms, maxRooms + 1);
        PlacedRoom endRoom = null;
        int attempts = 0;

        while (roomsToConnect.Count > 0 && roomCount < targetRoomCount && attempts < 100)
        {
            attempts++;
            PlacedRoom currentRoom = roomsToConnect.Dequeue();

            List<Direction> availableDirections = GetAvailableDirections(currentRoom);
            if (availableDirections.Count == 0) continue;

            ShuffleList(availableDirections);

            int connectionsToMake = Mathf.Min(Random.Range(1, 3), availableDirections.Count);

            for (int i = 0; i < connectionsToMake && roomCount < targetRoomCount; i++)
            {
                Direction direction = availableDirections[i];
                RoomType roomType = DetermineRoomType(roomCount, targetRoomCount);
                PlacedRoom newRoom = PlaceRoomInDirection(currentRoom, direction, roomType);

                if (newRoom != null)
                {
                    CreateHallway(currentRoom, newRoom, direction);
                    currentRoom.connector.MarkConnectionUsed(direction);
                    newRoom.connector.MarkConnectionUsed(GetOppositeDirection(direction));

                    if (roomType != RoomType.End)
                        roomsToConnect.Enqueue(newRoom);
                    else
                        endRoom = newRoom;

                    roomCount++;
                }
            }
        }

        if (endRoom == null && placedRooms.Count > 1)
        {
            ConvertToEndRoom(placedRooms[placedRooms.Count - 1]);
        }

        Debug.Log($"Generated {placedRooms.Count} rooms");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // METHOD 2: InitializeRoomGrids() - INITIALIZES TILEMAPS
    // ════════════════════════════════════════════════════════════════════════════════
    
    private void InitializeRoomGrids()
    {
        foreach (PlacedRoom room in placedRooms)
        {
            // TILEMAP CHANGE: Initialize tilemap structure and TilemapRoomGrid
            RoomTilemapSetup tilemapSetup = room.roomInstance.GetComponent<RoomTilemapSetup>();
            if (tilemapSetup == null)
            {
                tilemapSetup = room.roomInstance.AddComponent<RoomTilemapSetup>();
            }

            tilemapSetup.Initialize();

            RoomGrid roomGridComponent = room.roomInstance.GetComponent<RoomGrid>();
            if (roomGridComponent == null)
            {
                roomGridComponent = room.roomInstance.AddComponent<RoomGrid>();
            }

            roomGridComponent.Initialize(
                width:          tilemapSetup.GetWidth(),
                height:         tilemapSetup.GetHeight(),
                cellSize:       tilemapSetup.GetCellSize(),
                worldPosition:  room.worldPosition,
                gridOffset:     tilemapSetup.GetGridOffset(),
                debugPrefab:    null  // Tilemap has built-in debug visualization
            );

            
            room.roomGrid = roomGridComponent;

            if (LevelGrid.Instance != null)
            {
                LevelGrid.Instance.RegisterRoomGrid(room.roomGrid);
            }

            Debug.Log($"[LevelGenerator] Room '{room.roomInstance.name}' tilemap initialized");
        }
    }

    private void InitializeDoors()
    {
        foreach (PlacedRoom room in placedRooms)
        {
            RoomDoor[] doors = room.roomInstance.GetComponentsInChildren<RoomDoor>();
            foreach (RoomDoor door in doors)
            {
                door.Initialize(room);
            }
        }
    }

    private void SpawnPlayer()
    {
        PlacedRoom startRoom = placedRooms.Find(r => r.prefabData.roomType == RoomType.Start);

        if (startRoom == null || startRoom.roomGrid == null)
        {
            Debug.LogError("[LevelGenerator] No valid start room found!");
            return;
        }

        // Set current room FIRST so LevelGrid is ready before PlaceInRoom
        if (RoomManager.Instance != null)
            RoomManager.Instance.SetCurrentRoom(startRoom);

        if (LevelGrid.Instance != null)
            LevelGrid.Instance.SetCurrentRoomGrid(startRoom.roomGrid);

        // Try to use a painted spawn point tile
        GridPosition spawnGridPos = GetStartRoomSpawnPosition(startRoom);

        // Instantiate at a temporary position — PlaceInRoom will move them correctly
        spawnedPlayer = Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);
        spawnedPlayer.name = "Player";

        Unit playerUnit = spawnedPlayer.GetComponent<Unit>();
        if (playerUnit != null)
        {
            playerUnit.PlaceInRoom(startRoom.roomGrid, spawnGridPos);
            Debug.Log($"[LevelGenerator] Player spawned at grid {spawnGridPos} " +
                    $"world {startRoom.roomGrid.GetWorldPosition(spawnGridPos)} " +
                    $"in {startRoom.roomInstance.name}");
        }
        else
        {
            Debug.LogError("[LevelGenerator] Player prefab has no Unit component!");
        }

        
    }

    /// <summary>
    /// Gets the spawn position for the start room.
    /// Looks for a SpawnPointTile first — falls back to room center.
    /// Start room has no entry direction so we look for any painted spawn point.
    /// </summary>
    private GridPosition GetStartRoomSpawnPosition(PlacedRoom startRoom)
    {
        RoomSpawnPointReader reader = startRoom.roomInstance.GetComponent<RoomSpawnPointReader>();

        if (reader != null)
        {
            // For the start room, use any available spawn point
            var allSpawnPoints = reader.GetAllSpawnPoints();
            if (allSpawnPoints.Count > 0)
            {
                // Pick the first one — or you could pick a specific direction
                foreach (var kvp in allSpawnPoints)
                {
                    Debug.Log($"[LevelGenerator] Using spawn point (entry: {kvp.Key}) at {kvp.Value}");
                    return kvp.Value;
                }
            }
        }

        // Fallback: room center
        Debug.LogWarning("[LevelGenerator] No spawn points found in start room — using center.");
        int centerX = startRoom.roomGrid.GetWidth() / 2;
        int centerZ = startRoom.roomGrid.GetHeight() / 2;
        return new GridPosition(centerX, centerZ);
    }

    private PlacedRoom PlaceRoom(RoomType roomType, Vector2Int gridPosition, Vector3 worldPosition)
    {
        if (roomGrid.ContainsKey(gridPosition)) return null;

        RoomPrefabData prefabData = GetRandomRoomPrefab(roomType);
        if (prefabData == null) return null;

        GameObject roomInstance = Instantiate(prefabData.prefab, worldPosition, Quaternion.identity, transform);
        roomInstance.name = $"{roomType}Room_({gridPosition.x},{gridPosition.y})";

        RoomConnector connector = roomInstance.GetComponent<RoomConnector>();
        if (connector == null)
        {
            Debug.LogError($"{prefabData.prefab.name} missing RoomConnector!");
            Destroy(roomInstance);
            return null;
        }

        RoomTilemapSetup tilemapSetup = roomInstance.GetComponent<RoomTilemapSetup>();
        if (tilemapSetup != null)
        {
            prefabData.width = tilemapSetup.GetWidth();
            prefabData.height = tilemapSetup.GetHeight();
            prefabData.gridOffset = tilemapSetup.GetGridOffset();
        }

        PlacedRoom placedRoom = new PlacedRoom
        {
            roomInstance = roomInstance,
            prefabData = prefabData,
            connector = connector,
            worldPosition = worldPosition,
            gridPosition = gridPosition
        };

        placedRooms.Add(placedRoom);
        roomGrid.Add(gridPosition, placedRoom);

        return placedRoom;
    }

    private PlacedRoom PlaceRoomInDirection(PlacedRoom existingRoom, Direction direction, RoomType roomType)
    {
        RoomConnector.ConnectionPoint exitPoint = existingRoom.connector.GetConnectionPoint(direction);
        if (exitPoint == null || exitPoint.transform == null) return null;

        RoomPrefabData newRoomPrefab = GetRandomRoomPrefab(roomType);
        if (newRoomPrefab == null) return null;

        RoomConnector tempConnector = newRoomPrefab.prefab.GetComponent<RoomConnector>();
        if (tempConnector == null) return null;

        Direction oppositeDir = GetOppositeDirection(direction);
        if (!tempConnector.HasConnectionPoint(oppositeDir)) return null;

        RoomConnector.ConnectionPoint entryPoint = tempConnector.GetConnectionPoint(oppositeDir);
        Vector3 newRoomWorldPos = exitPoint.transform.position - entryPoint.transform.localPosition;

        Vector2Int newGridPos = existingRoom.gridPosition + GetDirectionOffset(direction);
        return PlaceRoom(roomType, newGridPos, newRoomWorldPos);
    }

    private void CreateHallway(PlacedRoom roomA, PlacedRoom roomB, Direction direction)
    {
        roomConnections[(roomA, direction)] = roomB;
        roomConnections[(roomB, GetOppositeDirection(direction))] = roomA;

        if (hallwayPrefab == null) return;

        RoomConnector.ConnectionPoint exitPoint = roomA.connector.GetConnectionPoint(direction);
        RoomConnector.ConnectionPoint entryPoint = roomB.connector.GetConnectionPoint(GetOppositeDirection(direction));

        if (exitPoint?.transform == null || entryPoint?.transform == null) return;

        Vector3 hallwayPosition = (exitPoint.transform.position + entryPoint.transform.position) / 2f;

        Quaternion rot = (direction == Direction.East || direction == Direction.West)
            ? Quaternion.Euler(0, 90, 0)
            : Quaternion.identity;

        GameObject hallway = Instantiate(hallwayPrefab, hallwayPosition, rot, transform);
        hallway.name = $"Hallway_{roomA.gridPosition}_{direction}";
    }

    private void ConvertToEndRoom(PlacedRoom room)
    {
        RoomPrefabData endPrefab = GetRandomRoomPrefab(RoomType.End);
        if (endPrefab == null) return;

        Vector3 position = room.worldPosition;
        Vector2Int gridPos = room.gridPosition;
        Destroy(room.roomInstance);

        PlacedRoom newRoom = PlaceRoom(RoomType.End, gridPos, position);
        if (newRoom != null)
        {
            int index = placedRooms.IndexOf(room);
            if (index >= 0) placedRooms[index] = newRoom;
        }
    }

    private List<Direction> GetAvailableDirections(PlacedRoom room)
    {
        List<Direction> available = new List<Direction>();
        if (room.connector == null) return available;

        foreach (Direction dir in System.Enum.GetValues(typeof(Direction)))
        {
            if (room.connector.IsDirectionAvailable(dir))
            {
                Vector2Int checkPos = room.gridPosition + GetDirectionOffset(dir);
                if (!roomGrid.ContainsKey(checkPos))
                    available.Add(dir);
            }
        }

        return available;
    }

    private RoomType DetermineRoomType(int currentCount, int targetCount)
    {
        if (currentCount == targetCount - 1) return RoomType.End;
        if (Random.value < specialRoomChance) return RoomType.Special;
        return RoomType.Normal;
    }

    private Vector2Int GetDirectionOffset(Direction direction)
    {
        switch (direction)
        {
            case Direction.North: return new Vector2Int(0, 1);
            case Direction.South: return new Vector2Int(0, -1);
            case Direction.East: return new Vector2Int(1, 0);
            case Direction.West: return new Vector2Int(-1, 0);
            default: return Vector2Int.zero;
        }
    }

    public Direction GetOppositeDirection(Direction dir)
    {
        switch (dir)
        {
            case Direction.North: return Direction.South;
            case Direction.South: return Direction.North;
            case Direction.East: return Direction.West;
            case Direction.West: return Direction.East;
            default: return Direction.North;
        }
    }

    private RoomPrefabData GetRandomRoomPrefab(RoomType roomType)
    {
        List<RoomPrefabData> valid = roomPrefabs.FindAll(p => p.roomType == roomType);
        if (valid.Count == 0) return null;

        float total = 0f;
        foreach (var p in valid) total += p.spawnWeight;

        float rand = Random.value * total;
        float current = 0f;

        foreach (var p in valid)
        {
            current += p.spawnWeight;
            if (rand <= current) return p;
        }

        return valid[0];
    }

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int j = Random.Range(i, list.Count);
            T temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }

    public PlacedRoom GetConnectedRoom(PlacedRoom room, Direction direction)
    {
        roomConnections.TryGetValue((room, direction), out PlacedRoom connected);
        return connected;
    }

    public RoomGrid GetRoomAtWorldPosition(Vector3 worldPosition)
    {
        if (placedRooms == null) return null;
        foreach (PlacedRoom room in placedRooms)
        {
            if (room.roomGrid != null && room.roomGrid.IsPositionInRoom(worldPosition))
                return room.roomGrid;
        }
        return null;
    }

    public List<PlacedRoom> GetAllRooms() => placedRooms;
    public float GetCellSize() => cellSize;

    [ContextMenu("Regenerate Level")]
    public void RegenerateLevel()
    {
        ReadRoomPrefabDefinitions();
        GenerateLevel();
    }
}
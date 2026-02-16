using System.Collections.Generic;
using UnityEngine;

public class LevelGenerator : MonoBehaviour
{
    [System.Serializable]
    public class RoomPrefabData
    {
        public GameObject prefab;
        public RoomType roomType;
        public int width;  // in grid cells
        public int height; // in grid cells
        [Range(0f, 1f)] public float spawnWeight = 1f;
    }

    public enum RoomType
    {
        Start,
        End,
        Normal,
        Special
    }

    public enum Direction
    {
        North,
        South,
        East,
        West
    }

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

    // Event fired when level generation is complete
    public static System.Action OnLevelReady;

    public class PlacedRoom
    {
        public GameObject roomInstance;
        public RoomPrefabData prefabData;
        public RoomConnector connector;
        public Vector3 worldPosition;
        public Vector2Int gridPosition;
        public RoomGrid roomGrid;

        public PlacedRoom() { }
    }

    private void Start()
    {
        Invoke(nameof(GenerateLevel), 0.1f);
    }

    public void GenerateLevel()
    {
        // Clear any existing level
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }

        // Destroy previously spawned player if regenerating
        if (spawnedPlayer != null)
        {
            Destroy(spawnedPlayer);
            spawnedPlayer = null;
        }

        placedRooms = new List<PlacedRoom>();
        roomGrid = new Dictionary<Vector2Int, PlacedRoom>();
        roomConnections = new Dictionary<(PlacedRoom, Direction), PlacedRoom>();

        GenerateRoomLayout();
        InitializeRoomGrids();
        InitializeDoors();

        // Spawn player after level is ready
        if (spawnPlayerOnGenerate && playerPrefab != null)
        {
            SpawnPlayer();
        }

        // Notify all systems that level is ready
        Debug.Log("🎉 LEVEL GENERATION COMPLETE - Firing OnLevelReady event");
        OnLevelReady?.Invoke();
    }

    private void GenerateRoomLayout()
    {
        // Place starting room at origin
        Vector2Int startGridPos = Vector2Int.zero;
        PlacedRoom startRoom = PlaceRoom(RoomType.Start, startGridPos, Vector3.zero);
        
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

        int maxAttempts = 100;
        int attempts = 0;

        while (roomsToConnect.Count > 0 && roomCount < targetRoomCount && attempts < maxAttempts)
        {
            attempts++;
            PlacedRoom currentRoom = roomsToConnect.Dequeue();
            
            // Get available directions from this room
            List<Direction> availableDirections = GetAvailableDirections(currentRoom);
            
            if (availableDirections.Count == 0)
                continue;

            // Shuffle directions
            ShuffleList(availableDirections);

            // Try to connect 1-2 new rooms
            int connectionsToMake = Mathf.Min(Random.Range(1, 3), availableDirections.Count);
            
            for (int i = 0; i < connectionsToMake && roomCount < targetRoomCount; i++)
            {
                Direction direction = availableDirections[i];

                RoomType roomType = DetermineRoomType(roomCount, targetRoomCount);
                PlacedRoom newRoom = PlaceRoomInDirection(currentRoom, direction, roomType);
                
                if (newRoom != null)
                {
                    CreateHallway(currentRoom, newRoom, direction);
                    
                    // Mark connections as used
                    currentRoom.connector.MarkConnectionUsed(direction);
                    newRoom.connector.MarkConnectionUsed(GetOppositeDirection(direction));
                    
                    if (roomType != RoomType.End)
                    {
                        roomsToConnect.Enqueue(newRoom);
                    }
                    else
                    {
                        endRoom = newRoom;
                    }
                    
                    roomCount++;
                }
            }
        }

        // Ensure we have an end room
        if (endRoom == null && placedRooms.Count > 1)
        {
            PlacedRoom lastRoom = placedRooms[placedRooms.Count - 1];
            ConvertToEndRoom(lastRoom);
        }

        Debug.Log($"Generated {placedRooms.Count} rooms in {attempts} attempts");
    }

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int randomIndex = Random.Range(i, list.Count);
            T temp = list[i];
            list[i] = list[randomIndex];
            list[randomIndex] = temp;
        }
    }

    private List<Direction> GetAvailableDirections(PlacedRoom room)
    {
        List<Direction> available = new List<Direction>();
        
        if (room.connector == null)
        {
            Debug.LogWarning($"Room {room.roomInstance.name} has no RoomConnector component!");
            return available;
        }

        foreach (Direction dir in System.Enum.GetValues(typeof(Direction)))
        {
            // Check if room has this connection point and it's not used
            if (room.connector.IsDirectionAvailable(dir))
            {
                // Check if there's already a room in that grid position
                Vector2Int offset = GetDirectionOffset(dir);
                Vector2Int checkPos = room.gridPosition + offset;
                
                if (!roomGrid.ContainsKey(checkPos))
                {
                    available.Add(dir);
                }
            }
        }
        
        return available;
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

    private RoomType DetermineRoomType(int currentCount, int targetCount)
    {
        if (currentCount == targetCount - 1)
        {
            return RoomType.End;
        }
        
        if (Random.value < specialRoomChance)
        {
            return RoomType.Special;
        }
        
        return RoomType.Normal;
    }

    private PlacedRoom PlaceRoom(RoomType roomType, Vector2Int gridPosition, Vector3 worldPosition)
    {
        // Check if position is already occupied
        if (roomGrid.ContainsKey(gridPosition))
        {
            return null;
        }

        RoomPrefabData prefabData = GetRandomRoomPrefab(roomType);
        if (prefabData == null) return null;

        GameObject roomInstance = Instantiate(prefabData.prefab, worldPosition, Quaternion.identity, transform);
        roomInstance.name = $"{roomType}Room_({gridPosition.x},{gridPosition.y})";

        // Get the RoomConnector component
        RoomConnector connector = roomInstance.GetComponent<RoomConnector>();
        if (connector == null)
        {
            Debug.LogError($"Room prefab {prefabData.prefab.name} is missing RoomConnector component!");
            Destroy(roomInstance);
            return null;
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
        // Get the connection point from the existing room
        RoomConnector.ConnectionPoint exitPoint = existingRoom.connector.GetConnectionPoint(direction);
        if (exitPoint == null || exitPoint.transform == null)
        {
            Debug.LogWarning($"No connection point found for direction {direction} on room {existingRoom.roomInstance.name}");
            return null;
        }

        // Get a random prefab of the desired type
        RoomPrefabData newRoomPrefab = GetRandomRoomPrefab(roomType);
        if (newRoomPrefab == null) return null;

        // Check if new room has the opposite connection point
        RoomConnector tempConnector = newRoomPrefab.prefab.GetComponent<RoomConnector>();
        if (tempConnector == null)
        {
            Debug.LogError($"Room prefab {newRoomPrefab.prefab.name} is missing RoomConnector component!");
            return null;
        }

        Direction oppositeDir = GetOppositeDirection(direction);
        if (!tempConnector.HasConnectionPoint(oppositeDir))
        {
            Debug.LogWarning($"Room prefab {newRoomPrefab.prefab.name} has no {oppositeDir} connection point!");
            return null;
        }

        // Calculate world position for new room
        // We want the new room's entrance to align with the exit point
        RoomConnector.ConnectionPoint entryPoint = tempConnector.GetConnectionPoint(oppositeDir);
        
        // Calculate offset from new room's pivot to its entry point
        Vector3 entryLocalPos = entryPoint.transform.localPosition;
        
        // New room position = exit point position - entry point local position
        Vector3 newRoomWorldPos = exitPoint.transform.position - entryLocalPos;

        // Calculate grid position
        Vector2Int offset = GetDirectionOffset(direction);
        Vector2Int newGridPos = existingRoom.gridPosition + offset;

        return PlaceRoom(roomType, newGridPos, newRoomWorldPos);
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

    private void CreateHallway(PlacedRoom roomA, PlacedRoom roomB, Direction direction)
    {
        // Track the connection between rooms
        roomConnections[(roomA, direction)] = roomB;
        roomConnections[(roomB, GetOppositeDirection(direction))] = roomA;

        if (hallwayPrefab == null)
        {
            return; // Hallways are optional
        }

        // Get connection points
        RoomConnector.ConnectionPoint exitPoint = roomA.connector.GetConnectionPoint(direction);
        RoomConnector.ConnectionPoint entryPoint = roomB.connector.GetConnectionPoint(GetOppositeDirection(direction));

        if (exitPoint == null || exitPoint.transform == null || entryPoint == null || entryPoint.transform == null)
        {
            return;
        }

        // Position hallway between the two connection points
        Vector3 hallwayPosition = (exitPoint.transform.position + entryPoint.transform.position) / 2f;
        
        // Determine rotation based on direction
        Quaternion hallwayRotation = Quaternion.identity;
        switch (direction)
        {
            case Direction.North:
            case Direction.South:
                hallwayRotation = Quaternion.Euler(0, 0, 0); // Z-axis aligned
                break;
            case Direction.East:
            case Direction.West:
                hallwayRotation = Quaternion.Euler(0, 90, 0); // X-axis aligned
                break;
        }

        GameObject hallway = Instantiate(hallwayPrefab, hallwayPosition, hallwayRotation, transform);
        hallway.name = $"Hallway_{roomA.gridPosition}_{direction}";
    }

    private void InitializeRoomGrids()
    {
        foreach (PlacedRoom room in placedRooms)
        {
            RoomGrid roomGridComponent = room.roomInstance.GetComponent<RoomGrid>();
            if (roomGridComponent == null)
            {
                roomGridComponent = room.roomInstance.AddComponent<RoomGrid>();
            }

            roomGridComponent.Initialize(
                room.prefabData.width,
                room.prefabData.height,
                cellSize,
                room.worldPosition,
                gridDebugObjectPrefab
            );

            room.roomGrid = roomGridComponent;
        }
    }

    private void InitializeDoors()
    {
        foreach (PlacedRoom room in placedRooms)
        {
            // Find all RoomDoor components in this room
            RoomDoor[] doors = room.roomInstance.GetComponentsInChildren<RoomDoor>();
            
            foreach (RoomDoor door in doors)
            {
                door.Initialize(room);
            }

            Debug.Log($"Initialized {doors.Length} doors in {room.roomInstance.name}");
        }
    }

    private void SpawnPlayer()
    {
        Debug.Log("=== SPAWN PLAYER CALLED ===");
        
        if (playerPrefab == null)
        {
            Debug.LogError("❌ No player prefab assigned to LevelGenerator!");
            return;
        }

        if (placedRooms == null || placedRooms.Count == 0)
        {
            Debug.LogError("❌ Cannot spawn player - no rooms generated!");
            return;
        }

        Debug.Log($"Total rooms generated: {placedRooms.Count}");
        
        // Debug: Show all rooms and their types
        for (int i = 0; i < placedRooms.Count; i++)
        {
            Debug.Log($"Room {i}: {placedRooms[i].roomInstance.name}, Type: {placedRooms[i].prefabData.roomType}");
        }

        // Find the start room
        PlacedRoom startRoom = placedRooms.Find(r => r.prefabData.roomType == RoomType.Start);
        
        if (startRoom == null)
        {
            Debug.LogError("❌ NO START ROOM FOUND!");
            Debug.LogError("Make sure you have a room prefab with RoomType set to 'Start' in the Inspector!");
            return;
        }

        Debug.Log($"✓ Found start room: {startRoom.roomInstance.name}");

        if (startRoom.roomGrid == null)
        {
            Debug.LogError("❌ Start room has no grid!");
            return;
        }

        Debug.Log($"✓ Start room grid exists: {startRoom.prefabData.width}x{startRoom.prefabData.height}");

        // Spawn player at center of start room
        int centerX = startRoom.prefabData.width / 2;
        int centerZ = startRoom.prefabData.height / 2;
        GridPosition spawnGridPos = new GridPosition(centerX, centerZ);
        Vector3 spawnWorldPos = startRoom.roomGrid.GetWorldPosition(spawnGridPos);

        Debug.Log($"Spawning at grid position: {spawnGridPos}, world position: {spawnWorldPos}");

        spawnedPlayer = Instantiate(playerPrefab, spawnWorldPos, Quaternion.identity);
        spawnedPlayer.name = "Player";

        // Set the start room as current room in RoomManager
        if (RoomManager.Instance != null)
        {
            RoomManager.Instance.SetCurrentRoom(startRoom);
            Debug.Log($"✓ Set current room in RoomManager: {startRoom.roomInstance.name}");
        }
        else
        {
            Debug.LogError("❌ RoomManager.Instance is NULL! Make sure you have a RoomManager in the scene!");
        }

        Debug.Log($"✓✓✓ Player spawned successfully at {spawnWorldPos} ✓✓✓");
    }

    private RoomPrefabData GetRandomRoomPrefab(RoomType roomType)
    {
        List<RoomPrefabData> validPrefabs = roomPrefabs.FindAll(p => p.roomType == roomType);
        
        if (validPrefabs.Count == 0)
        {
            Debug.LogWarning($"No prefabs found for room type: {roomType}");
            return null;
        }

        float totalWeight = 0f;
        foreach (RoomPrefabData prefab in validPrefabs)
        {
            totalWeight += prefab.spawnWeight;
        }

        float randomValue = Random.value * totalWeight;
        float currentWeight = 0f;

        foreach (RoomPrefabData prefab in validPrefabs)
        {
            currentWeight += prefab.spawnWeight;
            if (randomValue <= currentWeight)
            {
                return prefab;
            }
        }

        return validPrefabs[0];
    }

    private void ConvertToEndRoom(PlacedRoom room)
    {
        RoomPrefabData endPrefab = GetRandomRoomPrefab(RoomType.End);
        if (endPrefab != null)
        {
            Vector3 position = room.worldPosition;
            Vector2Int gridPos = room.gridPosition;
            Destroy(room.roomInstance);
            
            PlacedRoom newRoom = PlaceRoom(RoomType.End, gridPos, position);
            if (newRoom != null)
            {
                // Update the reference in the list
                int index = placedRooms.IndexOf(room);
                if (index >= 0)
                {
                    placedRooms[index] = newRoom;
                }
            }
        }
    }

    public PlacedRoom GetConnectedRoom(PlacedRoom room, Direction direction)
    {
        if (roomConnections.TryGetValue((room, direction), out PlacedRoom connected))
        {
            return connected;
        }
        return null;
    }

    public RoomGrid GetRoomAtWorldPosition(Vector3 worldPosition)
    {
        if (placedRooms == null) return null;

        foreach (PlacedRoom room in placedRooms)
        {
            if (room.roomGrid != null && room.roomGrid.IsPositionInRoom(worldPosition))
            {
                return room.roomGrid;
            }
        }
        return null;
    }

    public List<PlacedRoom> GetAllRooms()
    {
        return placedRooms;
    }

    [ContextMenu("Regenerate Level")]
    public void RegenerateLevel()
    {
        GenerateLevel();
    }
}
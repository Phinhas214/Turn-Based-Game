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

    [Header("Room Prefabs")]
    [SerializeField] private List<RoomPrefabData> roomPrefabs;
    [SerializeField] private GameObject hallwayPrefab;
    
    [Header("Generation Settings")]
    [SerializeField] private int minRooms = 5;
    [SerializeField] private int maxRooms = 10;
    [SerializeField] private float specialRoomChance = 0.3f;
    
    [Header("Grid Settings")]
    [SerializeField] private float cellSize = 2f;
    [SerializeField] private int hallwayWidth = 2;
    [SerializeField] private Transform gridDebugObjectPrefab;

    private List<PlacedRoom> placedRooms;
    private HashSet<GridPosition> occupiedPositions;

    public class PlacedRoom
    {
        public GameObject roomInstance;
        public RoomPrefabData prefabData;
        public Vector3 worldPosition;
        public GridPosition masterGridPosition;
        public RoomGrid roomGrid;
        public List<GridPosition> doorPositions;

        public PlacedRoom()
        {
            doorPositions = new List<GridPosition>();
        }
    }

    private void Start()
    {
        Invoke(nameof(GenerateLevel), 0.1f);
    }

    public void GenerateLevel()
    {
        placedRooms = new List<PlacedRoom>();
        occupiedPositions = new HashSet<GridPosition>();

        GenerateRoomLayout();
        InitializeRoomGrids();
    }

    private void GenerateRoomLayout()
    {
        GridPosition startPosition = new GridPosition(0, 0);
        PlacedRoom startRoom = PlaceRoom(RoomType.Start, startPosition);
        
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

        while (roomsToConnect.Count > 0 && roomCount < targetRoomCount)
        {
            PlacedRoom currentRoom = roomsToConnect.Dequeue();
            int connectionsToMake = Random.Range(1, 4);
            
            for (int i = 0; i < connectionsToMake && roomCount < targetRoomCount; i++)
            {
                RoomType roomType = DetermineRoomType(roomCount, targetRoomCount);
                PlacedRoom newRoom = PlaceRoomNear(currentRoom, roomType);
                
                if (newRoom != null)
                {
                    CreateHallway(currentRoom, newRoom);
                    
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

        if (endRoom == null && placedRooms.Count > 1)
        {
            PlacedRoom lastRoom = placedRooms[placedRooms.Count - 1];
            ConvertToEndRoom(lastRoom);
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

    private PlacedRoom PlaceRoom(RoomType roomType, GridPosition gridPosition)
    {
        RoomPrefabData prefabData = GetRandomRoomPrefab(roomType);
        if (prefabData == null) return null;

        if (!IsSpaceAvailable(gridPosition, prefabData.width, prefabData.height))
        {
            return null;
        }

        Vector3 worldPosition = new Vector3(
            gridPosition.x * cellSize * prefabData.width,
            0,
            gridPosition.z * cellSize * prefabData.height
        );

        GameObject roomInstance = Instantiate(prefabData.prefab, worldPosition, Quaternion.identity, transform);

        PlacedRoom placedRoom = new PlacedRoom
        {
            roomInstance = roomInstance,
            prefabData = prefabData,
            worldPosition = worldPosition,
            masterGridPosition = gridPosition
        };

        placedRooms.Add(placedRoom);
        MarkSpaceOccupied(gridPosition, prefabData.width, prefabData.height);
        
        return placedRoom;
    }

    private PlacedRoom PlaceRoomNear(PlacedRoom existingRoom, RoomType roomType)
    {
        Vector2Int[] directions = new Vector2Int[]
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1)
        };

        for (int i = 0; i < directions.Length; i++)
        {
            int randomIndex = Random.Range(i, directions.Length);
            Vector2Int temp = directions[i];
            directions[i] = directions[randomIndex];
            directions[randomIndex] = temp;
        }

        foreach (Vector2Int dir in directions)
        {
            int offsetDistance = 1 + (hallwayWidth / Mathf.Max(existingRoom.prefabData.width, 1));
            
            GridPosition newPosition = new GridPosition(
                existingRoom.masterGridPosition.x + (dir.x * offsetDistance),
                existingRoom.masterGridPosition.z + (dir.y * offsetDistance)
            );

            for (int attempt = 0; attempt < 3; attempt++)
            {
                GridPosition attemptPosition = new GridPosition(
                    newPosition.x + (Random.Range(-1, 2) * attempt),
                    newPosition.z + (Random.Range(-1, 2) * attempt)
                );

                PlacedRoom newRoom = PlaceRoom(roomType, attemptPosition);
                if (newRoom != null)
                {
                    return newRoom;
                }
            }
        }

        return null;
    }

    private void CreateHallway(PlacedRoom roomA, PlacedRoom roomB)
    {
        Vector3 startPos = roomA.worldPosition;
        Vector3 endPos = roomB.worldPosition;

        Vector3 roomACenter = startPos + new Vector3(
            roomA.prefabData.width * cellSize / 2f,
            0,
            roomA.prefabData.height * cellSize / 2f
        );

        Vector3 roomBCenter = endPos + new Vector3(
            roomB.prefabData.width * cellSize / 2f,
            0,
            roomB.prefabData.height * cellSize / 2f
        );

        Vector3 cornerPos = new Vector3(roomACenter.x, 0, roomBCenter.z);

        if (Mathf.Abs(roomACenter.x - cornerPos.x) > 0.1f)
        {
            GameObject hallway1 = Instantiate(hallwayPrefab, 
                (roomACenter + cornerPos) / 2f, 
                Quaternion.identity, 
                transform);
            
            float distance1 = Vector3.Distance(roomACenter, cornerPos);
            hallway1.transform.localScale = new Vector3(distance1, 1, hallwayWidth * cellSize);
        }

        if (Mathf.Abs(cornerPos.z - roomBCenter.z) > 0.1f)
        {
            GameObject hallway2 = Instantiate(hallwayPrefab, 
                (cornerPos + roomBCenter) / 2f, 
                Quaternion.identity, 
                transform);

            float distance2 = Vector3.Distance(cornerPos, roomBCenter);
            hallway2.transform.localScale = new Vector3(hallwayWidth * cellSize, 1, distance2);
        }
    }

    private void InitializeRoomGrids()
    {
        foreach (PlacedRoom room in placedRooms)
        {
            RoomGrid roomGrid = room.roomInstance.GetComponent<RoomGrid>();
            if (roomGrid == null)
            {
                roomGrid = room.roomInstance.AddComponent<RoomGrid>();
            }

            roomGrid.Initialize(
                room.prefabData.width,
                room.prefabData.height,
                cellSize,
                room.worldPosition,
                gridDebugObjectPrefab
            );

            room.roomGrid = roomGrid;
        }
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

    private bool IsSpaceAvailable(GridPosition gridPosition, int width, int height)
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                GridPosition checkPos = new GridPosition(
                    gridPosition.x + x,
                    gridPosition.z + z
                );
                
                if (occupiedPositions.Contains(checkPos))
                {
                    return false;
                }
            }
        }
        return true;
    }

    private void MarkSpaceOccupied(GridPosition gridPosition, int width, int height)
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                GridPosition checkPos = new GridPosition(
                    gridPosition.x + x,
                    gridPosition.z + z
                );
                occupiedPositions.Add(checkPos);
            }
        }
    }

    private void ConvertToEndRoom(PlacedRoom room)
    {
        RoomPrefabData endPrefab = GetRandomRoomPrefab(RoomType.End);
        if (endPrefab != null)
        {
            Vector3 position = room.worldPosition;
            Destroy(room.roomInstance);
            room.roomInstance = Instantiate(endPrefab.prefab, position, Quaternion.identity, transform);
            room.prefabData = endPrefab;
        }
    }

    public RoomGrid GetRoomAtWorldPosition(Vector3 worldPosition)
    {
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
}
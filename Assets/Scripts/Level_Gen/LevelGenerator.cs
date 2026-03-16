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

    public enum RoomType  { Start, End, Normal, Special, Boss }
    public enum Direction { North, South, East, West }

    [Header("Room Prefabs")]
    [SerializeField] private List<RoomPrefabData> roomPrefabs;
    [SerializeField] private GameObject hallwayPrefab;

    [Header("Generation Settings")]
    [SerializeField] private int minRooms = 5;
    [SerializeField] private int maxRooms = 10;
    [SerializeField] private float specialRoomChance = 0.3f;
    [SerializeField] private float roomSpacing = 0f;

    [Header("Boss Room Settings")]
    [Tooltip("If true, a boss room will always be placed before the end room.")]
    [SerializeField] private bool spawnBossRoom = true;

    [Header("Grid Settings")]
    [SerializeField] private float cellSize = 2f;
    [SerializeField] private Transform gridDebugObjectPrefab;

    [Header("Player Spawn")]
    [Tooltip("Index matches character select: 0=SmokeStack, 1=Sconstance, 2=Wip, 3=Wip-2")]
    [SerializeField] private List<GameObject> playerPrefabs;
    [SerializeField] private bool spawnPlayerOnGenerate = true;

    private List<PlacedRoom> placedRooms;
    private Dictionary<Vector2Int, PlacedRoom> roomGrid;
    private Dictionary<(PlacedRoom, Direction), PlacedRoom> roomConnections;
    private GameObject spawnedPlayer;

    public static System.Action OnLevelReady;

    public class PlacedRoom
    {
        public GameObject     roomInstance;
        public RoomPrefabData prefabData;
        public RoomConnector  connector;
        public Vector3        worldPosition;
        public Vector2Int     gridPosition;
        public RoomGrid       roomGrid;
    }

    private void Start()
    {
        ReadRoomPrefabDefinitions();
        Invoke(nameof(GenerateLevel), 0.1f);
    }

    private void ReadRoomPrefabDefinitions()
    {
        foreach (RoomPrefabData data in roomPrefabs)
        {
            if (data.prefab == null) continue;
            RoomTilemapSetup tilemapSetup = data.prefab.GetComponent<RoomTilemapSetup>();
            if (tilemapSetup != null)
            {
                data.width      = tilemapSetup.GetWidth();
                data.height     = tilemapSetup.GetHeight();
                data.gridOffset = tilemapSetup.GetGridOffset();
            }
            else
            {
                data.width      = 10;
                data.height     = 10;
                data.gridOffset = new Vector3(0, 0.1f, 0);
                Debug.LogWarning($"[LevelGenerator] Room '{data.prefab.name}' has no RoomTilemapSetup! Using defaults.");
            }
        }
    }

    public void GenerateLevel()
    {
        ClearLevel();

        placedRooms     = new List<PlacedRoom>();
        roomGrid        = new Dictionary<Vector2Int, PlacedRoom>();
        roomConnections = new Dictionary<(PlacedRoom, Direction), PlacedRoom>();

        GenerateRoomLayout();
        ConfigureRoomDoors();
        InitializeRoomGrids();
        InitializeDoors();

        PlacedRoom startRoom = placedRooms.Find(r => r.prefabData.roomType == RoomType.Start
                                                  && r.roomGrid != null
                                                  && r.roomGrid.IsInitialized());
        if (startRoom == null)
        {
            Debug.LogError("[LevelGenerator] Generation failed — no valid start room. Retrying...");
            GenerateLevel();
            return;
        }

        if (spawnPlayerOnGenerate && playerPrefabs != null && playerPrefabs.Count > 0)
            SpawnPlayer(startRoom);

        Debug.Log("[LevelGenerator] Level generation complete. Firing OnLevelReady.");
        OnLevelReady?.Invoke();
    }

    private void ClearLevel()
    {
        // ── Destroy all room GameObjects ───────────────────────────────────
        foreach (Transform child in transform)
            Destroy(child.gameObject);

        // ── Destroy the player ─────────────────────────────────────────────
        if (spawnedPlayer != null)
        {
            Destroy(spawnedPlayer);
            spawnedPlayer = null;
        }

        // ── Clear LevelGrid AFTER destroying rooms, right before rebuilding ─
        // This is the correct time — rooms are gone, new ones are about to be
        // created and registered. Calling this too early (e.g. from MainMenu)
        // causes EnemySpawner to find no valid rooms when OnLevelReady fires.
        if (LevelGrid.Instance != null)
            LevelGrid.Instance.ClearAllRoomGrids();

        // ── Clear enemies ──────────────────────────────────────────────────
        if (EnemyManager.Instance != null)
            EnemyManager.Instance.ClearAllEnemies();

        // ── Reset room manager ─────────────────────────────────────────────
        if (RoomManager.Instance != null)
            RoomManager.Instance.ClearCurrentRoom();

        Debug.Log("[LevelGenerator] ClearLevel complete.");
    }

    // ── Room layout ────────────────────────────────────────────────────────

    private void GenerateRoomLayout()
    {
        if (GetRandomRoomPrefab(RoomType.End) == null)
        {
            Debug.LogError("[LevelGenerator] No End room prefab assigned!");
            return;
        }

        PlacedRoom startRoom = PlaceRoom(RoomType.Start, Vector2Int.zero, Vector3.zero);
        if (startRoom == null) { Debug.LogError("Failed to place start room!"); return; }

        Queue<PlacedRoom> roomsToConnect = new Queue<PlacedRoom>();
        roomsToConnect.Enqueue(startRoom);

        int        roomCount       = 1;
        int        scaledMin       = WaveManager.Instance != null ? WaveManager.Instance.GetMinRooms() : minRooms;
        int        scaledMax       = WaveManager.Instance != null ? WaveManager.Instance.GetMaxRooms() : maxRooms;
        int        targetRoomCount = Random.Range(scaledMin, scaledMax + 1);
        PlacedRoom bossRoom        = null;
        PlacedRoom lastPlacedRoom  = startRoom;
        int        attempts        = 0;

        Debug.Log($"[LevelGenerator] Level {WaveManager.Instance?.CurrentLevel ?? 1} — targeting {targetRoomCount} rooms.");

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
                Direction  direction = availableDirections[i];
                RoomType   roomType  = DetermineRoomType(roomCount, targetRoomCount, bossRoom != null);
                PlacedRoom newRoom   = PlaceRoomInDirection(currentRoom, direction, roomType);

                if (newRoom == null) continue;

                ConnectRooms(currentRoom, newRoom, direction);
                lastPlacedRoom = newRoom;

                if (roomType == RoomType.Boss)
                {
                    bossRoom = newRoom;
                    roomsToConnect.Enqueue(newRoom);
                }
                else if (roomType != RoomType.End)
                {
                    roomsToConnect.Enqueue(newRoom);
                }

                roomCount++;
            }
        }

        bool hasEnd = placedRooms.Exists(r => r.prefabData.roomType == RoomType.End);
        if (!hasEnd)
        {
            PlacedRoom toConvert = lastPlacedRoom?.prefabData.roomType != RoomType.Start
                ? lastPlacedRoom
                : placedRooms.FindLast(r =>
                    r.prefabData.roomType == RoomType.Normal ||
                    r.prefabData.roomType == RoomType.Special);

            if (toConvert != null)
            {
                Debug.Log($"[LevelGenerator] Converting '{toConvert.roomInstance.name}' to End room.");
                ForceConvertToEndRoom(toConvert);
            }
            else
            {
                Debug.LogWarning("[LevelGenerator] Appending new end room.");
                ForceAppendEndRoom(lastPlacedRoom ?? startRoom);
            }
        }

        bool finalCheck = placedRooms.Exists(r => r.prefabData.roomType == RoomType.End);
        Debug.Log($"[LevelGenerator] Generated {placedRooms.Count} rooms. Boss={bossRoom != null} EndRoom={finalCheck}");

        if (!finalCheck)
            Debug.LogError("[LevelGenerator] END ROOM STILL MISSING!");
    }

    private void ForceConvertToEndRoom(PlacedRoom room)
    {
        RoomPrefabData endPrefab = GetRandomRoomPrefab(RoomType.End);
        if (endPrefab == null) { Debug.LogError("[LevelGenerator] No End prefab!"); return; }

        Vector3    position  = room.worldPosition;
        Vector2Int gridPos   = room.gridPosition;
        int        listIndex = placedRooms.IndexOf(room);

        var inbound  = new List<((PlacedRoom from, Direction dir), PlacedRoom to)>();
        var outbound = new List<((PlacedRoom from, Direction dir), PlacedRoom to)>();

        foreach (var kvp in roomConnections)
        {
            if (kvp.Key.Item1 == room) outbound.Add((kvp.Key, kvp.Value));
            if (kvp.Value     == room) inbound.Add((kvp.Key, kvp.Value));
        }

        placedRooms.Remove(room);
        roomGrid.Remove(gridPos);
        foreach (var entry in outbound) roomConnections.Remove(entry.Item1);
        foreach (var entry in inbound)  roomConnections.Remove(entry.Item1);

        if (room.roomInstance != null)
            Destroy(room.roomInstance);

        PlacedRoom newRoom = PlaceRoom(RoomType.End, gridPos, position);
        if (newRoom == null) { Debug.LogError("[LevelGenerator] ForceConvertToEndRoom failed!"); return; }

        placedRooms.Remove(newRoom);
        int insertAt = Mathf.Clamp(listIndex, 0, placedRooms.Count);
        placedRooms.Insert(insertAt, newRoom);

        foreach (var entry in inbound)
            roomConnections[entry.Item1] = newRoom;
        foreach (var entry in outbound)
            roomConnections[(newRoom, entry.Item1.Item2)] = entry.Item2;

        Debug.Log($"[LevelGenerator] Converted room at {gridPos} to End room.");
    }

    private void ForceAppendEndRoom(PlacedRoom fromRoom)
    {
        List<Direction> dirs = GetAvailableDirections(fromRoom);
        if (dirs.Count == 0)
            dirs = new List<Direction> { Direction.North, Direction.South, Direction.East, Direction.West };

        foreach (Direction dir in dirs)
        {
            PlacedRoom newRoom = PlaceRoomInDirection(fromRoom, dir, RoomType.End);
            if (newRoom != null)
            {
                ConnectRooms(fromRoom, newRoom, dir);
                Debug.Log($"[LevelGenerator] Appended End room going {dir}.");
                return;
            }
        }

        Debug.LogError("[LevelGenerator] ForceAppendEndRoom failed!");
    }

    private void ConnectRooms(PlacedRoom roomA, PlacedRoom roomB, Direction direction)
    {
        roomConnections[(roomA, direction)]                       = roomB;
        roomConnections[(roomB, GetOppositeDirection(direction))] = roomA;

        roomA.connector.MarkConnectionUsed(direction);
        roomB.connector.MarkConnectionUsed(GetOppositeDirection(direction));

        CreateHallway(roomA, roomB, direction);
    }

    // ── Door strip configuration ───────────────────────────────────────────

    private void ConfigureRoomDoors()
    {
        foreach (PlacedRoom room in placedRooms)
        {
            RoomConnector connector = room.connector;
            if (connector == null) continue;

            connector.CloseAllDoors();

            bool isBossRoom = room.prefabData.roomType == RoomType.Boss;

            foreach (Direction dir in System.Enum.GetValues(typeof(Direction)))
            {
                if (!roomConnections.ContainsKey((room, dir))) continue;

                if (isBossRoom)
                {
                    PlacedRoom neighbour = roomConnections[(room, dir)];
                    bool       isExit   = neighbour.prefabData.roomType == RoomType.End;

                    if (isExit)
                    {
                        GameObject strip = GetStripObject(connector, dir);
                        if (strip != null)
                        {
                            BossRoomDoor brd = strip.GetComponent<BossRoomDoor>();
                            if (brd == null) brd = strip.AddComponent<BossRoomDoor>();
                            brd.Initialize(room.roomGrid);
                        }
                    }
                    else
                    {
                        connector.SetDoorOpen(dir, true);
                    }
                }
                else
                {
                    connector.SetDoorOpen(dir, true);
                }
            }
        }
    }

    private GameObject GetStripObject(RoomConnector connector, Direction dir)
    {
        switch (dir)
        {
            case Direction.North: return connector.northDoorStrip;
            case Direction.South: return connector.southDoorStrip;
            case Direction.East:  return connector.eastDoorStrip;
            case Direction.West:  return connector.westDoorStrip;
            default:              return null;
        }
    }

    // ── Room grids & doors ─────────────────────────────────────────────────

    private void InitializeRoomGrids()
    {
        foreach (PlacedRoom room in placedRooms)
        {
            RoomTilemapSetup tilemapSetup = room.roomInstance.GetComponent<RoomTilemapSetup>();
            if (tilemapSetup == null)
                tilemapSetup = room.roomInstance.AddComponent<RoomTilemapSetup>();

            tilemapSetup.Initialize();

            RoomGrid roomGridComponent = room.roomInstance.GetComponent<RoomGrid>();
            if (roomGridComponent == null)
                roomGridComponent = room.roomInstance.AddComponent<RoomGrid>();

            roomGridComponent.Initialize(
                width:         tilemapSetup.GetWidth(),
                height:        tilemapSetup.GetHeight(),
                cellSize:      tilemapSetup.GetCellSize(),
                worldPosition: room.worldPosition,
                gridOffset:    tilemapSetup.GetGridOffset(),
                debugPrefab:   null
            );

            room.roomGrid = roomGridComponent;

            if (LevelGrid.Instance != null)
                LevelGrid.Instance.RegisterRoomGrid(room.roomGrid);
        }
    }

    private void InitializeDoors()
    {
        foreach (PlacedRoom room in placedRooms)
        {
            RoomDoor[] doors = room.roomInstance.GetComponentsInChildren<RoomDoor>();
            foreach (RoomDoor door in doors)
                door.Initialize(room);
        }
    }

    // ── Player spawn ───────────────────────────────────────────────────────

    private void SpawnPlayer(PlacedRoom startRoom)
    {
        if (RoomManager.Instance != null)
            RoomManager.Instance.SetCurrentRoom(startRoom);
        if (LevelGrid.Instance != null)
            LevelGrid.Instance.SetCurrentRoomGrid(startRoom.roomGrid);

        int charIndex = CharacterSelection.Index;
        GameObject prefabToSpawn = (charIndex >= 0 && charIndex < playerPrefabs.Count)
            ? playerPrefabs[charIndex]
            : playerPrefabs[0];

        if (prefabToSpawn == null)
        {
            Debug.LogError($"[LevelGenerator] No prefab at index {charIndex}!");
            return;
        }

        GridPosition? spawnPos = FindValidSpawnTile(startRoom.roomGrid);
        if (spawnPos == null)
        {
            Debug.LogError("[LevelGenerator] Could not find a walkable spawn tile in start room!");
            return;
        }

        spawnedPlayer      = Instantiate(prefabToSpawn, Vector3.zero, Quaternion.identity);
        spawnedPlayer.name = "Player";

        Unit playerUnit = spawnedPlayer.GetComponent<Unit>();
        if (playerUnit != null)
            playerUnit.PlaceInRoom(startRoom.roomGrid, spawnPos.Value);

        Debug.Log($"[LevelGenerator] Player spawned at grid {spawnPos.Value}");
    }

    private GridPosition? FindValidSpawnTile(RoomGrid roomGrid)
    {
        TilemapRoomGrid tilemapGrid = roomGrid.GetTilemapRoomGrid();
        if (tilemapGrid == null) { Debug.LogError("[FindSpawn] tilemapGrid is null!"); return null; }

        Tilemap floor = tilemapGrid.GetFloorTilemap();
        if (floor == null) { Debug.LogError("[FindSpawn] floor tilemap is null!"); return null; }

        BoundsInt bounds = floor.cellBounds;

        if (bounds.size.x == 0 || bounds.size.y == 0)
        {
            Debug.LogWarning("[FindSpawn] Room has empty floor bounds — falling back to first valid normal room.");
            foreach (PlacedRoom room in placedRooms)
            {
                if (room.prefabData.roomType == RoomType.Normal && room.roomGrid != null)
                {
                    GridPosition? fallback = FindValidSpawnTile(room.roomGrid);
                    if (fallback != null) return fallback;
                }
            }
            return null;
        }

        int centerX = (bounds.xMin + bounds.xMax) / 2;
        int centerY = (bounds.yMin + bounds.yMax) / 2;

        GridPosition center = new GridPosition(centerX, centerY);
        if (tilemapGrid.IsWalkable(center)) return center;

        for (int radius = 1; radius < Mathf.Max(bounds.size.x, bounds.size.y); radius++)
        {
            for (int x = centerX - radius; x <= centerX + radius; x++)
            {
                for (int y = centerY - radius; y <= centerY + radius; y++)
                {
                    if (Mathf.Abs(x - centerX) != radius && Mathf.Abs(y - centerY) != radius)
                        continue;
                    GridPosition candidate = new GridPosition(x, y);
                    if (tilemapGrid.IsWalkable(candidate)) return candidate;
                }
            }
        }

        return null;
    }

    // ── Placement helpers ──────────────────────────────────────────────────

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
            prefabData.width      = tilemapSetup.GetWidth();
            prefabData.height     = tilemapSetup.GetHeight();
            prefabData.gridOffset = tilemapSetup.GetGridOffset();
        }

        PlacedRoom placedRoom = new PlacedRoom
        {
            roomInstance  = roomInstance,
            prefabData    = prefabData,
            connector     = connector,
            worldPosition = worldPosition,
            gridPosition  = gridPosition
        };

        placedRooms.Add(placedRoom);
        roomGrid.Add(gridPosition, placedRoom);

        return placedRoom;
    }

    private PlacedRoom PlaceRoomInDirection(PlacedRoom existing, Direction dir, RoomType type)
    {
        var exit = existing.connector.GetConnectionPoint(dir);
        if (exit?.transform == null) return null;

        RoomPrefabData newPrefab = GetRandomRoomPrefab(type);
        if (newPrefab == null) return null;

        RoomConnector tempConn = newPrefab.prefab.GetComponent<RoomConnector>();
        if (tempConn == null) return null;

        Direction oppDir = GetOppositeDirection(dir);
        if (!tempConn.HasConnectionPoint(oppDir)) return null;

        var entry = tempConn.GetConnectionPoint(oppDir);

        Vector2Int gridOffset = GetDirectionOffset(dir);
        Vector3    worldDir   = new Vector3(gridOffset.x, 0, gridOffset.y);
        Vector3    newPos     = exit.transform.position - entry.transform.localPosition + (worldDir * roomSpacing);
        Vector2Int newGrid    = existing.gridPosition + gridOffset;

        return PlaceRoom(type, newGrid, newPos);
    }

    private void CreateHallway(PlacedRoom roomA, PlacedRoom roomB, Direction direction)
    {
        if (hallwayPrefab == null) return;

        RoomConnector.ConnectionPoint exitPoint  = roomA.connector.GetConnectionPoint(direction);
        RoomConnector.ConnectionPoint entryPoint = roomB.connector.GetConnectionPoint(GetOppositeDirection(direction));

        if (exitPoint?.transform == null || entryPoint?.transform == null) return;

        Vector3    hallwayPos = (exitPoint.transform.position + entryPoint.transform.position) / 2f;
        Quaternion rot        = (direction == Direction.East || direction == Direction.West)
            ? Quaternion.Euler(0, 90, 0)
            : Quaternion.identity;

        GameObject hallway = Instantiate(hallwayPrefab, hallwayPos, rot, transform);
        hallway.name = $"Hallway_{roomA.gridPosition}_{direction}";
    }

    // ── Room type logic ────────────────────────────────────────────────────

    private RoomType DetermineRoomType(int currentCount, int targetCount, bool bossAlreadyPlaced)
    {
        // Only attempt boss room if a boss prefab is actually assigned
        bool canSpawnBoss = spawnBossRoom && GetRandomRoomPrefab(RoomType.Boss) != null;

        if (canSpawnBoss && !bossAlreadyPlaced && currentCount == targetCount - 2)
            return RoomType.Boss;

        if (currentCount == targetCount - 1)
            return RoomType.End;

        if (Random.value < specialRoomChance)
            return RoomType.Special;

        return RoomType.Normal;
    }

    // ── Utility ────────────────────────────────────────────────────────────

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

    private Vector2Int GetDirectionOffset(Direction direction)
    {
        switch (direction)
        {
            case Direction.North: return new Vector2Int(0,  1);
            case Direction.South: return new Vector2Int(0, -1);
            case Direction.East:  return new Vector2Int(1,  0);
            case Direction.West:  return new Vector2Int(-1, 0);
            default:              return Vector2Int.zero;
        }
    }

    public Direction GetOppositeDirection(Direction dir)
    {
        switch (dir)
        {
            case Direction.North: return Direction.South;
            case Direction.South: return Direction.North;
            case Direction.East:  return Direction.West;
            case Direction.West:  return Direction.East;
            default:              return Direction.North;
        }
    }

    private RoomPrefabData GetRandomRoomPrefab(RoomType roomType)
    {
        List<RoomPrefabData> valid = roomPrefabs.FindAll(p => p.roomType == roomType);
        if (valid.Count == 0) return null;

        float total = 0f;
        foreach (var p in valid) total += p.spawnWeight;

        float rand    = Random.value * total;
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
            int j    = Random.Range(i, list.Count);
            T   temp = list[i];
            list[i]  = list[j];
            list[j]  = temp;
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
            if (room.roomGrid != null && room.roomGrid.IsPositionInRoom(worldPosition))
                return room.roomGrid;
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
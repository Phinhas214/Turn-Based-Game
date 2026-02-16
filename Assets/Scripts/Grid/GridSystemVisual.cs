using System.Collections.Generic;
using UnityEngine;

public class GridSystemVisual : MonoBehaviour
{
    public static GridSystemVisual Instance { get; private set; }

    [SerializeField] private Transform gridSystemVisualSinglePrefab;

    private Dictionary<RoomGrid, GridSystemVisualSingle[,]> roomVisualGrids;
    private RoomGrid currentVisibleRoom;
    private bool isInitialized = false;

    private void Awake()
    {
        if (Instance != null)
        {
            Debug.LogError("There's more than one GridSystemVisual! " + transform + " " + Instance);
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        roomVisualGrids = new Dictionary<RoomGrid, GridSystemVisualSingle[,]>();
    }

    private void OnEnable()
    {
        // Subscribe to level ready event
        LevelGenerator.OnLevelReady += InitializeVisuals;
    }

    private void OnDisable()
    {
        // Unsubscribe
        LevelGenerator.OnLevelReady -= InitializeVisuals;
        
        if (RoomManager.Instance != null)
        {
            RoomManager.Instance.OnRoomChanged -= OnRoomChanged;
        }
    }

    private void InitializeVisuals()
    {
        Debug.Log("=== GridSystemVisual.InitializeVisuals (via OnLevelReady event) ===");
        
        LevelGenerator levelGen = FindFirstObjectByType<LevelGenerator>();
        if (levelGen == null)
        {
            Debug.LogWarning("GridSystemVisual: No LevelGenerator found!");
            return;
        }

        List<LevelGenerator.PlacedRoom> rooms = levelGen.GetAllRooms();
        if (rooms == null || rooms.Count == 0)
        {
            Debug.LogWarning("GridSystemVisual: No rooms to visualize!");
            return;
        }

        // Create visual grids for each room
        foreach (var room in rooms)
        {
            if (room.roomGrid == null) continue;
            CreateVisualGridForRoom(room.roomGrid);
        }

        Debug.Log($"✓ GridSystemVisual: Created visuals for {roomVisualGrids.Count} rooms");

        // Subscribe to room changes
        if (RoomManager.Instance != null)
        {
            RoomManager.Instance.OnRoomChanged += OnRoomChanged;
        }

        // Show the current room's grid
        if (RoomManager.Instance != null && RoomManager.Instance.GetCurrentRoom() != null)
        {
            ShowRoomGrid(RoomManager.Instance.GetCurrentRoomGrid());
        }

        isInitialized = true;
    }

    private void CreateVisualGridForRoom(RoomGrid roomGrid)
    {
        int width = roomGrid.GetWidth();
        int height = roomGrid.GetHeight();

        GridSystemVisualSingle[,] visualArray = new GridSystemVisualSingle[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                GridPosition gridPosition = new GridPosition(x, z);
                Vector3 worldPos = roomGrid.GetWorldPosition(gridPosition);

                Transform visualTransform = Instantiate(
                    gridSystemVisualSinglePrefab,
                    worldPos,
                    Quaternion.identity,
                    transform
                );

                GridSystemVisualSingle visual = visualTransform.GetComponent<GridSystemVisualSingle>();
                visual.Hide(); // Start hidden
                visualArray[x, z] = visual;
            }
        }

        roomVisualGrids[roomGrid] = visualArray;
    }

    private void OnRoomChanged(LevelGenerator.PlacedRoom newRoom)
    {
        if (newRoom != null && newRoom.roomGrid != null)
        {
            ShowRoomGrid(newRoom.roomGrid);
        }
    }

    private void ShowRoomGrid(RoomGrid roomGrid)
    {
        HideAllGrids();
        currentVisibleRoom = roomGrid;
        Debug.Log($"GridSystemVisual: Switched to room grid");
    }

    private void Update()
    {
        if (!isInitialized) return; // Don't update until initialized
        
        UpdateGridVisual();
    }

    public void HideAllGridPosition()
    {
        if (currentVisibleRoom == null || !roomVisualGrids.ContainsKey(currentVisibleRoom))
            return;

        GridSystemVisualSingle[,] visualArray = roomVisualGrids[currentVisibleRoom];

        for (int x = 0; x < visualArray.GetLength(0); x++)
        {
            for (int z = 0; z < visualArray.GetLength(1); z++)
            {
                visualArray[x, z].Hide();
            }
        }
    }

    private void HideAllGrids()
    {
        foreach (var visualArray in roomVisualGrids.Values)
        {
            for (int x = 0; x < visualArray.GetLength(0); x++)
            {
                for (int z = 0; z < visualArray.GetLength(1); z++)
                {
                    visualArray[x, z].Hide();
                }
            }
        }
    }

    public void ShowGridPositionList(List<GridPosition> gridPositionList)
    {
        if (currentVisibleRoom == null || !roomVisualGrids.ContainsKey(currentVisibleRoom))
            return;

        GridSystemVisualSingle[,] visualArray = roomVisualGrids[currentVisibleRoom];

        foreach (GridPosition gridPosition in gridPositionList)
        {
            if (gridPosition.x >= 0 && gridPosition.x < visualArray.GetLength(0) &&
                gridPosition.z >= 0 && gridPosition.z < visualArray.GetLength(1))
            {
                visualArray[gridPosition.x, gridPosition.z].Show();
            }
        }
    }

    private void UpdateGridVisual()
    {
        HideAllGridPosition();

        // Check if UnitActionSystem exists
        if (UnitActionSystem.Instance == null)
        {
            return; // Silently return - system not ready yet
        }
        
        Unit selectedUnit = UnitActionSystem.Instance.GetSelectedUnit();
        if (selectedUnit == null)
        {
            return; // No unit selected - this is normal
        }

        // Make sure unit has MoveAction
        MoveAction moveAction = selectedUnit.GetMoveAction();
        if (moveAction == null)
        {
            return; // No MoveAction - don't spam warnings
        }

        // Show valid move positions
        List<GridPosition> validPositions = moveAction.GetValidActionGridPositionList();
        ShowGridPositionList(validPositions);
    }
}
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
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        roomVisualGrids = new Dictionary<RoomGrid, GridSystemVisualSingle[,]>();
    }

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady += InitializeVisuals;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady -= InitializeVisuals;
        
        if (RoomManager.Instance != null)
        {
            RoomManager.Instance.OnRoomChanged -= OnRoomChanged;
        }
    }

    private void InitializeVisuals()
    {
        LevelGenerator levelGen = FindFirstObjectByType<LevelGenerator>();
        if (levelGen == null) return;

        List<LevelGenerator.PlacedRoom> rooms = levelGen.GetAllRooms();
        if (rooms == null || rooms.Count == 0) return;

        foreach (var room in rooms)
        {
            if (room.roomGrid == null) continue;
            CreateVisualGridForRoom(room.roomGrid);
        }

        if (RoomManager.Instance != null)
        {
            RoomManager.Instance.OnRoomChanged += OnRoomChanged;
        }

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
                visual.Hide();
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
    }

    private void Update()
    {
        if (!isInitialized) return;
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

        if (UnitActionSystem.Instance == null) return;
        
        Unit selectedUnit = UnitActionSystem.Instance.GetSelectedUnit();
        if (selectedUnit == null) return;

        MoveAction moveAction = selectedUnit.GetMoveAction();
        if (moveAction == null) return;

        List<GridPosition> validPositions = moveAction.GetValidActionGridPositionList();
        ShowGridPositionList(validPositions);
    }
}
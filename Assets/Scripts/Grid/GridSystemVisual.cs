using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages grid tile highlight visuals for the current room.
/// • Move action     → shows moveColor tiles
/// • Combat action   → shows rangeHighlight tiles for valid range
///                     + live aoeHighlight tiles that follow the mouse
///
/// Replaces the original GridSystemVisual. Drop-in compatible.
/// </summary>
public class GridSystemVisual : MonoBehaviour
{
    public static GridSystemVisual Instance { get; private set; }

    // ─────────────────────────────────────────────────────────────────────
    [Header("Prefab")]
    [Tooltip("Prefab with a GridSystemVisualSingle component used for each grid tile.")]
    [SerializeField] private Transform gridSystemVisualSinglePrefab;

    [Header("Move Highlight")]
    [Tooltip("Color shown for tiles the player can move to.")]
    [SerializeField] private Color moveColor = new Color(0.5f, 0.8f, 1f, 1f);

    [Header("Combat Highlights")]
    [Tooltip("Color shown for tiles within valid attack range (before hovering).")]
    [SerializeField] private Color rangeColor  = new Color(1f, 0.8f, 0.2f, 0.7f);

    [Tooltip("Color shown for tiles the AoE pattern would actually hit under the cursor.")]
    [SerializeField] private Color aoeColor    = new Color(1f, 0.2f, 0.2f, 1f);

    // ─────────────────────────────────────────────────────────────────────
    //  Runtime state
    // ─────────────────────────────────────────────────────────────────────
    private Dictionary<RoomGrid, GridSystemVisualSingle[,]> roomVisualGrids
        = new Dictionary<RoomGrid, GridSystemVisualSingle[,]>();

    private RoomGrid currentVisibleRoom;
    private bool isInitialized = false;

    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
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
            RoomManager.Instance.OnRoomChanged -= OnRoomChanged;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Initialisation
    // ─────────────────────────────────────────────────────────────────────

    private void InitializeVisuals()
    {
        LevelGenerator levelGen = FindFirstObjectByType<LevelGenerator>();
        if (levelGen == null) return;

        foreach (var room in levelGen.GetAllRooms())
            if (room.roomGrid != null)
                CreateVisualGridForRoom(room.roomGrid);

        if (RoomManager.Instance != null)
        {
            RoomManager.Instance.OnRoomChanged += OnRoomChanged;

            var currentRoom = RoomManager.Instance.GetCurrentRoom();
            if (currentRoom != null)
                ShowRoomGrid(currentRoom.roomGrid);
        }

        isInitialized = true;
    }

    private void CreateVisualGridForRoom(RoomGrid roomGrid)
    {
        int w = roomGrid.GetWidth();
        int h = roomGrid.GetHeight();
        GridSystemVisualSingle[,] arr = new GridSystemVisualSingle[w, h];

        for (int x = 0; x < w; x++)
        {
            for (int z = 0; z < h; z++)
            {
                Vector3 worldPos = roomGrid.GetWorldPosition(new GridPosition(x, z));
                Transform t = Instantiate(gridSystemVisualSinglePrefab, worldPos, Quaternion.identity, transform);
                GridSystemVisualSingle v = t.GetComponent<GridSystemVisualSingle>();
                v.Hide();
                arr[x, z] = v;
            }
        }

        roomVisualGrids[roomGrid] = arr;
    }

    private void OnRoomChanged(LevelGenerator.PlacedRoom newRoom)
    {
        if (newRoom?.roomGrid != null)
            ShowRoomGrid(newRoom.roomGrid);
    }

    private void ShowRoomGrid(RoomGrid roomGrid)
    {
        HideAllGrids();
        currentVisibleRoom = roomGrid;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Per-frame update
    // ─────────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!isInitialized) return;
        UpdateGridVisual();
    }

    private void UpdateGridVisual()
    {
        if (UnitActionSystem.Instance == null) return;

        Unit selectedUnit = UnitActionSystem.Instance.GetSelectedUnit();
        if (selectedUnit == null) return;

        BaseAction selectedAction = UnitActionSystem.Instance.GetSelectedAction();

        HideAllGridPosition();

        if (selectedAction is MoveAction moveAction)
        {
            ShowGridPositionList(moveAction.GetValidActionGridPositionList(), moveColor);
        }
        else if (selectedAction is CombatAction combatAction)
        {
            // Override colors from the action data if available
            Color rangeTint = combatAction.ActionData != null
                ? combatAction.ActionData.rangeHighlightColor
                : rangeColor;
            Color aoeTint = combatAction.ActionData != null
                ? combatAction.ActionData.aoeHighlightColor
                : aoeColor;

            // Show reachable tiles
            ShowGridPositionList(combatAction.GetValidActionGridPositionList(), rangeTint);

            // Show live AoE preview under cursor
            if (LevelGrid.Instance != null)
            {
                GridPosition mousePos = LevelGrid.Instance.GetGridPosition(MouseWorld.GetPosition());
                ShowGridPositionList(combatAction.GetPreviewPositions(mousePos), aoeTint);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Hide all tiles in the current visible room.</summary>
    public void HideAllGridPosition()
    {
        if (currentVisibleRoom == null || !roomVisualGrids.ContainsKey(currentVisibleRoom)) return;
        GridSystemVisualSingle[,] arr = roomVisualGrids[currentVisibleRoom];
        for (int x = 0; x < arr.GetLength(0); x++)
            for (int z = 0; z < arr.GetLength(1); z++)
                arr[x, z].Hide();
    }

    /// <summary>Highlight a list of grid positions with a specific color.</summary>
    public void ShowGridPositionList(List<GridPosition> gridPositionList, Color color)
    {
        if (currentVisibleRoom == null || !roomVisualGrids.ContainsKey(currentVisibleRoom)) return;
        GridSystemVisualSingle[,] arr = roomVisualGrids[currentVisibleRoom];

        foreach (GridPosition gp in gridPositionList)
        {
            if (gp.x >= 0 && gp.x < arr.GetLength(0) &&
                gp.z >= 0 && gp.z < arr.GetLength(1))
            {
                arr[gp.x, gp.z].Show(color);
            }
        }
    }

    /// <summary>Highlight a list of grid positions using the default move color.</summary>
    public void ShowGridPositionList(List<GridPosition> gridPositionList) =>
        ShowGridPositionList(gridPositionList, moveColor);

    // ─────────────────────────────────────────────────────────────────────
    //  Private
    // ─────────────────────────────────────────────────────────────────────

    private void HideAllGrids()
    {
        foreach (var arr in roomVisualGrids.Values)
            for (int x = 0; x < arr.GetLength(0); x++)
                for (int z = 0; z < arr.GetLength(1); z++)
                    arr[x, z].Hide();
    }
}

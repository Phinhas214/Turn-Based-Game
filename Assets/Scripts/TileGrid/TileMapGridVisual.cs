using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Shows valid grid positions by coloring tiles on the Tilemap directly.
/// No separate prefab needed - uses the existing Tilemap.
/// </summary>
public class TilemapGridVisual : MonoBehaviour
{
    public static TilemapGridVisual Instance { get; private set; }

    [Header("Visual Settings")]
    [SerializeField] private Color moveHighlightColor = new Color(0.5f, 0.8f, 1f, 0.6f);
    [SerializeField] private Color rangeHighlightColor = new Color(1f, 0.8f, 0.2f, 0.5f);
    [SerializeField] private Color aoeHighlightColor = new Color(1f, 0.2f, 0.2f, 0.5f);
    [SerializeField] private Color normalColor = Color.white;

    private Tilemap currentTilemap;
    private Dictionary<Vector3Int, Color> originalColors = new Dictionary<Vector3Int, Color>();
    private bool isInitialized = false;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        Debug.Log("[TilemapGridVisual] Instance created");
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

    private void InitializeVisuals()
    {
        Debug.Log("[TilemapGridVisual] InitializeVisuals called");

        if (RoomManager.Instance != null)
        {
            RoomManager.Instance.OnRoomChanged += OnRoomChanged;
            var currentRoom = RoomManager.Instance.GetCurrentRoom();
            if (currentRoom != null)
                SetCurrentRoom(currentRoom.roomGrid);
        }

        isInitialized = true;
        Debug.Log("[TilemapGridVisual] ✅ Initialization complete");
    }

    private void OnRoomChanged(LevelGenerator.PlacedRoom newRoom)
    {
        if (newRoom?.roomGrid != null)
            SetCurrentRoom(newRoom.roomGrid);
    }

    private void SetCurrentRoom(RoomGrid roomGrid)
    {
        // Clear previous room's highlights
        ResetAllTiles();

        // Get the floor tilemap (use floor for highlights, it's on top visually)
        TilemapRoomGrid tilemapGrid = roomGrid.GetTilemapRoomGrid();
        if (tilemapGrid == null) return;

        currentTilemap = tilemapGrid.GetFloorTilemap();
        if (currentTilemap == null)
            currentTilemap = tilemapGrid.GetWallsTilemap();

        if (currentTilemap == null)
        {
            Debug.LogWarning("[TilemapGridVisual] No floor or walls tilemap found!");
            return;
        }

        Debug.Log($"[TilemapGridVisual] Set current tilemap to {currentTilemap.gameObject.name}");
    }

    private void Update()
    {
        if (!isInitialized || currentTilemap == null) return;
        UpdateGridVisual();
    }

    private void UpdateGridVisual()
    {
        if (UnitActionSystem.Instance == null) return;

        Unit selectedUnit = UnitActionSystem.Instance.GetSelectedUnit();
        if (selectedUnit == null) return;

        BaseAction selectedAction = UnitActionSystem.Instance.GetSelectedAction();

        ResetAllTiles();

        if (selectedAction is MoveAction moveAction)
        {
            Debug.Log("[TilemapGridVisual] Showing MoveAction tiles");
            List<GridPosition> validPos = moveAction.GetValidActionGridPositionList();
            Debug.Log($"[TilemapGridVisual] Valid positions: {validPos.Count}");
            HighlightTiles(validPos, moveHighlightColor);
        }
        else if (selectedAction is CombatAction combatAction)
        {
            Color rangeTint = combatAction.ActionData != null
                ? combatAction.ActionData.rangeHighlightColor
                : rangeHighlightColor;

            HighlightTiles(combatAction.GetValidActionGridPositionList(), rangeTint);

            if (LevelGrid.Instance != null)
            {
                GridPosition mousePos = LevelGrid.Instance.GetGridPosition(MouseWorld.GetPosition());
                HighlightTiles(combatAction.GetPreviewPositions(mousePos), aoeHighlightColor);
            }
        }
    }

    private void HighlightTiles(List<GridPosition> positions, Color color)
    {
        if (currentTilemap == null) return;

        int highlightedCount = 0;
        foreach (GridPosition pos in positions)
        {
            Vector3Int tilePos = new Vector3Int(pos.x, pos.z, 0);

            // Store original color if we haven't already
            if (!originalColors.ContainsKey(tilePos))
            {
                originalColors[tilePos] = currentTilemap.GetColor(tilePos);
            }

            // Set highlight color
            currentTilemap.SetColor(tilePos, color);
            highlightedCount++;
        }

        Debug.Log($"[TilemapGridVisual] Highlighted {highlightedCount} tiles");
    }

    private void ResetAllTiles()
    {
        if (currentTilemap == null) return;

        foreach (var kvp in originalColors)
        {
            Vector3Int tilePos = kvp.Key;
            Color originalColor = kvp.Value;

            if (currentTilemap.HasTile(tilePos))
            {
                currentTilemap.SetColor(tilePos, originalColor);
            }
        }

        originalColors.Clear();
    }

    public void ClearAllHighlights()
    {
        ResetAllTiles();
    }
}
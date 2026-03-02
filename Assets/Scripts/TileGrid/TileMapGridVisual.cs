using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TilemapGridVisual : MonoBehaviour
{
    public static TilemapGridVisual Instance { get; private set; }

    [Header("Visual Colors")]
    [SerializeField] private Color moveColor = new Color(0.5f, 0.8f, 1f, 0.6f);
    [SerializeField] private Color rangeColor = new Color(1f, 0.8f, 0.2f, 0.5f);
    [SerializeField] private Color aoeColor = new Color(1f, 0.2f, 0.2f, 0.5f);
    
    // NEW: Hover Indicator Color
    [SerializeField] private Color hoverColor = new Color(1f, 1f, 1f, 0.3f); 

    private Tilemap currentTilemap;
    private Dictionary<Vector3Int, Color> originalColors = new Dictionary<Vector3Int, Color>();
    private bool isInitialized = false;

    private void Awake() => Instance = this;
    private void OnEnable() => LevelGenerator.OnLevelReady += Initialize;
    private void OnDisable() => LevelGenerator.OnLevelReady -= Initialize;

    private void Initialize()
    {
        if (RoomManager.Instance != null)
        {
            RoomManager.Instance.OnRoomChanged += (newRoom) => SetCurrentRoom(newRoom.roomGrid);
            var startRoom = RoomManager.Instance.GetCurrentRoom();
            if (startRoom != null) SetCurrentRoom(startRoom.roomGrid);
        }
        isInitialized = true;
    }

    private void SetCurrentRoom(RoomGrid roomGrid)
    {
        ResetAllTiles();
        TilemapRoomGrid trg = roomGrid.GetTilemapRoomGrid();
        currentTilemap = trg.GetFloorTilemap();
    }

    private void Update()
    {
        if (!isInitialized || currentTilemap == null) return;

        ResetAllTiles(); // Clear visuals from previous frame

        // 1. Show Action Highlights (Move/Attack)
        UpdateActionVisuals();

        // 2. Show Hover Highlight (Always last so it shows on top)
        UpdateHoverVisual();
    }

    private void UpdateActionVisuals()
    {
        Unit selectedUnit = UnitActionSystem.Instance.GetSelectedUnit();
        if (selectedUnit == null) return;

        BaseAction selectedAction = UnitActionSystem.Instance.GetSelectedAction();

        if (selectedAction is MoveAction moveAction)
        {
            HighlightGridPositions(moveAction.GetValidActionGridPositionList(), moveColor);
        }
        else if (selectedAction is CombatAction combatAction)
        {
            HighlightGridPositions(combatAction.GetValidActionGridPositionList(), rangeColor);
            
            GridPosition mousePos = LevelGrid.Instance.GetGridPosition(MouseWorld.GetPosition());
            HighlightGridPositions(combatAction.GetPreviewPositions(mousePos), aoeColor);
        }
    }

    private void UpdateHoverVisual()
    {
        // Get the current grid position under the mouse
        GridPosition mouseGridPos = LevelGrid.Instance.GetGridPosition(MouseWorld.GetPosition());

        // Only highlight if the position is valid within the level bounds
        if (LevelGrid.Instance.IsValidGridPosition(mouseGridPos))
        {
            // Map Grid Z to Tilemap Y
            Vector3Int tilePos = new Vector3Int(mouseGridPos.x, mouseGridPos.z, 0);

            // Store original color if we haven't already
            if (!originalColors.ContainsKey(tilePos))
            {
                originalColors[tilePos] = currentTilemap.GetColor(tilePos);
            }

            // Apply hover tint
            currentTilemap.SetTileFlags(tilePos, TileFlags.None);
            currentTilemap.SetColor(tilePos, hoverColor);
        }
    }

    private void HighlightGridPositions(List<GridPosition> positions, Color color)
    {
        foreach (GridPosition gp in positions)
        {
            Vector3Int tilePos = new Vector3Int(gp.x, gp.z, 0);

            if (!originalColors.ContainsKey(tilePos))
                originalColors[tilePos] = currentTilemap.GetColor(tilePos);

            currentTilemap.SetTileFlags(tilePos, TileFlags.None);
            currentTilemap.SetColor(tilePos, color);
        }
    }

    private void ResetAllTiles()
    {
        if (currentTilemap == null) return;
        foreach (var kvp in originalColors)
        {
            currentTilemap.SetColor(kvp.Key, kvp.Value);
        }
        originalColors.Clear();
    }
}
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TilemapGridVisual : MonoBehaviour
{
    public static TilemapGridVisual Instance { get; private set; }

    [Header("Visual Colors")]
    [SerializeField] private Color moveColor  = new Color(0.5f, 0.8f, 1f,  0.6f);
    [SerializeField] private Color rangeColor = new Color(1f,   0.8f, 0.2f, 0.5f);
    [SerializeField] private Color aoeColor   = new Color(1f,   0.2f, 0.2f, 0.5f);
    [SerializeField] private Color hoverColor = new Color(1f,   1f,   1f,   0.3f);

    private Tilemap currentTilemap;

    // Store the tile's original color properly
    private HashSet<Vector3Int> modifiedTiles = new HashSet<Vector3Int>();

    private void Awake() => Instance = this;

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady    += OnLevelReady;
        RoomManager.OnAnyRoomChanged   += OnRoomChanged;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady    -= OnLevelReady;
        RoomManager.OnAnyRoomChanged   -= OnRoomChanged;
    }

    private void OnLevelReady()
    {
        if (RoomManager.Instance != null)
        {
            var room = RoomManager.Instance.GetCurrentRoom();
            if (room != null) SetCurrentRoom(room);
        }
    }

    private void OnRoomChanged(LevelGenerator.PlacedRoom newRoom)
    {
        if (newRoom?.roomGrid != null)
            SetCurrentRoom(newRoom);
    }

    private void SetCurrentRoom(LevelGenerator.PlacedRoom room)
    {
        ResetAllTiles();
        TilemapRoomGrid trg = room.roomGrid.GetTilemapRoomGrid();
        if (trg == null) return;
        currentTilemap = trg.GetFloorTilemap();
        Debug.Log($"[TilemapGridVisual] Tilemap set to Floor in {room.roomInstance.name}");
    }

    private void Update()
    {
        if (currentTilemap == null) return;
        ResetAllTiles();
        UpdateActionVisuals();
        UpdateHoverVisual();
    }

    private void UpdateActionVisuals()
    {
        if (UnitActionSystem.Instance == null) return;
        Unit selectedUnit = UnitActionSystem.Instance.GetSelectedUnit();
        if (selectedUnit == null) return;

        BaseAction selectedAction = UnitActionSystem.Instance.GetSelectedAction();
        if (selectedAction == null) return;

        if (selectedAction is MoveAction moveAction)
            HighlightGridPositions(moveAction.GetValidActionGridPositionList(), moveColor);
        else if (selectedAction is CombatAction combatAction)
        {
            HighlightGridPositions(combatAction.GetValidActionGridPositionList(), rangeColor);
            if (LevelGrid.Instance != null)
            {
                GridPosition mousePos = LevelGrid.Instance.GetGridPosition(MouseWorld.GetPosition());
                HighlightGridPositions(combatAction.GetPreviewPositions(mousePos), aoeColor);
            }
        }
    }

    private void UpdateHoverVisual()
    {
        if (LevelGrid.Instance == null || !LevelGrid.Instance.IsInitialized()) return;
        GridPosition mouseGridPos = LevelGrid.Instance.GetGridPosition(MouseWorld.GetPosition());
        if (!LevelGrid.Instance.IsValidGridPosition(mouseGridPos)) return;

        Vector3Int tilePos = new Vector3Int(mouseGridPos.x, mouseGridPos.z, 0);
        ColorTile(tilePos, hoverColor);
    }

    private void HighlightGridPositions(List<GridPosition> positions, Color color)
    {
        if (currentTilemap == null) return;
        foreach (GridPosition gp in positions)
            ColorTile(new Vector3Int(gp.x, gp.z, 0), color);
    }

    private void ColorTile(Vector3Int tilePos, Color color)
    {
        if (currentTilemap == null) return;
        if (!currentTilemap.HasTile(tilePos)) return;

        // Unlock flags so we can set color
        currentTilemap.SetTileFlags(tilePos, TileFlags.None);
        currentTilemap.SetColor(tilePos, color);
        modifiedTiles.Add(tilePos);
    }

    private void ResetAllTiles()
    {
        if (currentTilemap == null) return;
        foreach (Vector3Int tilePos in modifiedTiles)
        {
            if (currentTilemap.HasTile(tilePos))
            {
                currentTilemap.SetTileFlags(tilePos, TileFlags.None);
                // Reset to white = full original color, no tint
                currentTilemap.SetColor(tilePos, Color.white);
            }
        }
        modifiedTiles.Clear();
    }
}
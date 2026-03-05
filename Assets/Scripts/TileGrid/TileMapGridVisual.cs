using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TilemapGridVisual : MonoBehaviour
{
    public static TilemapGridVisual Instance { get; private set; }

    [Header("Visual Assets")]
    [SerializeField] private TileBase solidWhiteTile; // Drag a plain white square tile here

    [Header("Visual Colors")]
    // Ensure Alpha (A) is high (0.8 - 1.0) for that solid look
    [SerializeField] private Color moveColor  = new Color(0.2f, 0.6f, 1f, 1f); 
    [SerializeField] private Color rangeColor = new Color(1f, 0.85f, 0f, 1f); 
    [SerializeField] private Color aoeColor   = new Color(1f, 0.15f, 0.15f, 1f); 
    [SerializeField] private Color hoverColor = new Color(1f, 1f, 1f, 0.5f); 

    private Tilemap currentTilemap;
    
    // Tracks original tiles so we can restore the floor art perfectly
    private Dictionary<Vector3Int, TileBase> originalTileData = new Dictionary<Vector3Int, TileBase>();
    private HashSet<Vector3Int> modifiedPositions = new HashSet<Vector3Int>();

    private void Awake() => Instance = this;

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady += OnLevelReady;
        RoomManager.OnAnyRoomChanged += OnRoomChanged;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady -= OnLevelReady;
        RoomManager.OnAnyRoomChanged -= OnRoomChanged;
    }

    private void OnLevelReady() => RefreshCurrentTilemap();
    private void OnRoomChanged(LevelGenerator.PlacedRoom room) => RefreshCurrentTilemap();

    private void RefreshCurrentTilemap()
    {
        ResetAllTiles();
        var room = RoomManager.Instance?.GetCurrentRoom();
        if (room?.roomGrid != null)
        {
            currentTilemap = room.roomGrid.GetTilemapRoomGrid()?.GetFloorTilemap();
        }
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

        BaseAction selectedAction = UnitActionSystem.Instance.GetSelectedAction();
        if (selectedAction == null) return;

        if (selectedAction is MoveAction moveAction)
        {
            HighlightPositions(moveAction.GetValidActionGridPositionList(), moveColor);
        }
        else if (selectedAction is CombatAction combatAction)
        {
            Color rColor = combatAction.ActionData != null ? combatAction.ActionData.rangeHighlightColor : rangeColor;
            Color aColor = combatAction.ActionData != null ? combatAction.ActionData.aoeHighlightColor : aoeColor;

            HighlightPositions(combatAction.GetValidActionGridPositionList(), rColor);

            GridPosition mousePos = LevelGrid.Instance.GetGridPosition(MouseWorld.GetPosition());
            HighlightPositions(combatAction.GetPreviewPositions(mousePos), aColor);
        }
    }

    private void UpdateHoverVisual()
    {
        if (LevelGrid.Instance == null || !LevelGrid.Instance.IsInitialized()) return;

        GridPosition mouseGridPos = LevelGrid.Instance.GetGridPosition(MouseWorld.GetPosition());
        if (LevelGrid.Instance.IsValidGridPosition(mouseGridPos))
        {
            ApplySolidColor(new Vector3Int(mouseGridPos.x, mouseGridPos.z, 0), hoverColor);
        }
    }

    private void HighlightPositions(List<GridPosition> positions, Color color)
    {
        foreach (GridPosition gp in positions)
        {
            ApplySolidColor(new Vector3Int(gp.x, gp.z, 0), color);
        }
    }

    private void ApplySolidColor(Vector3Int pos, Color color)
    {
        if (currentTilemap == null || !currentTilemap.HasTile(pos)) return;

        // 1. Capture the original floor tile before we swap it
        if (!originalTileData.ContainsKey(pos))
        {
            originalTileData[pos] = currentTilemap.GetTile(pos);
        }

        // 2. Swap to the solid white tile asset
        currentTilemap.SetTile(pos, solidWhiteTile);

        // 3. IMPORTANT: Unlock flags AFTER setting the tile, or Unity resets it to 'Locked'
        currentTilemap.SetTileFlags(pos, TileFlags.None);

        // 4. Apply the color
        currentTilemap.SetColor(pos, color);
        
        modifiedPositions.Add(pos);
    }

    private void ResetAllTiles()
    {
        if (currentTilemap == null) return;

        foreach (Vector3Int pos in modifiedPositions)
        {
            if (originalTileData.TryGetValue(pos, out TileBase originalTile))
            {
                // Restore the original floor texture
                currentTilemap.SetTile(pos, originalTile);
                
                // Reset color to full white (no tint)
                currentTilemap.SetTileFlags(pos, TileFlags.None);
                currentTilemap.SetColor(pos, Color.white);
            }
        }

        modifiedPositions.Clear();
        originalTileData.Clear();
    }
}
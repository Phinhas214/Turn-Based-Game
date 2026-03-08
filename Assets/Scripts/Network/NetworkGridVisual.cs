using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Networked grid/tilemap visual that only shows highlights for the LOCAL player.
///
/// BUG FIX: Old version cached currentTilemap on room-change events. After moving rooms
/// the highlights painted on the OLD room's tilemap at wrong positions (stale cache).
/// Now the tilemap is resolved every frame from the current room — always correct.
///
/// SETUP:
///   - Attach to your GridSystemVisual / TilemapGridVisual GameObject.
///   - Fill solidWhiteTile in the Inspector.
///   - Disable/remove the old TilemapGridVisual component.
/// </summary>
public class NetworkedGridVisual : MonoBehaviour
{
    public static NetworkedGridVisual Instance { get; private set; }

    [Header("Visual Assets")]
    [SerializeField] private TileBase solidWhiteTile;

    [Header("Visual Colors")]
    [SerializeField] private Color moveColor  = new Color(0.2f, 0.6f, 1f,    1f);
    [SerializeField] private Color rangeColor = new Color(1f,   0.85f, 0f,   1f);
    [SerializeField] private Color aoeColor   = new Color(1f,   0.15f, 0.15f, 1f);
    [SerializeField] private Color hoverColor = new Color(1f,   1f,   1f,    0.5f);

    // Per-frame paint tracking — reset every frame before repainting
    private Tilemap             lastTilemap      = null;
    private HashSet<Vector3Int> modifiedPositions = new HashSet<Vector3Int>();
    private Dictionary<Vector3Int, TileBase> originalTileData = new Dictionary<Vector3Int, TileBase>();

    private void Awake() => Instance = this;

    private void Update()
    {
        // ALWAYS resolve the current room's tilemap this frame (never cache across frames)
        Tilemap currentTilemap = GetCurrentRoomTilemap();

        // If the player moved to a different room, clear paint from the OLD tilemap
        if (currentTilemap != lastTilemap)
        {
            ResetAllTiles(lastTilemap);
            lastTilemap = currentTilemap;
        }

        if (currentTilemap == null) return;

        // Only the local player sees their own highlights
        if (!IsLocalPlayerActive())
        {
            ResetAllTiles(currentTilemap);
            return;
        }

        ResetAllTiles(currentTilemap);
        UpdateActionVisuals(currentTilemap);
        UpdateHoverVisual(currentTilemap);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Tilemap resolution — fresh every frame
    // ─────────────────────────────────────────────────────────────────────

    private Tilemap GetCurrentRoomTilemap()
    {
        var room = RoomManager.Instance?.GetCurrentRoom();
        if (room?.roomGrid == null) return null;
        return room.roomGrid.GetTilemapRoomGrid()?.GetFloorTilemap();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Ownership check
    // ─────────────────────────────────────────────────────────────────────

    private bool IsLocalPlayerActive()
    {
        if (NetworkedUnitActionSystem.Instance != null)
        {
            Unit selected = NetworkedUnitActionSystem.Instance.GetSelectedUnit();
            if (selected == null) return false;
            var netObj = selected.GetComponent<Unity.Netcode.NetworkObject>();
            return netObj == null || netObj.IsOwner;
        }
        if (UnitActionSystem.Instance != null)
            return UnitActionSystem.Instance.GetSelectedUnit() != null;
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Visual updates
    // ─────────────────────────────────────────────────────────────────────

    private void UpdateActionVisuals(Tilemap tilemap)
    {
        BaseAction selectedAction = NetworkedUnitActionSystem.Instance?.GetSelectedAction()
                                 ?? UnitActionSystem.Instance?.GetSelectedAction();
        if (selectedAction == null) return;

        if (selectedAction is MoveAction moveAction)
        {
            HighlightPositions(tilemap, moveAction.GetValidActionGridPositionList(), moveColor);
        }
        else if (selectedAction is CombatAction combatAction)
        {
            Color rColor = combatAction.ActionData != null ? combatAction.ActionData.rangeHighlightColor : rangeColor;
            Color aColor = combatAction.ActionData != null ? combatAction.ActionData.aoeHighlightColor   : aoeColor;

            HighlightPositions(tilemap, combatAction.GetValidActionGridPositionList(), rColor);

            if (LevelGrid.Instance != null)
            {
                Vector3    mouseWorld   = MouseWorld.GetPosition();
                RoomGrid   mouseRoom    = LevelGrid.Instance.GetRoomAtPosition(mouseWorld);
                if (mouseRoom != null)
                {
                    GridPosition mousePos = mouseRoom.GetGridPosition(mouseWorld);
                    HighlightPositions(tilemap, combatAction.GetPreviewPositions(mousePos), aColor);
                }
            }
        }
    }

    private void UpdateHoverVisual(Tilemap tilemap)
    {
        if (LevelGrid.Instance == null || !LevelGrid.Instance.IsInitialized()) return;

        Vector3  mouseWorld = MouseWorld.GetPosition();
        RoomGrid mouseRoom  = LevelGrid.Instance.GetRoomAtPosition(mouseWorld);
        if (mouseRoom == null) return;

        GridPosition mouseGridPos = mouseRoom.GetGridPosition(mouseWorld);
        if (mouseRoom.IsValidGridPosition(mouseGridPos))
            ApplySolidColor(tilemap, new Vector3Int(mouseGridPos.x, mouseGridPos.z, 0), hoverColor);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Tile painting
    // ─────────────────────────────────────────────────────────────────────

    private void HighlightPositions(Tilemap tilemap, List<GridPosition> positions, Color color)
    {
        foreach (GridPosition gp in positions)
            ApplySolidColor(tilemap, new Vector3Int(gp.x, gp.z, 0), color);
    }

    private void ApplySolidColor(Tilemap tilemap, Vector3Int pos, Color color)
    {
        if (tilemap == null || !tilemap.HasTile(pos)) return;

        if (!originalTileData.ContainsKey(pos))
            originalTileData[pos] = tilemap.GetTile(pos);

        tilemap.SetTile(pos, solidWhiteTile);
        tilemap.SetTileFlags(pos, TileFlags.None);
        tilemap.SetColor(pos, color);
        modifiedPositions.Add(pos);
    }

    private void ResetAllTiles(Tilemap tilemap)
    {
        if (tilemap == null || modifiedPositions.Count == 0) return;

        foreach (Vector3Int pos in modifiedPositions)
        {
            if (originalTileData.TryGetValue(pos, out TileBase original))
            {
                tilemap.SetTile(pos, original);
                tilemap.SetTileFlags(pos, TileFlags.None);
                tilemap.SetColor(pos, Color.white);
            }
        }

        modifiedPositions.Clear();
        originalTileData.Clear();
    }
}
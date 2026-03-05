using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Networked grid/tilemap visual that only shows highlights for the LOCAL player.
///
/// Other players do not see each other's movement ranges or attack previews.
/// This is a drop-in replacement used INSTEAD of both GridSystemVisual
/// and TilemapGridVisual — it does the job of both, but with the owner check.
///
/// SETUP:
///   - Attach to the same GameObject as your TilemapGridVisual.
///   - Fill solidWhiteTile reference.
///   - Remove or disable the old TilemapGridVisual component.
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

    private Tilemap currentTilemap;

    private Dictionary<Vector3Int, TileBase> originalTileData  = new Dictionary<Vector3Int, TileBase>();
    private HashSet<Vector3Int>              modifiedPositions  = new HashSet<Vector3Int>();

    private void Awake() => Instance = this;

    private void OnEnable()
    {
        NetworkedLevelGenerator.OnLevelReady += OnLevelReady;
        RoomManager.OnAnyRoomChanged         += OnRoomChanged;
    }

    private void OnDisable()
    {
        NetworkedLevelGenerator.OnLevelReady -= OnLevelReady;
        RoomManager.OnAnyRoomChanged         -= OnRoomChanged;
    }

    private void OnLevelReady()      => RefreshCurrentTilemap();
    private void OnRoomChanged(LevelGenerator.PlacedRoom room) => RefreshCurrentTilemap();

    private void RefreshCurrentTilemap()
    {
        ResetAllTiles();
        var room = RoomManager.Instance?.GetCurrentRoom();
        if (room?.roomGrid != null)
            currentTilemap = room.roomGrid.GetTilemapRoomGrid()?.GetFloorTilemap();
    }

    private void Update()
    {
        if (currentTilemap == null) return;

        // CRITICAL: Only render highlights if this is the local player's action system
        if (!IsLocalPlayerActive()) return;

        ResetAllTiles();
        UpdateActionVisuals();
        UpdateHoverVisual();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Ownership check
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if there is a valid local-player unit selected.
    /// Prevents showing highlights on other players' screens.
    /// </summary>
    private bool IsLocalPlayerActive()
    {
        if (NetworkedUnitActionSystem.Instance == null) return false;

        Unit selected = NetworkedUnitActionSystem.Instance.GetSelectedUnit();
        if (selected == null) return false;

        // If no NetworkObject, assume local (single-player testing)
        NetworkObject netObj = selected.GetComponent<NetworkObject>();
        if (netObj == null) return true;

        return netObj.IsOwner;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Visual updates (identical logic to TilemapGridVisual, just uses NetworkedUnitActionSystem)
    // ─────────────────────────────────────────────────────────────────────

    private void UpdateActionVisuals()
    {
        if (NetworkedUnitActionSystem.Instance == null) return;

        BaseAction selectedAction = NetworkedUnitActionSystem.Instance.GetSelectedAction();
        if (selectedAction == null) return;

        if (selectedAction is MoveAction moveAction)
        {
            HighlightPositions(moveAction.GetValidActionGridPositionList(), moveColor);
        }
        else if (selectedAction is CombatAction combatAction)
        {
            Color rColor = combatAction.ActionData != null ? combatAction.ActionData.rangeHighlightColor : rangeColor;
            Color aColor = combatAction.ActionData != null ? combatAction.ActionData.aoeHighlightColor   : aoeColor;

            HighlightPositions(combatAction.GetValidActionGridPositionList(), rColor);

            if (LevelGrid.Instance != null)
            {
                GridPosition mousePos = LevelGrid.Instance.GetGridPosition(MouseWorld.GetPosition());
                HighlightPositions(combatAction.GetPreviewPositions(mousePos), aColor);
            }
        }
    }

    private void UpdateHoverVisual()
    {
        if (LevelGrid.Instance == null || !LevelGrid.Instance.IsInitialized()) return;

        GridPosition mouseGridPos = LevelGrid.Instance.GetGridPosition(MouseWorld.GetPosition());
        if (LevelGrid.Instance.IsValidGridPosition(mouseGridPos))
            ApplySolidColor(new Vector3Int(mouseGridPos.x, mouseGridPos.z, 0), hoverColor);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Tile painting (same as TilemapGridVisual)
    // ─────────────────────────────────────────────────────────────────────

    private void HighlightPositions(List<GridPosition> positions, Color color)
    {
        foreach (GridPosition gp in positions)
            ApplySolidColor(new Vector3Int(gp.x, gp.z, 0), color);
    }

    private void ApplySolidColor(Vector3Int pos, Color color)
    {
        if (currentTilemap == null || !currentTilemap.HasTile(pos)) return;

        if (!originalTileData.ContainsKey(pos))
            originalTileData[pos] = currentTilemap.GetTile(pos);

        currentTilemap.SetTile(pos, solidWhiteTile);
        currentTilemap.SetTileFlags(pos, TileFlags.None);
        currentTilemap.SetColor(pos, color);

        modifiedPositions.Add(pos);
    }

    private void ResetAllTiles()
    {
        if (currentTilemap == null) return;

        foreach (Vector3Int pos in modifiedPositions)
        {
            if (originalTileData.TryGetValue(pos, out TileBase original))
            {
                currentTilemap.SetTile(pos, original);
                currentTilemap.SetTileFlags(pos, TileFlags.None);
                currentTilemap.SetColor(pos, Color.white);
            }
        }

        modifiedPositions.Clear();
        originalTileData.Clear();
    }
}
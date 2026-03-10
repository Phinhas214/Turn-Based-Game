using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TilemapGridVisual : MonoBehaviour
{
    public static TilemapGridVisual Instance { get; private set; }

    [Header("Visual Assets")]
    [SerializeField] private TileBase solidWhiteTile;

    [Header("Visual Colors")]
    [SerializeField] private Color moveColor  = new Color(0.2f, 0.6f, 1f,    1f);
    [SerializeField] private Color rangeColor = new Color(1f,   0.85f, 0f,   1f);
    [SerializeField] private Color aoeColor   = new Color(1f,   0.15f, 0.15f, 1f);
    [SerializeField] private Color hoverColor = new Color(1f,   1f,   1f,    0.5f);

    private Tilemap currentTilemap;

    private Dictionary<Vector3Int, TileBase> originalTileData  = new Dictionary<Vector3Int, TileBase>();
    private HashSet<Vector3Int>              modifiedPositions = new HashSet<Vector3Int>();

    private void Awake() => Instance = this;

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady          += OnLevelReady;
        NetworkedLevelGenerator.OnLevelReady += OnLevelReady;
        RoomManager.OnAnyRoomChanged         += OnRoomChanged;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady          -= OnLevelReady;
        NetworkedLevelGenerator.OnLevelReady -= OnLevelReady;
        RoomManager.OnAnyRoomChanged         -= OnRoomChanged;
    }

    private void OnLevelReady() => RefreshCurrentTilemap();
    private void OnRoomChanged(LevelGenerator.PlacedRoom room) => RefreshCurrentTilemap();

    private void RefreshCurrentTilemap()
    {
        ResetAllTiles();
        RoomGrid roomGrid = GetLocalPlayerRoomGrid();
        currentTilemap = roomGrid?.GetTilemapRoomGrid()?.GetFloorTilemap();
    }

    private void Update()
    {
        // Re-resolve every frame so the tilemap always follows the local player
        // even when they move within a room (Unit.gridPosition updates every frame)
        RoomGrid roomGrid = GetLocalPlayerRoomGrid();
        Tilemap  tilemap  = roomGrid?.GetTilemapRoomGrid()?.GetFloorTilemap();

        // Player moved to a different room — clear old paint, switch tilemap
        if (tilemap != currentTilemap)
        {
            ResetAllTiles();
            currentTilemap = tilemap;
        }

        if (currentTilemap == null) return;

        ResetAllTiles();
        UpdateActionVisuals();
        UpdateHoverVisual();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Room resolution
    // SP:  reads from local Unit component directly (frame-accurate)
    // MP:  reads from owned NetworkedUnit (also frame-accurate via Unit.Update)
    // Either way it is per-client — no shared global state
    // ─────────────────────────────────────────────────────────────────────

    private RoomGrid GetLocalPlayerRoomGrid()
    {
        bool isMP = Unity.Netcode.NetworkManager.Singleton != null
                 && Unity.Netcode.NetworkManager.Singleton.IsListening;

        if (isMP)
        {
            // Find the Unit the local client owns
            foreach (var netObj in FindObjectsByType<Unity.Netcode.NetworkObject>(FindObjectsSortMode.None))
            {
                if (!netObj.IsOwner) continue;
                Unit u = netObj.GetComponent<Unit>();
                if (u != null) return u.GetCurrentRoomGrid();
            }
        }

        // SP: just get the single Unit in the scene
        Unit spUnit = FindFirstObjectByType<Unit>();
        return spUnit?.GetCurrentRoomGrid();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Action system — checks MP system first, falls back to SP
    // ─────────────────────────────────────────────────────────────────────

    private BaseAction GetSelectedAction()
    {
        if (NetworkedUnitActionSystem.Instance != null)
            return NetworkedUnitActionSystem.Instance.GetSelectedAction();
        return UnitActionSystem.Instance?.GetSelectedAction();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Everything below is identical to the working SP version
    // ─────────────────────────────────────────────────────────────────────

    private void UpdateActionVisuals()
    {
        BaseAction selectedAction = GetSelectedAction();
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

            GridPosition mousePos = LevelGrid.Instance.GetGridPosition(MouseWorld.GetPosition());
            HighlightPositions(combatAction.GetPreviewPositions(mousePos), aColor);
        }
    }

    private void UpdateHoverVisual()
    {
        if (LevelGrid.Instance == null || !LevelGrid.Instance.IsInitialized()) return;

        GridPosition mouseGridPos = LevelGrid.Instance.GetGridPosition(MouseWorld.GetPosition());
        if (LevelGrid.Instance.IsValidGridPosition(mouseGridPos))
            ApplySolidColor(new Vector3Int(mouseGridPos.x, mouseGridPos.z, 0), hoverColor);
    }

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
            if (originalTileData.TryGetValue(pos, out TileBase originalTile))
            {
                currentTilemap.SetTile(pos, originalTile);
                currentTilemap.SetTileFlags(pos, TileFlags.None);
                currentTilemap.SetColor(pos, Color.white);
            }
        }

        modifiedPositions.Clear();
        originalTileData.Clear();
    }
}
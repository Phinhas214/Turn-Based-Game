using System.Collections.Generic;
using Unity.Netcode;
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
    [SerializeField] private Color hoverColor = new Color(1f,   1f,    1f,    0.5f);

    private Tilemap currentTilemap;
    private Dictionary<Vector3Int, TileBase> originalTileData  = new Dictionary<Vector3Int, TileBase>();
    private HashSet<Vector3Int> modifiedPositions = new HashSet<Vector3Int>();

    private void Awake() => Instance = this;

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady            += OnLevelReady;
        NetworkedLevelGenerator.OnLevelReady += OnLevelReady;
        RoomManager.OnAnyRoomChanged         += OnRoomChanged;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady            -= OnLevelReady;
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
        RoomGrid roomGrid = GetLocalPlayerRoomGrid();
        Tilemap tilemap = roomGrid?.GetTilemapRoomGrid()?.GetFloorTilemap();

        if (tilemap != currentTilemap)
        {
            Debug.Log($"[TilemapGridVisual] Context switch: {(currentTilemap?.gameObject.name ?? "NULL")} → {(tilemap?.gameObject.name ?? "NULL")}");
            ResetAllTiles();
            currentTilemap = tilemap;
        }

        if (currentTilemap == null) return;

        ResetAllTiles();

        // Clear the cost UI before recalculating this frame
        if (GridCostVisualizer.Instance != null)
            GridCostVisualizer.Instance.ClearAll();

        UpdateActionVisuals();
        UpdateHoverVisual();
    }

    // ── Logic ─────────────────────────────────────────────────────────────

    private void UpdateActionVisuals()
    {
        BaseAction selectedAction = GetSelectedAction();
        if (selectedAction == null) return;

        if (selectedAction is MoveAction moveAction)
        {
            List<GridPosition> validPositions = moveAction.GetValidActionGridPositionList();
            HighlightPositions(validPositions, moveColor);

            // Handle Move Cost UI (Sam's addition)
            GridPosition mouseGridPos = LevelGrid.Instance.GetGridPosition(MouseWorld.GetPosition());
            if (validPositions.Contains(mouseGridPos) && GridCostVisualizer.Instance != null)
            {
                int cost = moveAction.GetMoveCost(mouseGridPos);
                GridCostVisualizer.Instance.ShowCost(mouseGridPos, cost);
            }
        }
        else if (selectedAction is CombatAction combatAction)
        {
            // Use specific ActionData colors if available, otherwise fallback to defaults
            Color rColor = combatAction.ActionData != null ? combatAction.ActionData.rangeHighlightColor : rangeColor;
            Color aColor = combatAction.ActionData != null ? combatAction.ActionData.aoeHighlightColor : aoeColor;

            HighlightPositions(combatAction.GetValidActionGridPositionList(), rColor);

            // Show AOE preview based on mouse position
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

    // ── Systems Resolution ───────────────────────────────────────────────

    private RoomGrid GetLocalPlayerRoomGrid()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            foreach (var netObj in FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
            {
                if (!netObj.IsOwner) continue;
                Unit u = netObj.GetComponent<Unit>();
                if (u != null) return u.GetCurrentRoomGrid();
            }
        }
        return FindFirstObjectByType<Unit>()?.GetCurrentRoomGrid();
    }

    private BaseAction GetSelectedAction()
    {
        if (NetworkedUnitActionSystem.Instance != null)
            return NetworkedUnitActionSystem.Instance.GetSelectedAction();
        return UnitActionSystem.Instance?.GetSelectedAction();
    }

    // ── Rendering ────────────────────────────────────────────────────────

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

        // Apply solid color visual
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
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class RoomSpawnPointReader : MonoBehaviour
{
    [SerializeField] private Tilemap spawnPointsTilemap;
    [SerializeField] private bool showDebugLogs = true;

    private Dictionary<LevelGenerator.Direction, GridPosition> spawnPositions
        = new Dictionary<LevelGenerator.Direction, GridPosition>();

    private bool isInitialized = false;

    // Called by RoomTilemapSetup — we just find the tilemap here, don't scan yet
    public void Initialize()
    {
        if (spawnPointsTilemap != null) return;

        foreach (Tilemap tm in GetComponentsInChildren<Tilemap>())
        {
            if (tm.gameObject.name == "SpawnPoints")
            {
                spawnPointsTilemap = tm;
                break;
            }
        }

        if (spawnPointsTilemap == null)
            Debug.LogWarning($"[RoomSpawnPointReader] No SpawnPoints tilemap in {gameObject.name}");
        else
            Debug.Log($"[RoomSpawnPointReader] Found SpawnPoints tilemap in {gameObject.name}");
    }

    // Lazy scan — only runs once, on first request
    private void EnsureScanned()
    {
        if (isInitialized) return;

        if (spawnPointsTilemap == null)
        {
            // Try one more time in case Initialize() missed it
            foreach (Tilemap tm in GetComponentsInChildren<Tilemap>())
            {
                if (tm.gameObject.name == "SpawnPoints")
                {
                    spawnPointsTilemap = tm;
                    break;
                }
            }
        }

        if (spawnPointsTilemap == null)
        {
            Debug.LogWarning($"[RoomSpawnPointReader] Still no SpawnPoints tilemap in {gameObject.name}");
            isInitialized = true;
            return;
        }

        // Hide renderer at runtime
        TilemapRenderer rend = spawnPointsTilemap.GetComponent<TilemapRenderer>();
        if (rend != null) rend.enabled = false;

        // Scan every cell
        spawnPositions.Clear();
        int found = 0;
        int totalTiles = 0;

        BoundsInt bounds = spawnPointsTilemap.cellBounds;
        Debug.Log($"[RoomSpawnPointReader] Scanning {gameObject.name} bounds {bounds}");

        foreach (Vector3Int pos in bounds.allPositionsWithin)
        {
            TileBase tile = spawnPointsTilemap.GetTile(pos);
            if (tile == null) continue;

            totalTiles++;
            Debug.Log($"[RoomSpawnPointReader] Cell {pos} tile: {tile.name} ({tile.GetType().Name})");

            if (tile is SpawnPointTile spawnTile)
            {
                GridPosition gp = new GridPosition(pos.x, pos.y);
                spawnPositions[spawnTile.entryDirection] = gp;
                found++;

                Vector3 worldPos = spawnPointsTilemap.GetCellCenterWorld(pos);
                Debug.Log($"[RoomSpawnPointReader] ✓ SpawnPoint: {spawnTile.entryDirection} " +
                          $"cell {pos} → GridPos {gp} world {worldPos}");
            }
        }

        Debug.Log($"[RoomSpawnPointReader] {gameObject.name} — " +
                  $"{totalTiles} tiles scanned, {found} SpawnPointTiles found.");
        isInitialized = true;
    }

    public GridPosition GetSpawnPosition(LevelGenerator.Direction entryDirection, RoomGrid roomGrid)
    {
        EnsureScanned();

        if (spawnPositions.TryGetValue(entryDirection, out GridPosition pos))
        {
            Debug.Log($"[RoomSpawnPointReader] Returning spawn {entryDirection} → {pos}");
            return pos;
        }

        Debug.LogWarning($"[RoomSpawnPointReader] No spawn for {entryDirection} in {gameObject.name}");
        return new GridPosition(roomGrid.GetWidth() / 2, roomGrid.GetHeight() / 2);
    }

    public bool HasSpawnPoint(LevelGenerator.Direction entryDirection)
    {
        EnsureScanned();
        return spawnPositions.ContainsKey(entryDirection);
    }

    public Dictionary<LevelGenerator.Direction, GridPosition> GetAllSpawnPoints()
    {
        EnsureScanned();
        return new Dictionary<LevelGenerator.Direction, GridPosition>(spawnPositions);
    }
}
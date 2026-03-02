// RoomSpawnPointReader.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Reads the SpawnPoints tilemap layer in this room and provides
/// GridPositions for each entry direction.
/// 
/// Setup:
/// 1. Add a child Tilemap named "SpawnPoints" to your room prefab
/// 2. Create SpawnPointTile assets (one per direction) via Assets > Create > Tiles > SpawnPoint Tile
/// 3. Paint the correct SpawnPointTile on the cell where the player should land for each door
/// 4. The TilemapRenderer on SpawnPoints should be disabled at runtime (or set alpha to 0)
/// </summary>
public class RoomSpawnPointReader : MonoBehaviour
{
    [Header("Tilemap Reference")]
    [Tooltip("The 'SpawnPoints' tilemap layer in this room prefab.")]
    [SerializeField] private Tilemap spawnPointsTilemap;

    [Header("Runtime Debug")]
    [SerializeField] private bool showDebugLogs = true;

    private Dictionary<LevelGenerator.Direction, GridPosition> spawnPositions
        = new Dictionary<LevelGenerator.Direction, GridPosition>();

    private bool isInitialized = false;

    // ── Initialization ─────────────────────────────────────────────────────

    /// <summary>Call this after the room tilemap is initialized.</summary>
    public void Initialize()
    {
        spawnPositions.Clear();

        if (spawnPointsTilemap == null)
        {
            // Try to find it by name in children
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
            Debug.LogWarning($"[RoomSpawnPointReader] No 'SpawnPoints' tilemap found in {gameObject.name}.");
            return;
        }

        TilemapRenderer renderer = spawnPointsTilemap.GetComponent<TilemapRenderer>();
        if (renderer != null) renderer.enabled = false;

        BoundsInt bounds = spawnPointsTilemap.cellBounds;
        for (int x = bounds.xMin; x < bounds.xMax; x++)
        {
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                Vector3Int cellPos = new Vector3Int(x, y, 0);
                TileBase tile = spawnPointsTilemap.GetTile(cellPos);

                if (tile is SpawnPointTile spawnTile)
                {
                    GridPosition gp = new GridPosition(cellPos.x, cellPos.y);
                    spawnPositions[spawnTile.entryDirection] = gp;

                    if (showDebugLogs)
                        Debug.Log($"[RoomSpawnPointReader] Found spawn point: entry from {spawnTile.entryDirection} at {gp}");
                }
            }
        }

        isInitialized = true;
        Debug.Log($"[RoomSpawnPointReader] {gameObject.name} — {spawnPositions.Count} spawn points loaded.");
    }


    public GridPosition GetSpawnPosition(LevelGenerator.Direction entryDirection, RoomGrid roomGrid)
    {
        if (isInitialized && spawnPositions.TryGetValue(entryDirection, out GridPosition pos))
            return pos;

        Debug.LogWarning($"[RoomSpawnPointReader] No spawn point for {entryDirection} in {gameObject.name}. Using center.");
        return new GridPosition(roomGrid.GetWidth() / 2, roomGrid.GetHeight() / 2);
    }

    public bool HasSpawnPoint(LevelGenerator.Direction entryDirection)
        => spawnPositions.ContainsKey(entryDirection);

    public Dictionary<LevelGenerator.Direction, GridPosition> GetAllSpawnPoints()
        => new Dictionary<LevelGenerator.Direction, GridPosition>(spawnPositions);
}
// SpawnPointTile.cs
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// A tile asset that marks a player spawn point for a specific entry direction.
/// Create 4 of these: Assets > Create > Tiles > SpawnPoint Tile
/// Paint them on the SpawnPoints tilemap layer in each room prefab.
/// </summary>
[CreateAssetMenu(fileName = "SpawnPointTile", menuName = "Tiles/SpawnPoint Tile")]
public class SpawnPointTile : Tile
{
    [Tooltip("Which direction the player is coming FROM when they should spawn here.\n" +
             "e.g. North = player entered through the north connection.")]
    public LevelGenerator.Direction entryDirection;

    public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
    {
        base.GetTileData(position, tilemap, ref tileData);
        // Keep the sprite/color you assigned in the Inspector
        // This tile is invisible at runtime — hide the SpawnPoints layer's renderer
    }

#if UNITY_EDITOR
    // Tint the tile in the editor so you can see which direction it represents
    public override void RefreshTile(Vector3Int position, ITilemap tilemap)
    {
        tilemap.RefreshTile(position);
    }
#endif
}
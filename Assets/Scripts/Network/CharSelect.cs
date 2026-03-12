using UnityEngine;

/// <summary>
/// Stores the player's character choice between scenes.
/// Static so it survives scene loads without needing DontDestroyOnLoad.
///
/// USAGE:
///   MainMenuController sets:  CharacterSelection.Index  = selectedCharIndex;
///                             CharacterSelection.Prefab = selectedPrefab;
///   LevelGenerator reads:     CharacterSelection.Index  (existing)
///                             CharacterSelection.Prefab (new — use this to spawn the right prefab)
/// </summary>
public static class CharacterSelection
{
    public static int        Index  { get; set; } = 0;
    public static GameObject Prefab { get; set; } = null;
}
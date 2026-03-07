/// <summary>
/// Stores the player's character choice between scenes.
/// Static so it survives scene loads without needing DontDestroyOnLoad.
/// 
/// USAGE:
///   MainMenuController sets:  CharacterSelection.Index = selectedCharIndex;
///   LevelGenerator reads:     CharacterSelection.Index
/// </summary>
public static class CharacterSelection
{
    public static int Index { get; set; } = 0;
}
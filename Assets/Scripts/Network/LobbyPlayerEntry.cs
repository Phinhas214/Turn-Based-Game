using TMPro;
using UnityEngine;

/// <summary>
/// Displays one player's info in the lobby list.
///
/// PREFAB SETUP:
///   LobbyPlayerEntry (root — add this component here)
///     PlayerNameText     (TextMeshProUGUI)  — name + "(You)" for local player
///     CharacterClassText (TextMeshProUGUI)  — selected class name
///     ReadyIcon          (GameObject)       — shown when player is ready
///     HostCrown          (GameObject)       — shown for the host
///
/// Character class order MUST match NetworkedLevelGenerator.playerPrefabs:
///   0 = Knight, 1 = Rogue, 2 = Mage, 3 = Cleric
/// </summary>
public class LobbyPlayerEntry : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private TextMeshProUGUI characterClassText;
    [SerializeField] private GameObject      readyIcon;
    [SerializeField] private GameObject      hostCrown;

    private static readonly string[] ClassNames = { "Knight", "Rogue", "Mage", "Cleric" };

    public void Setup(SessionPlayerInfo info)
    {
        if (playerNameText != null)
            playerNameText.text = info.IsLocalPlayer
                ? $"{info.DisplayName} (You)"
                : info.DisplayName;

        if (characterClassText != null)
            characterClassText.text = (info.CharacterIndex >= 0 && info.CharacterIndex < ClassNames.Length)
                ? ClassNames[info.CharacterIndex]
                : "Selecting...";

        if (readyIcon != null)
            readyIcon.SetActive(info.IsReady);

        if (hostCrown != null)
            hostCrown.SetActive(info.IsHost);
    }
}
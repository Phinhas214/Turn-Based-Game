using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One player row in the lobby list.
///
/// PREFAB HIERARCHY:
///   PlayerSlot                    ← root, has this component + Horizontal Layout Group
///     PlayerNameText              ← TextMeshProUGUI  "PlayerName (You)"
///     CharacterNameText           ← TextMeshProUGUI  "Knight", "Selecting..." etc.
///     ReadyIndicator              ← Image  green=ready, grey=not ready
///     HostCrown                   ← GameObject  visible only for host
///     YouIndicator                ← GameObject  visible only for local player
/// </summary>
public class PlayerSlotUI : MonoBehaviour
{
    [Header("Display Elements")]
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private TextMeshProUGUI characterNameText;
    [SerializeField] private Image           readyIndicator;
    [SerializeField] private GameObject      hostCrown;
    [SerializeField] private GameObject      youIndicator;

    [Header("Ready Colors")]
    [SerializeField] private Color readyColor    = new Color(0.2f, 0.9f, 0.3f, 1f);
    [SerializeField] private Color notReadyColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);

    private static readonly string[] ClassNames = { "Knight", "Rogue", "Mage", "Cleric" };

    // ─────────────────────────────────────────────────────────────────────

    public void Setup(SessionPlayerInfo info)
    {
        if (playerNameText != null)
            playerNameText.text = info.IsLocalPlayer
                ? $"{info.DisplayName} (You)"
                : info.DisplayName;

        if (characterNameText != null)
            characterNameText.text = (info.CharacterIndex >= 0 && info.CharacterIndex < ClassNames.Length)
                ? ClassNames[info.CharacterIndex]
                : "Selecting...";

        if (readyIndicator != null)
            readyIndicator.color = info.IsReady ? readyColor : notReadyColor;

        if (hostCrown != null)
            hostCrown.SetActive(info.IsHost);

        if (youIndicator != null)
            youIndicator.SetActive(info.IsLocalPlayer);
    }

    // Legacy overload kept for compatibility
    public void SetData(SessionPlayerInfo info, string characterName)
    {
        Setup(info);
        if (characterNameText != null && !string.IsNullOrEmpty(characterName))
            characterNameText.text = characterName;
    }
}
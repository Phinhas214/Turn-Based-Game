using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Represents one player's row in the lobby player list.
///
/// PREFAB SETUP:
///   PlayerSlot (GameObject with PlayerSlotUI)
///     - PlayerNameText     (TextMeshProUGUI)
///     - CharacterNameText  (TextMeshProUGUI)
///     - ReadyIndicator     (Image — green when ready, grey when not)
///     - HostCrown          (GameObject — visible only for host)
///     - YouIndicator       (GameObject — visible only for local player)
///
/// All fields are optional — missing references are silently skipped.
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
    [SerializeField] private Color readyColor   = new Color(0.2f, 0.9f, 0.3f, 1f);
    [SerializeField] private Color notReadyColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);

    // ─────────────────────────────────────────────────────────────────────

    public void SetData(LobbyPlayerInfo info, string characterName)
    {
        if (playerNameText != null)
        {
            playerNameText.text = info.IsLocalPlayer
                ? $"{info.DisplayName} (You)"
                : info.DisplayName;
        }

        if (characterNameText != null)
            characterNameText.text = characterName;

        if (readyIndicator != null)
            readyIndicator.color = info.IsReady ? readyColor : notReadyColor;

        if (hostCrown != null)
            hostCrown.SetActive(info.IsHost);

        if (youIndicator != null)
            youIndicator.SetActive(info.IsLocalPlayer);
    }
}
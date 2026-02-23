using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Drives one action button in the action bar UI.
/// - Shows the action name
/// - Shows stamina cost if the action is a CombatAction
/// - Greys out and disables the button when the unit can't afford the action
/// - Highlights when the action is currently selected
/// </summary>
public class ActionButtonUI : MonoBehaviour
{
    // -------------------------------------------------------------------------
    [Header("Core References")]
    [Tooltip("The clickable button.")]
    [SerializeField] private Button button;

    [Tooltip("Label showing the action name.")]
    [SerializeField] private TextMeshProUGUI actionNameText;

    [Tooltip("Outline or background image shown when this action is selected.")]
    [SerializeField] private GameObject selectedGameObject;

    // -------------------------------------------------------------------------
    [Header("Stamina Cost Display")]
    [Tooltip("Root object for the stamina cost badge. Hidden for actions with no cost.")]
    [SerializeField] private GameObject staminaCostRoot;

    [Tooltip("Text showing the stamina cost number.")]
    [SerializeField] private TextMeshProUGUI staminaCostText;

    [Tooltip("Icon next to the stamina cost number (optional).")]
    [SerializeField] private Image staminaIcon;

    // -------------------------------------------------------------------------
    [Header("Affordability Visuals")]
    [Tooltip("Alpha applied to the button contents when the unit can't afford the action.")]
    [Range(0f, 1f)]
    [SerializeField] private float unaffordableAlpha = 0.4f;

    [Tooltip("CanvasGroup on this button used to dim it when unaffordable. " +
             "If left empty one will be added automatically.")]
    [SerializeField] private CanvasGroup canvasGroup;

    // -------------------------------------------------------------------------
    // Runtime
    // -------------------------------------------------------------------------
    private BaseAction baseAction;

    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
    }

    // -------------------------------------------------------------------------
    //  Public API
    // -------------------------------------------------------------------------

    public void SetBaseAction(BaseAction action)
    {
        baseAction = action;

        // Action name
        if (actionNameText != null)
            actionNameText.text = action.GetActionName().ToUpper();

        // Wire button click
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => UnitActionSystem.Instance.SetSelectedAction(baseAction));

        // Refresh stamina cost badge
        RefreshStaminaCost();
        RefreshAffordability();
    }

    /// <summary>
    /// Called by UnitActionSystemUI every frame (or on relevant events)
    /// to keep the selected highlight and affordability state current.
    /// </summary>
    public void UpdateSelectedVisual()
    {
        if (UnitActionSystem.Instance == null) return;

        // Selected highlight
        bool isSelected = UnitActionSystem.Instance.GetSelectedAction() == baseAction;
        if (selectedGameObject != null)
            selectedGameObject.SetActive(isSelected);

        // Affordability (stamina may have changed since last frame)
        RefreshAffordability();
    }

    // -------------------------------------------------------------------------
    //  Private helpers
    // -------------------------------------------------------------------------

    private void RefreshStaminaCost()
    {
        if (staminaCostRoot == null) return;

        if (baseAction is CombatAction combatAction && combatAction.ActionData != null)
        {
            int cost = combatAction.ActionData.staminaCost;
            staminaCostRoot.SetActive(cost > 0);
            if (staminaCostText != null)
                staminaCostText.text = cost.ToString();
        }
        else if (baseAction is SpinAction)
        {
            // SpinAction hard-codes a cost of 1 — expose it here
            staminaCostRoot.SetActive(true);
            if (staminaCostText != null)
                staminaCostText.text = "1";
        }
        else
        {
            staminaCostRoot.SetActive(false);
        }
    }

    private void RefreshAffordability()
    {
        if (canvasGroup == null) return;

        bool canAfford = CanAffordAction();
        canvasGroup.alpha = canAfford ? 1f : unaffordableAlpha;
        // Keep interactable so the player can still click (UnitActionSystem
        // will log "not enough stamina" and no-op). Set to false if you prefer
        // to hard-block the click entirely.
        button.interactable = canAfford;
    }

    private bool CanAffordAction()
    {
        // MoveAction: affordable as long as there's at least 1 stamina
        if (baseAction is MoveAction moveAction)
        {
            PlayerStats stats = baseAction.GetComponent<PlayerStats>();
            return stats == null || stats.currentStamina > 0;
        }

        // CombatAction: use its built-in CanAfford()
        if (baseAction is CombatAction combatAction)
            return combatAction.CanAfford();

        // Everything else (SpinAction etc.): always show as affordable
        return true;
    }
}
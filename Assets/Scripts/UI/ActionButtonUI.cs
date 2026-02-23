using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;
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
    [Header("Core References")]
    [Tooltip("The clickable button.")]
    [SerializeField] private Button button;

    [Tooltip("Label showing the action name.")]
    [FormerlySerializedAs("textMeshPro")]          // ← keeps your existing prefab reference
    [SerializeField] private TextMeshProUGUI actionNameText;

    [Tooltip("Outline or background image shown when this action is selected.")]
    [SerializeField] private GameObject selectedGameObject;

    [Header("Stamina Cost Display")]
    [Tooltip("Root object for the stamina cost badge. Hidden for actions with no cost.")]
    [SerializeField] private GameObject staminaCostRoot;

    [Tooltip("Text showing the stamina cost number.")]
    [SerializeField] private TextMeshProUGUI staminaCostText;

    [Tooltip("Icon next to the stamina cost number (optional).")]
    [SerializeField] private Image staminaIcon;

    [Header("Affordability Visuals")]
    [Tooltip("Alpha applied to the button when the unit can't afford the action.")]
    [Range(0f, 1f)]
    [SerializeField] private float unaffordableAlpha = 0.4f;

    [Tooltip("CanvasGroup used to dim the button when unaffordable. Auto-added if left empty.")]
    [SerializeField] private CanvasGroup canvasGroup;

    // ── Runtime ───────────────────────────────────────────────────────────
    private BaseAction baseAction;

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void SetBaseAction(BaseAction action)
    {
        baseAction = action;

        if (actionNameText != null)
            actionNameText.text = action.GetActionName().ToUpper();

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => UnitActionSystem.Instance.SetSelectedAction(baseAction));

        RefreshStaminaCost();
        RefreshAffordability();
    }

    public void UpdateSelectedVisual()
    {
        if (UnitActionSystem.Instance == null) return;

        if (selectedGameObject != null)
            selectedGameObject.SetActive(UnitActionSystem.Instance.GetSelectedAction() == baseAction);

        RefreshAffordability();
    }

    // ── Private helpers ───────────────────────────────────────────────────

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
        button.interactable = canAfford;
    }

    private bool CanAffordAction()
    {
        if (baseAction is MoveAction)
        {
            PlayerStats stats = baseAction.GetComponent<PlayerStats>();
            return stats == null || stats.currentStamina > 0;
        }

        if (baseAction is CombatAction combatAction)
            return combatAction.CanAfford();

        return true;
    }
}
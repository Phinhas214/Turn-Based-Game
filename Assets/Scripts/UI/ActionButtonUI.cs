using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;
using TMPro;

/// <summary>
/// Drives one action button in the action bar.
/// Works with both UnitActionSystem (single-player) and
/// NetworkedUnitActionSystem (multiplayer).
/// </summary>
public class ActionButtonUI : MonoBehaviour
{
    [Header("Core References")]
    [SerializeField] private Button button;

    [FormerlySerializedAs("textMeshPro")]
    [SerializeField] private TextMeshProUGUI actionNameText;

    [SerializeField] private GameObject selectedGameObject;

    [Header("Stamina Cost Display")]
    [SerializeField] private GameObject      staminaCostRoot;
    [SerializeField] private TextMeshProUGUI staminaCostText;
    [SerializeField] private Image           staminaIcon;

    [Header("Affordability Visuals")]
    [Range(0f, 1f)]
    [SerializeField] private float   unaffordableAlpha = 0.4f;
    [SerializeField] private CanvasGroup canvasGroup;

    private BaseAction baseAction;

    private void Awake()
    {
        // Ensure we have a CanvasGroup for the alpha dimming effect
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
    }

    public void SetBaseAction(BaseAction action)
    {
        baseAction = action;

        if (actionNameText != null)
            actionNameText.text = action.GetActionName().ToUpper();

        // Setup button listener to route to the correct system (SP or MP)
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => SetActionOnActiveSystem(baseAction));

        RefreshStaminaCost();
        RefreshAffordability();
    }

    public void UpdateSelectedVisual()
    {
        BaseAction currentAction = GetSelectedActionFromActiveSystem();

        if (selectedGameObject != null)
            selectedGameObject.SetActive(currentAction == baseAction);

        // Re-check if we can still afford this whenever the selection changes
        RefreshAffordability();
    }

    // ── Routing Helpers ───────────────────────────────────────────────────

    private void SetActionOnActiveSystem(BaseAction action)
    {
        if (NetworkedUnitActionSystem.Instance != null)
            NetworkedUnitActionSystem.Instance.SetSelectedAction(action);
        else
            UnitActionSystem.Instance?.SetSelectedAction(action);
    }

    private BaseAction GetSelectedActionFromActiveSystem()
    {
        if (NetworkedUnitActionSystem.Instance != null)
            return NetworkedUnitActionSystem.Instance.GetSelectedAction();

        return UnitActionSystem.Instance?.GetSelectedAction();
    }

    // ── UI Refresh Logic ──────────────────────────────────────────────────

    private void RefreshStaminaCost()
    {
        if (staminaCostRoot == null) return;

        // 1. Check Combat Actions (uses ActionData values)
        if (baseAction is CombatAction combatAction && combatAction.ActionData != null)
        {
            int cost = combatAction.ActionData.staminaCost;
            staminaCostRoot.SetActive(cost > 0);

            if (staminaCostText != null)
                staminaCostText.text = cost.ToString();
            
            return;
        }

        // 2. Check Move Action (Sam's 1-stamina rule)
        if (baseAction is MoveAction)
        {
            staminaCostRoot.SetActive(true);

            if (staminaCostText != null)
                staminaCostText.text = "1";

            return;
        }

        // 3. Default: Hide cost if neither of the above
        staminaCostRoot.SetActive(false);
    }

    private void RefreshAffordability()
    {
        if (canvasGroup == null || button == null) return;

        bool canAfford = CanAffordAction();
        
        // Dim the button and disable interaction if unaffordable
        canvasGroup.alpha = canAfford ? 1f : unaffordableAlpha;
        button.interactable = canAfford;
    }

    private bool CanAffordAction()
    {
        // For MoveAction, we check the unit's PlayerStats directly
        if (baseAction is MoveAction)
        {
            PlayerStats stats = baseAction.GetComponent<PlayerStats>();
            // If no stats found, assume it's free/allowed for safety
            return stats == null || stats.currentStamina > 0;
        }

        // For CombatAction, we use the built-in affordability check
        if (baseAction is CombatAction combatAction)
            return combatAction.CanAfford();

        return true;
    }
}
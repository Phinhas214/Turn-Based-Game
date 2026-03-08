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
    [SerializeField] private float       unaffordableAlpha = 0.4f;
    [SerializeField] private CanvasGroup canvasGroup;

    private BaseAction baseAction;

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
    }

    public void SetBaseAction(BaseAction action)
    {
        baseAction = action;

        if (actionNameText != null)
            actionNameText.text = action.GetActionName().ToUpper();

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

        RefreshAffordability();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Routes SetSelectedAction to whichever system is active.</summary>
    private void SetActionOnActiveSystem(BaseAction action)
    {
        if (NetworkedUnitActionSystem.Instance != null)
            NetworkedUnitActionSystem.Instance.SetSelectedAction(action);
        else
            UnitActionSystem.Instance?.SetSelectedAction(action);
    }

    /// <summary>Returns the currently selected action from whichever system is active.</summary>
    private BaseAction GetSelectedActionFromActiveSystem()
    {
        if (NetworkedUnitActionSystem.Instance != null)
            return NetworkedUnitActionSystem.Instance.GetSelectedAction();

        return UnitActionSystem.Instance?.GetSelectedAction();
    }

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
        else
        {
            staminaCostRoot.SetActive(false);
        }
    }

    private void RefreshAffordability()
    {
        if (canvasGroup == null) return;

        bool canAfford = CanAffordAction();
        canvasGroup.alpha   = canAfford ? 1f : unaffordableAlpha;
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
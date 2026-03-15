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

    [Header("Description")]
    [Tooltip("Text field that shows dice damage / attack info.")]
    [SerializeField] private TextMeshProUGUI actionDescText;

    [Header("Icon")]
    [Tooltip("Assign the Image that should display the action's icon sprite. Leave empty to skip.")]
    [SerializeField] private Image actionIcon;

    [Header("Stamina Cost Display")]
    [SerializeField] private GameObject staminaCostRoot;
    [SerializeField] private TextMeshProUGUI staminaCostText;
    [SerializeField] private Image staminaIcon;

    [Header("Affordability Visuals")]
    [Range(0f, 1f)]
    [SerializeField] private float unaffordableAlpha = 0.4f;
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

        RefreshIcon();
        RefreshDescription();
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

    private void RefreshIcon()
    {
        if (actionIcon == null) return;

        if (baseAction is CombatAction combatAction && combatAction.ActionData?.icon != null)
        {
            actionIcon.sprite = combatAction.ActionData.icon;
            actionIcon.enabled = true;
        }
        else
        {
            actionIcon.enabled = false;
        }
    }

    private void RefreshDescription()
    {
        if (actionDescText == null)
            return;

        if (baseAction is CombatAction combatAction && combatAction.ActionData != null)
        {
            var data = combatAction.ActionData;

            if (data.useDiceDamage)
            {
                int sides = (int)data.dieType;

                string diceText = $"{data.diceCount}d{sides}";

                if (data.flatBonus > 0)
                    diceText += $" <color=#6CFF6C>+{data.flatBonus}</color>";
                else if (data.flatBonus < 0)
                    diceText += $" {data.flatBonus}";

                actionDescText.text = diceText;
            }
            else
            {
                actionDescText.text = data.baseDamage.ToString();
            }

            return;
        }

        if (baseAction is MoveAction)
        {
            actionDescText.text = "—";
            return;
        }

        actionDescText.text = "";
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

            return;
        }

        if (baseAction is MoveAction)
        {
            staminaCostRoot.SetActive(true);

            if (staminaCostText != null)
                staminaCostText.text = "1";

            return;
        }

        staminaCostRoot.SetActive(false);
    }

    private void RefreshAffordability()
    {
        if (canvasGroup == null || button == null) return;

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
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class TurnSystemUI : MonoBehaviour
{
    [SerializeField] private Button endTurnBtn;
    [SerializeField] private TextMeshProUGUI turnNumberText;
    [SerializeField] private GameObject endTurnFlashOverlay;
    [SerializeField] private float flashInterval = 0.3f;

    private PlayerStats playerStats;
    private Coroutine flashRoutine;

    private void Start()
    {
        endTurnBtn.onClick.AddListener(() =>
        {
            TurnSystem.Instance.NextTurn();
        });

        TurnSystem.Instance.OnTurnChanged += TurnSystem_OnTurnChanged;
        UpdateTurnText();

        Unit unit = FindFirstObjectByType<Unit>();
        if (unit != null)
            playerStats = unit.GetComponent<PlayerStats>();
    }

    void Update()
    {
        if (!playerStats || TurnSystem.Instance == null)
            return;
        Debug.Log($"Stamina: {playerStats.currentStamina}, PlayerTurn: {TurnSystem.Instance.IsPlayerTurn}");

        bool shouldFlash =
            TurnSystem.Instance.IsPlayerTurn &&
            playerStats.currentStamina == 0;

        if (shouldFlash && flashRoutine == null)
            flashRoutine = StartCoroutine(FlashEndTurn());

        if (!shouldFlash && flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
            endTurnFlashOverlay.SetActive(false);
        }
    }

    System.Collections.IEnumerator FlashEndTurn()
    {
        while (true)
        {
            endTurnFlashOverlay.SetActive(true);
            yield return new WaitForSeconds(flashInterval);
            endTurnFlashOverlay.SetActive(false);
            yield return new WaitForSeconds(flashInterval);
        }
    }

    private void TurnSystem_OnTurnChanged(object sender, EventArgs e)
    {
        UpdateTurnText();
    }
    
    private void UpdateTurnText()
    {
        turnNumberText.text = "TURN " + TurnSystem.Instance.GetTrunNumber();
    }


}

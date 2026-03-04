using UnityEngine;

public class PlayerTurnCanvasGate : MonoBehaviour
{
    [Header("Player-Only Canvases")]
    [SerializeField] private Canvas[] playerCanvases;

    void Start()
    {
        if (TurnSystem.Instance == null)
        {
            Debug.LogError("[CanvasGate] TurnSystem not found!");
            return;
        }

        TurnSystem.Instance.OnPlayerTurnBegin += EnableCanvases;
        TurnSystem.Instance.OnEnemyPhaseBegin += DisableCanvases;

        // Set initial state correctly
        SetCanvases(TurnSystem.Instance.IsPlayerTurn);
    }

    void OnDisable()
    {
        if (TurnSystem.Instance == null) return;

        TurnSystem.Instance.OnPlayerTurnBegin -= EnableCanvases;
        TurnSystem.Instance.OnEnemyPhaseBegin -= DisableCanvases;
    }

    void EnableCanvases()
    {
        Debug.Log("[CanvasGate] EnableCanvases");
        SetCanvases(true);
    }

    void DisableCanvases()
    {
        Debug.Log("[CanvasGate] DisableCanvases");
        SetCanvases(false);
    }

    void SetCanvases(bool enabled)
    {
        foreach (var canvas in playerCanvases)
        {
            if (canvas != null)
                canvas.enabled = enabled;
        }
    }
} 
using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Attach to each PLAYER PREFAB.
///
/// When a player submits end-turn, a "Ready" indicator flashes above them,
/// visible to ALL other players. Stops flashing and hides when the new turn begins.
///
/// SETUP:
///   1. Add this script to your player prefab.
///   2. Create a child GameObject called "ReadyIndicator" above the player (Y ~1.8).
///      Use a SpriteRenderer with a checkmark sprite, OR a world-space Canvas with "✓".
///   3. Assign it to the 'readyIndicator' field.
///   4. Optionally assign a SpriteRenderer to 'indicatorRenderer' for color flashing,
///      or leave it null to just toggle the GameObject on/off.
/// </summary>
public class PlayerReadyIndicator : NetworkBehaviour
{
    [Header("Indicator Object")]
    [Tooltip("Child GameObject shown above the player when ready (sprite or canvas).")]
    [SerializeField] private GameObject readyIndicator;

    [Tooltip("Optional SpriteRenderer on the indicator for color/alpha flashing.")]
    [SerializeField] private SpriteRenderer indicatorRenderer;

    [Header("Flash Settings")]
    [SerializeField] private float flashOnDuration  = 0.35f;
    [SerializeField] private float flashOffDuration = 0.2f;
    [SerializeField] private Color flashColor       = new Color(0.3f, 1f, 0.3f, 1f); // bright green
    [SerializeField] private Color dimColor         = new Color(0.3f, 1f, 0.3f, 0.2f);

    // Replicated so late-joining clients see the correct state immediately
    private NetworkVariable<bool> isReady = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Coroutine flashRoutine;

    // ─────────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        isReady.OnValueChanged += OnReadyStateChanged;

        // Apply current state immediately (handles late join)
        ApplyReadyState(isReady.Value);

        if (MultiplayerTurnSystem.Instance != null)
            MultiplayerTurnSystem.Instance.OnPlayerTurnBegin += OnPlayerTurnBegin;
    }

    public override void OnNetworkDespawn()
    {
        isReady.OnValueChanged -= OnReadyStateChanged;
        if (MultiplayerTurnSystem.Instance != null)
            MultiplayerTurnSystem.Instance.OnPlayerTurnBegin -= OnPlayerTurnBegin;
    }

    // ─────────────────────────────────────────────────────────────────────
    // State change
    // ─────────────────────────────────────────────────────────────────────

    private void OnReadyStateChanged(bool oldVal, bool newVal) => ApplyReadyState(newVal);

    private void OnPlayerTurnBegin()
    {
        if (IsServer) isReady.Value = false;
    }

    /// <summary>Called by MultiplayerTurnSystem on the server.</summary>
    public void SetReady(bool ready)
    {
        if (IsServer) isReady.Value = ready;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Visual — runs on ALL clients due to NetworkVariable replication
    // ─────────────────────────────────────────────────────────────────────

    private void ApplyReadyState(bool ready)
    {
        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }

        if (readyIndicator != null)
            readyIndicator.SetActive(ready);

        if (ready)
        {
            // Reset color before flashing
            SetColor(flashColor);
            flashRoutine = StartCoroutine(FlashRoutine());
        }
        else
        {
            SetColor(flashColor);
        }
    }

    private IEnumerator FlashRoutine()
    {
        while (true)
        {
            SetColor(flashColor);
            yield return new WaitForSeconds(flashOnDuration);
            SetColor(dimColor);
            yield return new WaitForSeconds(flashOffDuration);
        }
    }

    private void SetColor(Color color)
    {
        if (indicatorRenderer != null)
            indicatorRenderer.color = color;
    }
}
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tracks win/lose state.
///
/// Win  — player enters End room (checked every time SetCurrentRoom is called)
/// Lose — player HealthComponent reaches 0
/// R    — restart by reloading the scene
/// </summary>
public class GameStateManager : MonoBehaviour
{
    public static GameStateManager Instance { get; private set; }

    [Header("Restart")]
    [SerializeField] private KeyCode restartKey = KeyCode.R;

    // ── Events ─────────────────────────────────────────────────────────────
    public event Action OnGameWon;
    public event Action OnGameLost;
    public event Action OnGameRestarted;

    // ── State ──────────────────────────────────────────────────────────────
    public enum GameState { Playing, Won, Lost }
    public GameState CurrentState { get; private set; } = GameState.Playing;

    private HealthComponent playerHealth;
    private bool subscribedToPlayer = false;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady  += OnLevelReady;
        RoomManager.OnAnyRoomChanged += OnRoomChanged;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady  -= OnLevelReady;
        RoomManager.OnAnyRoomChanged -= OnRoomChanged;
        UnsubscribeFromPlayer();
    }

    private void Update()
    {
        if (Input.GetKeyDown(restartKey))
        {
            RestartGame();
            return;
        }

        // Poll every frame to subscribe to the player if we haven't yet.
        // This handles the case where the player spawns after OnLevelReady fires.
        if (!subscribedToPlayer)
            TrySubscribeToPlayer();

        // Also poll the player's alive state directly as a fallback.
        // Catches cases where OnDeath fires but the event wasn't hooked up yet.
        if (CurrentState == GameState.Playing && subscribedToPlayer)
        {
            if (playerHealth != null && playerHealth.IsDead)
                HandlePlayerDeath();
        }
    }

    // ── Level ready ────────────────────────────────────────────────────────

    private void OnLevelReady()
    {
        CurrentState       = GameState.Playing;
        subscribedToPlayer = false;
        TrySubscribeToPlayer();

        // Also check the current room right now in case the start room
        // was already set before we subscribed to OnAnyRoomChanged
        if (RoomManager.Instance != null)
        {
            var currentRoom = RoomManager.Instance.GetCurrentRoom();
            if (currentRoom != null)
                CheckForWin(currentRoom);
        }

        Debug.Log("[GameStateManager] Level ready — watching for win/lose.");
    }

    // ── Player subscription ────────────────────────────────────────────────

    private void TrySubscribeToPlayer()
    {
        Unit player = FindFirstObjectByType<Unit>();
        if (player == null) return;

        HealthComponent hc = player.GetComponent<HealthComponent>();
        if (hc == null)
        {
            Debug.LogWarning("[GameStateManager] Player has no HealthComponent.");
            return;
        }

        if (hc == playerHealth) return; // already subscribed to this one

        // Unsubscribe from old reference if it changed
        UnsubscribeFromPlayer();

        playerHealth = hc;
        playerHealth.OnDeath += HandlePlayerDeath;
        subscribedToPlayer   = true;

        Debug.Log($"[GameStateManager] Subscribed to player health ({playerHealth.MaxHealth} HP).");
    }

    private void UnsubscribeFromPlayer()
    {
        if (playerHealth != null)
        {
            playerHealth.OnDeath -= HandlePlayerDeath;
            playerHealth          = null;
        }
        subscribedToPlayer = false;
    }

    // ── Death ──────────────────────────────────────────────────────────────

    private void HandlePlayerDeath()
    {
        if (CurrentState != GameState.Playing) return;

        CurrentState = GameState.Lost;
        Debug.Log("[GameStateManager] Player died — GAME OVER.");
        OnGameLost?.Invoke();
    }

    // ── Room changed ───────────────────────────────────────────────────────

    private void OnRoomChanged(LevelGenerator.PlacedRoom newRoom)
    {
        CheckForWin(newRoom);
    }

    private void CheckForWin(LevelGenerator.PlacedRoom room)
    {
        if (CurrentState != GameState.Playing) return;
        if (room == null) return;

        if (room.prefabData != null &&
            room.prefabData.roomType == LevelGenerator.RoomType.End)
        {
            CurrentState = GameState.Won;
            Debug.Log("[GameStateManager] Player reached End room — YOU WIN.");
            OnGameWon?.Invoke();
        }
    }

    // ── Restart ────────────────────────────────────────────────────────────

    public void RestartGame()
    {
        Debug.Log("[GameStateManager] Restarting scene.");
        OnGameRestarted?.Invoke();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
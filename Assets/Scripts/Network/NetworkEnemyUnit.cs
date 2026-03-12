using System;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkedHealthComponent))]
public class NetworkedEnemyUnit : NetworkBehaviour, IHasHealth
{
    [Header("Stats")]
    [SerializeField] private EnemyStats stats;
    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    private NetworkVariable<int> netGridX = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> netGridZ = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> netIsDead = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private GridPosition             gridPosition;
    private RoomGrid                 currentRoomGrid;
    private NetworkedHealthComponent health;
    private bool                     isInitialized = false;
    private bool                     hasDied       = false;
    private int                      turnsWaited   = 0;

    public event Action<NetworkedEnemyUnit> OnEnemyDied;

    public EnemyStats               Stats           => stats;
    public NetworkedHealthComponent Health          => health;
    public GridPosition             GridPosition    => gridPosition;
    public RoomGrid                 CurrentRoomGrid => currentRoomGrid;
    public bool                     IsInitialized   => isInitialized;
    public bool                     IsDead          => netIsDead.Value;

    public int GetMaxHealth() => stats != null ? stats.maxHealth : 100;

    private void Awake()
    {
        health = GetComponent<NetworkedHealthComponent>();
    }

    public override void OnNetworkSpawn()
    {
        health.OnDeath           += HandleDeath;
        netIsDead.OnValueChanged += OnIsDeadChanged;
    }

    public override void OnNetworkDespawn()
    {
        health.OnDeath           -= HandleDeath;
        netIsDead.OnValueChanged -= OnIsDeadChanged;

        if (IsServer && currentRoomGrid != null && isInitialized)
        {
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetCompatUnit());
            NetworkedEnemyManager.Instance?.UnregisterEnemy(this);
        }
    }

    private void OnDestroy()
    {
        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetCompatUnit());
    }

    public void PlaceOnGrid(RoomGrid roomGrid, GridPosition position)
    {
        if (!IsServer) return;

        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetCompatUnit());

        currentRoomGrid    = roomGrid;
        gridPosition       = position;
        transform.position = roomGrid.GetWorldPosition(position);
        roomGrid.AddEnemyAtGridPosition(position, GetCompatUnit());
        netGridX.Value = position.x;
        netGridZ.Value = position.z;
        isInitialized  = true;

        if (showDebugLogs) Debug.Log($"[NetworkedEnemyUnit] {stats?.enemyName} placed at {position}");
    }

    public void MoveToPosition(GridPosition newPosition)
    {
        if (!IsServer || !isInitialized) return;

        currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetCompatUnit());
        gridPosition = newPosition;
        currentRoomGrid.AddEnemyAtGridPosition(newPosition, GetCompatUnit());

        Vector3 newWorldPos    = currentRoomGrid.GetWorldPosition(newPosition);
        transform.position     = newWorldPos;
        netGridX.Value         = newPosition.x;
        netGridZ.Value         = newPosition.z;

        SyncMoveToClientsClientRpc(newWorldPos.x, newWorldPos.y, newWorldPos.z,
                                   newPosition.x, newPosition.z,
                                   currentRoomGrid.gameObject.name);

        if (showDebugLogs) Debug.Log($"[NetworkedEnemyUnit] {stats?.enemyName} moved to {newPosition}");
    }

    public bool CanActThisTurn()
    {
        if (IsDead) return false;
        if (turnsWaited < stats.turnsBeforeFirstAction) { turnsWaited++; return false; }
        return true;
    }

    private void HandleDeath()
    {
        if (!IsServer || hasDied) return;
        hasDied = true;

        netIsDead.Value = true;

        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetCompatUnit());

        OnEnemyDied?.Invoke(this);
        NetworkedEnemyManager.Instance?.UnregisterEnemy(this);
        StartCoroutine(DespawnAfterDelay(0.5f));

        if (showDebugLogs) Debug.Log($"[NetworkedEnemyUnit] {stats?.enemyName} died.");
    }

    private System.Collections.IEnumerator DespawnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (IsServer && TryGetComponent<NetworkObject>(out var netObj))
            netObj.Despawn(true);
    }

    [ClientRpc]
    public void SyncRoomToClientsClientRpc(float wx, float wy, float wz, int gx, int gz,
                                           string roomName = "")
    {
        if (IsServer) return;

        Vector3      worldPos = new Vector3(wx, wy, wz);
        GridPosition pos      = new GridPosition(gx, gz);

        RoomGrid room = null;
        if (!string.IsNullOrEmpty(roomName))
            foreach (RoomGrid rg in FindObjectsByType<RoomGrid>(FindObjectsSortMode.None))
                if (rg.gameObject.name == roomName) { room = rg; break; }

        if (room == null) room = LevelGrid.Instance?.GetRoomAtPosition(worldPos);

        if (room == null)
        {
            Debug.LogWarning($"[NetworkedEnemyUnit] SyncRoomToClients: no room '{roomName}' at {worldPos}");
            return;
        }

        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetCompatUnit());

        currentRoomGrid    = room;
        gridPosition       = pos;
        transform.position = worldPos;
        room.AddEnemyAtGridPosition(pos, GetCompatUnit());
        isInitialized = true;
    }

    [ClientRpc]
    public void SyncMoveToClientsClientRpc(float wx, float wy, float wz, int gx, int gz,
                                           string roomName = "")
    {
        if (IsServer) return;

        if (currentRoomGrid == null && !string.IsNullOrEmpty(roomName))
            foreach (RoomGrid rg in FindObjectsByType<RoomGrid>(FindObjectsSortMode.None))
                if (rg.gameObject.name == roomName) { currentRoomGrid = rg; break; }

        if (currentRoomGrid == null && LevelGrid.Instance != null)
            currentRoomGrid = LevelGrid.Instance.GetRoomAtPosition(new Vector3(wx, wy, wz));

        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, GetCompatUnit());

        GridPosition newPos = new GridPosition(gx, gz);
        gridPosition        = newPos;
        transform.position  = new Vector3(wx, wy, wz);

        if (currentRoomGrid != null)
        {
            currentRoomGrid.AddEnemyAtGridPosition(newPos, GetCompatUnit());
            isInitialized = true;
        }
    }

    private void OnIsDeadChanged(bool oldVal, bool newVal) { }

    // Option A: keep EnemyUnit on the prefab as a tag component alongside NetworkedEnemyUnit.
    // Option B: change RoomGrid to accept NetworkedEnemyUnit and return 'this' here.
    private EnemyUnit GetCompatUnit()
    {
        EnemyUnit eu = GetComponent<EnemyUnit>();
#if UNITY_EDITOR
        if (eu == null)
            Debug.LogError($"[NetworkedEnemyUnit] '{gameObject.name}' missing EnemyUnit component.");
#endif
        return eu;
    }
}
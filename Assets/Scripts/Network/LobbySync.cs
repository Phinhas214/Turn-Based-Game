using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Syncs character selection, ready state, and phase transitions to all clients
/// using NGO NetworkVariables and ServerRpcs — no UGS session needed.
///
/// SETUP:
///   - Create a GameObject in your Menu scene called "LobbySync"
///   - Add NetworkObject + this component to it
///   - Make it a prefab and add to NetworkManager's NetworkPrefabs list
///   - Host spawns it automatically on connect (see Awake below)
/// </summary>
public class LobbySync : NetworkBehaviour
{
    public static LobbySync Instance { get; private set; }

    public event Action           OnCharSelectPhaseStarted;
    public event Action<ulong[]>  OnPlayerDataUpdated;

    private NetworkVariable<bool> charSelectPhaseActive = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Dictionary<ulong, int>  characterIndexMap = new Dictionary<ulong, int>();
    private Dictionary<ulong, bool> readyMap          = new Dictionary<ulong, bool>();

    public ulong LocalClientId => NetworkManager.Singleton?.LocalClientId ?? 0;

    private void Awake()
    {
        // Don't destroy here — NGO manages this object's lifecycle
        // Instance is set in OnNetworkSpawn instead
    }

    public override void OnNetworkSpawn()
    {
        // If another LobbySync already exists (carried over from menu scene), destroy this one.
        if (Instance != null && Instance != this)
        {
            NetworkObject.Despawn();
            return;
        }

        Instance = this;

        // Persist into the game scene so NetworkedLevelGenerator can read character selections.
        DontDestroyOnLoad(gameObject);

        charSelectPhaseActive.OnValueChanged += OnCharSelectPhaseChanged;
        RegisterClientServerRpc(NetworkManager.Singleton.LocalClientId);
    }

    public override void OnNetworkDespawn()
    {
        charSelectPhaseActive.OnValueChanged -= OnCharSelectPhaseChanged;
        if (Instance == this) Instance = null;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RegisterClientServerRpc(ulong clientId)
    {
        if (!characterIndexMap.ContainsKey(clientId)) characterIndexMap[clientId] = 0;
        if (!readyMap.ContainsKey(clientId))          readyMap[clientId]          = false;
        BroadcastPlayerData();
    }

    public void SetMyCharacter(int index)
        => SetCharacterServerRpc(NetworkManager.Singleton.LocalClientId, index);

    [ServerRpc(RequireOwnership = false)]
    private void SetCharacterServerRpc(ulong clientId, int index)
    {
        characterIndexMap[clientId] = index;
        BroadcastPlayerData();
    }

    public int GetCharacterIndex(ulong clientId)
    {
        characterIndexMap.TryGetValue(clientId, out int idx);
        return idx;
    }

    public void SetMyReady(bool ready)
        => SetReadyServerRpc(NetworkManager.Singleton.LocalClientId, ready);

    [ServerRpc(RequireOwnership = false)]
    private void SetReadyServerRpc(ulong clientId, bool ready)
    {
        readyMap[clientId] = ready;
        BroadcastPlayerData();
    }

    public bool IsReady(ulong clientId)
    {
        readyMap.TryGetValue(clientId, out bool ready);
        return ready;
    }

    public bool AllPlayersReady()
    {
        if (readyMap.Count == 0) return false;
        foreach (var kvp in readyMap)
            if (!kvp.Value) return false;
        return true;
    }

    public void BeginCharSelectPhase()
    {
        if (IsServer) charSelectPhaseActive.Value = true;
    }

    private void OnCharSelectPhaseChanged(bool oldVal, bool newVal)
    {
        if (newVal) OnCharSelectPhaseStarted?.Invoke();
    }

    private void BroadcastPlayerData()
    {
        var ids     = new List<ulong>(characterIndexMap.Keys);
        var indices = new int[ids.Count];
        var readys  = new bool[ids.Count];

        for (int i = 0; i < ids.Count; i++)
        {
            indices[i] = characterIndexMap[ids[i]];
            readyMap.TryGetValue(ids[i], out readys[i]);
        }

        UpdateClientsClientRpc(ids.ToArray(), indices, readys);
    }

    [ClientRpc]
    private void UpdateClientsClientRpc(ulong[] ids, int[] charIndices, bool[] readyStates)
    {
        characterIndexMap.Clear();
        readyMap.Clear();
        for (int i = 0; i < ids.Length; i++)
        {
            characterIndexMap[ids[i]] = charIndices[i];
            readyMap[ids[i]]          = readyStates[i];
        }
        OnPlayerDataUpdated?.Invoke(ids);
    }
}
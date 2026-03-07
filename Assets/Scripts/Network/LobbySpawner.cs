using Unity.Netcode;
using UnityEngine;


public class LobbySpawner : MonoBehaviour
{
    [SerializeField] private GameObject lobbySyncPrefab;

    private void OnEnable()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        else
            NetworkManager.OnInstantiated += OnNetworkManagerInstantiated;
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
    }

    private void OnNetworkManagerInstantiated(NetworkManager nm)
    {
        nm.OnServerStarted += OnServerStarted;
        NetworkManager.OnInstantiated -= OnNetworkManagerInstantiated;
    }

    private void OnServerStarted()
    {
        if (!NetworkManager.Singleton.IsHost) return;
        if (LobbySync.Instance != null) return; // already spawned

        if (lobbySyncPrefab == null)
        {
            Debug.LogError("[LobbySpawner] lobbySyncPrefab not assigned!");
            return;
        }

        GameObject go = Instantiate(lobbySyncPrefab);
        NetworkObject netObj = go.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError("[LobbySpawner] LobbySync prefab missing NetworkObject!");
            Destroy(go);
            return;
        }
        netObj.Spawn();
        Debug.Log("[LobbySpawner] LobbySync spawned.");
    }
}
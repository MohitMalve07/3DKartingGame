using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Server-side spawn manager that assigns fixed positions to players based on join order.
/// Attach this to a GameObject in the Game scene.
/// 
/// IMPORTANT — Unity Netcode Setup:
/// 1. Set the Player Prefab on the NetworkManager.
/// 2. UNCHECK "Auto-Spawn Player Prefab" on the NetworkManager so this script controls spawning.
/// 3. Ensure the Player Prefab has a NetworkObject component.
/// </summary>
public class SpawnManager : NetworkBehaviour
{
    [Header("Spawn Settings")]
    [Tooltip("The player prefab to spawn (must have a NetworkObject component)")]
    public GameObject playerPrefab;

    [Tooltip("Maximum number of players allowed in the session")]
    public int maxPlayers = 4;

    // Fixed spawn positions indexed by join order (0-based)
    private readonly Vector3[] spawnPositions = new Vector3[]
    {
        new Vector3(223.830002f, 8.369382501f, -143.339996f),   // Player 1
        new Vector3(220.330002f, 8.369382501f, -148.217575f),   // Player 2
        new Vector3(223.580002f, 8.369382501f, -152.967575f),   // Player 3
        new Vector3(220.330002f, 8.369382501f, -157.967575f)    // Player 4
    };

    // Default rotation for all spawned players
    private readonly Quaternion spawnRotation = Quaternion.identity;

    // Tracks the next available spawn index
    private int nextSpawnIndex = 0;

    // Tracks spawned player objects for cleanup
    private readonly Dictionary<ulong, NetworkObject> spawnedPlayers = new Dictionary<ulong, NetworkObject>();

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        // Subscribe to connection events so we handle spawning ourselves
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        // Spawn players for ALL currently-connected clients.
        // This handles both the host AND any clients that connected
        // before this scene finished loading (race condition fix).
        // The duplicate-spawn guard in OnClientConnected prevents double-spawning.
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            OnClientConnected(clientId);
        }
    }

    /// <summary>
    /// Called on the server when a client connects. Spawns a player at the next available position.
    /// </summary>
    private void OnClientConnected(ulong clientId)
    {
        // Prevent duplicate spawns (e.g. host calling this twice)
        if (spawnedPlayers.ContainsKey(clientId))
        {
            Debug.LogWarning($"[SpawnManager] Client {clientId} already has a spawned player. Skipping.");
            return;
        }

        // Enforce max player limit
        if (nextSpawnIndex >= maxPlayers)
        {
            Debug.LogWarning($"[SpawnManager] Max players ({maxPlayers}) reached. Disconnecting client {clientId}.");
            NetworkManager.Singleton.DisconnectClient(clientId);
            return;
        }

        if (playerPrefab == null)
        {
            Debug.LogError("[SpawnManager] Player prefab is not assigned!");
            return;
        }

        // Spawn the player at the assigned position
        Vector3 position = spawnPositions[nextSpawnIndex];
        GameObject playerInstance = Instantiate(playerPrefab, position, spawnRotation);

        NetworkObject networkObject = playerInstance.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Debug.LogError("[SpawnManager] Player prefab is missing a NetworkObject component!");
            Destroy(playerInstance);
            return;
        }

        // Spawn as player object — this assigns ownership to the connecting client
        networkObject.SpawnAsPlayerObject(clientId);

        // Track the spawned player
        spawnedPlayers[clientId] = networkObject;

        Debug.Log($"[SpawnManager] Player {nextSpawnIndex + 1} (Client {clientId}) spawned at {position}");
        nextSpawnIndex++;
    }

    /// <summary>
    /// Called on the server when a client disconnects. Cleans up their player object.
    /// </summary>
    private void OnClientDisconnected(ulong clientId)
    {
        if (spawnedPlayers.TryGetValue(clientId, out NetworkObject networkObject))
        {
            if (networkObject != null && networkObject.IsSpawned)
            {
                networkObject.Despawn();
                Destroy(networkObject.gameObject);
            }

            spawnedPlayers.Remove(clientId);
            Debug.Log($"[SpawnManager] Cleaned up player for Client {clientId}.");
        }
    }

    public override void OnNetworkDespawn()
    {
        // Unsubscribe to prevent memory leaks
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }
}

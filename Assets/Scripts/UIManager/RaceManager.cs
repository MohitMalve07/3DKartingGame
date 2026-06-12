using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Unity.Collections;

/// <summary>
/// Server-authoritative race manager that tracks finish order and broadcasts results.
/// Must be a NetworkObject in the Game scene.
/// 
/// SETUP:
/// 1. Create an empty GameObject in the Game scene named "RaceManager".
/// 2. Add NetworkObject and this script.
/// 3. Assign the resultsUI reference (or it will be found by name "Results").
/// </summary>
public class RaceManager : NetworkBehaviour
{
    [Header("References")]
    [Tooltip("The ResultsUI component. Found by name 'Results' if not assigned.")]
    public ResultsUI resultsUI;

    [Header("Settings")]
    [Tooltip("Delay (seconds) after the last player finishes before showing results.")]
    public float resultsDelay = 2f;

    /// <summary>
    /// Singleton for easy access from RaceProgress.
    /// </summary>
    public static RaceManager Instance { get; private set; }

    /// <summary>
    /// Clean structure pairing a client ID with their display name at the time they finished.
    /// </summary>
    [System.Serializable]
    public struct FinishData
    {
        public ulong clientId;
        public string username;

        public FinishData(ulong clientId, string username)
        {
            this.clientId = clientId;
            this.username = username;
        }
    }

    // Server-side: ordered list of finish data as players complete the race
    private List<FinishData> finishOrder = new List<FinishData>();
    private bool resultsShown = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[RaceManager] Duplicate found — destroying this one.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        // Find ResultsUI if not assigned
        if (resultsUI == null)
        {
            // Try finding by component
            resultsUI = FindObjectOfType<ResultsUI>(true);

            if (resultsUI == null)
            {
                Debug.LogWarning("[RaceManager] ResultsUI not found. Results won't display.");
            }
            else
            {
                Debug.Log($"[RaceManager] Found ResultsUI on '{resultsUI.gameObject.name}'.");
            }
        }

        // Ensure results panel starts hidden
        if (resultsUI != null)
        {
            resultsUI.HideResults();
        }
    }

    /// <summary>
    /// Called by RaceProgress on the server when a player completes the race.
    /// Only runs on the server. Tracks finish order.
    /// </summary>
    public void OnPlayerFinished(ulong clientId)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[RaceManager] OnPlayerFinished called on non-server. Ignoring.");
            return;
        }

        // Prevent duplicate entries
        foreach (var entry in finishOrder)
        {
            if (entry.clientId == clientId)
            {
                Debug.Log($"[RaceManager] Client {clientId} already finished. Ignoring duplicate.");
                return;
            }
        }

        // Retrieve the synchronized username from NetworkPlayer
        string username = NetworkPlayer.GetUsername(clientId);
        if (string.IsNullOrEmpty(username))
            username = $"Player {clientId + 1}";

        finishOrder.Add(new FinishData(clientId, username));
        int position = finishOrder.Count;
        Debug.Log($"[RaceManager] '{username}' (Client {clientId}) finished in position {position}!");

        // Disable movement for the finished player
        DisablePlayerMovementClientRpc(clientId);

        // Check if all connected players have finished
        int totalPlayers = NetworkManager.Singleton.ConnectedClientsIds.Count;
        Debug.Log($"[RaceManager] {finishOrder.Count}/{totalPlayers} players finished.");

        if (finishOrder.Count >= totalPlayers)
        {
            Debug.Log("[RaceManager] All players finished! Broadcasting results...");
            Invoke(nameof(BroadcastResults), resultsDelay);
        }
    }

    /// <summary>
    /// Server broadcasts final results to all clients.
    /// </summary>
    private void BroadcastResults()
    {
        if (resultsShown) return;
        resultsShown = true;

        // Pad to 4 entries for RPC (ulong.MaxValue = empty slot, empty string = no name)
        ulong[] paddedIds = new ulong[4];
        FixedString32Bytes[] paddedNames = new FixedString32Bytes[4];

        for (int i = 0; i < 4; i++)
        {
            if (i < finishOrder.Count)
            {
                paddedIds[i] = finishOrder[i].clientId;
                paddedNames[i] = new FixedString32Bytes(finishOrder[i].username);
            }
            else
            {
                paddedIds[i] = ulong.MaxValue;
                paddedNames[i] = new FixedString32Bytes();
            }
        }

        ShowResultsClientRpc(
            paddedIds[0], paddedIds[1], paddedIds[2], paddedIds[3],
            paddedNames[0], paddedNames[1], paddedNames[2], paddedNames[3]);
    }

    /// <summary>
    /// ClientRpc: show the results UI on all clients with the final standings.
    /// Sends both client IDs and their usernames for display.
    /// Using individual params instead of arrays for Netcode compatibility.
    /// </summary>
    [ClientRpc]
    private void ShowResultsClientRpc(
        ulong first, ulong second, ulong third, ulong fourth,
        FixedString32Bytes name1, FixedString32Bytes name2,
        FixedString32Bytes name3, FixedString32Bytes name4)
    {
        ulong[] results = new ulong[] { first, second, third, fourth };
        string[] usernames = new string[]
        {
            name1.ToString(), name2.ToString(),
            name3.ToString(), name4.ToString()
        };

        Debug.Log($"[RaceManager] Received results: 1st={name1}({first}), 2nd={name2}({second}), 3rd={name3}({third}), 4th={name4}({fourth})");

        if (resultsUI != null)
        {
            resultsUI.ShowResults(results, usernames);
        }
        else
        {
            // Retry finding ResultsUI (may not have been available at spawn)
            resultsUI = FindObjectOfType<ResultsUI>(true);
            if (resultsUI != null)
            {
                resultsUI.ShowResults(results, usernames);
            }
            else
            {
                Debug.LogError("[RaceManager] ResultsUI not found! Cannot display results.");
            }
        }
    }

    /// <summary>
    /// ClientRpc: disable movement for the specified player on all clients.
    /// </summary>
    [ClientRpc]
    private void DisablePlayerMovementClientRpc(ulong clientId)
    {
        // Find the player object for this clientId
        foreach (var networkObj in FindObjectsOfType<NetworkObject>())
        {
            if (networkObj.IsPlayerObject && networkObj.OwnerClientId == clientId)
            {
                PlayerMovement movement = networkObj.GetComponent<PlayerMovement>();
                if (movement != null)
                {
                    movement.ResetMoveForce();
                    movement.enabled = false;
                    Debug.Log($"[RaceManager] Disabled movement for Client {clientId}.");
                }

                // Also stop the rigidbody
                Rigidbody rb = networkObj.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                break;
            }
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}

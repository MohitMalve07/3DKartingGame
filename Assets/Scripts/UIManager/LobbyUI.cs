using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using UnityEngine.SceneManagement;

/// <summary>
/// Dynamic multiplayer lobby UI that shows connected players and lets the host start the game.
/// This is an inactive panel in the Main Menu scene — activated by NetworkUI when hosting/joining.
/// 
/// Uses a polling approach to sync the player list with NetworkManager.ConnectedClientsIds,
/// which is the most robust way to handle timing issues with connection callbacks.
/// 
/// SETUP:
/// 1. Create a Lobby panel (inactive by default) in the Main Menu Canvas.
/// 2. Inside it, add a Scroll View with a Content container.
/// 3. Create a PlayerEntry prefab (a UI element with a Legacy Text child).
/// 4. Assign all references in the Inspector.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Prefab with a Text component for each player entry.")]
    public GameObject playerEntryPrefab;

    [Tooltip("The Content transform inside the Scroll View where player entries are spawned.")]
    public Transform contentParent;

    [Tooltip("Button to start the game. Only visible and usable by the Host.")]
    public Button startGameButton;

    [Tooltip("Optional text showing player count (e.g., 'Players: 2/4').")]
    public Text playerCountText;

    [Header("Settings")]
    [Tooltip("Name of the gameplay scene to load when the host starts the game.")]
    public string gameSceneName = "Game";

    [Tooltip("Maximum number of players allowed in the lobby.")]
    public int maxPlayers = 4;

    [Tooltip("How often (in seconds) to refresh the player list.")]
    public float refreshInterval = 0.5f;

    // Dictionary mapping clientId → instantiated UI entry
    private Dictionary<ulong, GameObject> playerEntries = new Dictionary<ulong, GameObject>();

    private bool isHost = false;
    private float refreshTimer = 0f;

    /// <summary>
    /// Called by NetworkUI after activating this panel.
    /// Receives the host/client role directly.
    /// </summary>
    public void Initialize(bool hostMode)
    {
        isHost = hostMode;

        // Configure Start Game button
        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(isHost);

            if (isHost)
            {
                startGameButton.onClick.RemoveAllListeners();
                startGameButton.onClick.AddListener(OnStartGameClicked);
                Debug.Log("[LobbyUI] Start Game button enabled for Host.");
            }
        }
        else
        {
            Debug.LogWarning("[LobbyUI] startGameButton is not assigned in Inspector!");
        }

        // Validate required references
        if (playerEntryPrefab == null)
            Debug.LogError("[LobbyUI] playerEntryPrefab is not assigned in Inspector!");
        if (contentParent == null)
            Debug.LogError("[LobbyUI] contentParent is not assigned in Inspector!");

        // Force an immediate refresh
        RefreshPlayerList();

        Debug.Log($"[LobbyUI] Initialized as {(isHost ? "HOST" : "CLIENT")}.");
    }

    private void Update()
    {
        // Periodically refresh the player list to catch connection changes
        refreshTimer += Time.deltaTime;
        if (refreshTimer >= refreshInterval)
        {
            refreshTimer = 0f;
            RefreshPlayerList();
        }
    }

    /// <summary>
    /// Syncs the UI entries with the current NetworkManager.ConnectedClientsIds.
    /// Adds entries for new players, removes entries for disconnected players.
    /// </summary>
    private void RefreshPlayerList()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            return;

        // Get current connected client IDs
        // On the Host/Server, this is the full list
        // On Clients, we use a different approach
        HashSet<ulong> currentIds = new HashSet<ulong>();

        if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost)
        {
            // Server has the authoritative list
            foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
            {
                currentIds.Add(id);
            }
        }
        else
        {
            // Client: add own ID + count of connected clients from spawned player objects
            // The client always knows its own ID
            currentIds.Add(NetworkManager.Singleton.LocalClientId);

            // Find all spawned player objects to discover other players
            foreach (var networkObj in FindObjectsOfType<Unity.Netcode.NetworkObject>())
            {
                if (networkObj.IsPlayerObject)
                {
                    currentIds.Add(networkObj.OwnerClientId);
                }
            }
        }

        // Remove entries for players no longer connected
        var toRemove = new List<ulong>();
        foreach (ulong existingId in playerEntries.Keys)
        {
            if (!currentIds.Contains(existingId))
            {
                toRemove.Add(existingId);
            }
        }
        foreach (ulong id in toRemove)
        {
            RemovePlayerEntry(id);
        }

        // Add entries for new players
        foreach (ulong id in currentIds)
        {
            if (!playerEntries.ContainsKey(id))
            {
                AddPlayerEntry(id);
            }
        }

        // Always re-render names — usernames sync via NetworkVariable and
        // may arrive after the entry was first created (GetUsername returns ""
        // until the ServerRpc round-trip completes).
        RefreshPlayerNumbers();
    }

    private void AddPlayerEntry(ulong clientId)
    {
        if (playerEntries.ContainsKey(clientId))
            return;

        if (playerEntryPrefab == null || contentParent == null)
        {
            Debug.LogError("[LobbyUI] Cannot add entry — prefab or content parent not assigned!");
            return;
        }

        GameObject entry = Instantiate(playerEntryPrefab, contentParent);
        entry.name = $"PlayerEntry_{clientId}";

        playerEntries[clientId] = entry;

        RefreshPlayerNumbers();
        UpdatePlayerCount();

        Debug.Log($"[LobbyUI] Added entry for Client {clientId}. Total: {playerEntries.Count}");
    }

    private void RemovePlayerEntry(ulong clientId)
    {
        if (playerEntries.TryGetValue(clientId, out GameObject entry))
        {
            if (entry != null) Destroy(entry);
            playerEntries.Remove(clientId);

            RefreshPlayerNumbers();
            UpdatePlayerCount();

            Debug.Log($"[LobbyUI] Removed entry for Client {clientId}. Total: {playerEntries.Count}");
        }
    }

    /// <summary>
    /// Re-numbers all entries with synchronized usernames: "1. Username (Host)", "2. Username", etc.
    /// Falls back to "Player N" if the NetworkPlayer username hasn't synced yet.
    /// </summary>
    private void RefreshPlayerNumbers()
    {
        int playerNumber = 1;

        var sortedIds = new List<ulong>(playerEntries.Keys);
        sortedIds.Sort();

        foreach (ulong clientId in sortedIds)
        {
            if (playerEntries.TryGetValue(clientId, out GameObject entry) && entry != null)
            {
                Text entryText = entry.GetComponentInChildren<Text>();
                if (entryText != null)
                {
                    // Look up the synced username from NetworkPlayer
                    string username = NetworkPlayer.GetUsername(clientId);
                    if (string.IsNullOrEmpty(username))
                        username = $"Player {playerNumber}";

                    string hostLabel = (clientId == NetworkManager.ServerClientId) ? " (Host)" : "";
                    entryText.text = $"{playerNumber}. {username}{hostLabel}";
                }

                entry.transform.SetSiblingIndex(playerNumber - 1);
                playerNumber++;
            }
        }
    }

    private void UpdatePlayerCount()
    {
        if (playerCountText != null)
        {
            playerCountText.text = $"Players: {playerEntries.Count}/{maxPlayers}";
        }
    }

    public void OnStartGameClicked()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[LobbyUI] NetworkManager is null!");
            return;
        }

        if (!NetworkManager.Singleton.IsHost && !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("[LobbyUI] Only the host can start the game.");
            return;
        }

        Debug.Log($"[LobbyUI] Host starting game. Loading '{gameSceneName}'...");

        // LoadScene via NetworkManager syncs all connected clients to the new scene
        NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
    }

    private void OnDisable()
    {
        // Clean up all spawned entries
        foreach (var entry in playerEntries.Values)
        {
            if (entry != null) Destroy(entry);
        }
        playerEntries.Clear();

        if (startGameButton != null)
        {
            startGameButton.onClick.RemoveAllListeners();
        }
    }
}

using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

/// <summary>
/// Handles the Main Menu network buttons for hosting and joining a multiplayer game
/// using Unity Relay for NAT-traversal and WebGL compatibility.
///
/// Flow:
/// 1. Host clicks "Host" → creates Relay allocation → gets join code → starts host
///    → shows LobbyHostSide panel with the join code displayed.
/// 2. Client clicks "Join" → shows LobbyClientSide panel with a code input field.
/// 3. Client enters code and clicks "Join Game" → joins Relay allocation → starts client
///    → hides LobbyClientSide → shows LobbyHostSide panel.
/// 4. In the LobbyHostSide panel, host clicks "Start Game" → loads Game scene for everyone.
///
/// SETUP:
/// 1. Link your project in Edit → Project Settings → Services.
/// 2. Enable Relay in the Unity Dashboard (dashboard.unity3d.com).
/// 3. On the NetworkManager's UnityTransport component, enable "Use WebSockets" and "Use Encryption".
/// 4. Assign all Inspector references on this component.
/// </summary>
public class NetworkUI : MonoBehaviour
{
    [Header("Main Menu Buttons")]
    [Tooltip("Button that starts the game as Host (in the main menu).")]
    public Button hostButton;

    [Tooltip("Button that opens the Join panel (in the main menu).")]
    public Button joinButton;

    [Header("Username")]
    [Tooltip("InputField where the player types their display name before hosting/joining.")]
    public InputField usernameInput;

    /// <summary>
    /// The locally entered username, stored statically so NetworkPlayer can read it
    /// after the player prefab spawns. NOT used for network sync — just a temp buffer.
    /// </summary>
    public static string LocalUsername = "";

    [Header("Panel References")]
    [Tooltip("The main menu panel (Buttons, Title, etc.) to hide when entering the lobby.")]
    public GameObject mainMenuPanel;

    [Tooltip("The host-side lobby panel showing player list, join code, and Start Game button.")]
    public GameObject lobbyHostPanel;

    [Tooltip("The client-side lobby panel showing the join code input field.")]
    public GameObject lobbyClientPanel;

    [Header("Relay UI — Host Side")]
    [Tooltip("Text element inside LobbyHostSide that displays the Relay join code.")]
    public Text joinCodeDisplayText;

    [Header("Relay UI — Client Side")]
    [Tooltip("InputField inside LobbyClientSide where the client enters the join code.")]
    public InputField joinCodeInputField;

    [Tooltip("Button inside LobbyClientSide that triggers the Relay join.")]
    public Button clientJoinButton;

    [Header("Relay Settings")]
    [Tooltip("Maximum number of OTHER players (not counting the host). 3 means 4 total.")]
    public int maxRelayConnections = 3;

    // Tracks whether Unity Services have been initialized this session
    private static bool servicesInitialized = false;

    // The generated Relay join code, stored for reference
    private string currentJoinCode = "";

    private void Start()
    {
        // Ensure the NetworkManager persists when we load scenes
        if (NetworkManager.Singleton != null)
        {
            DontDestroyOnLoad(NetworkManager.Singleton.gameObject);
        }

        // Wire up button listeners
        if (hostButton != null) hostButton.onClick.AddListener(OnHostClicked);
        if (joinButton != null) joinButton.onClick.AddListener(OnJoinClicked);
        if (clientJoinButton != null) clientJoinButton.onClick.AddListener(OnClientJoinClicked);

        // Ensure lobby panels start hidden
        if (lobbyHostPanel != null) lobbyHostPanel.SetActive(false);
        if (lobbyClientPanel != null) lobbyClientPanel.SetActive(false);
    }

    // ─────────────────────────────────────────────
    //  Unity Services Initialization
    // ─────────────────────────────────────────────

    /// <summary>
    /// Initializes Unity Gaming Services and signs in anonymously.
    /// Safe to call multiple times — only runs once per session.
    /// </summary>
    private async System.Threading.Tasks.Task InitializeServicesAsync()
    {
        if (!servicesInitialized)
        {
            Debug.Log("[NetworkUI] Initializing Unity Services...");
            await UnityServices.InitializeAsync();
            servicesInitialized = true;
            Debug.Log("[NetworkUI] Unity Services initialized successfully.");
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            Debug.Log("[NetworkUI] Signing in anonymously...");
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log($"[NetworkUI] Signed in. Player ID: {AuthenticationService.Instance.PlayerId}");
        }
    }

    // ─────────────────────────────────────────────
    //  HOST Flow
    // ─────────────────────────────────────────────

    /// <summary>
    /// Host button clicked: create Relay allocation, get join code, configure transport, start host.
    /// </summary>
    private async void OnHostClicked()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[NetworkUI] NetworkManager.Singleton is null.");
            return;
        }

        // Prevent double-clicks
        SetButtonsInteractable(false);
        CaptureUsername();

        try
        {
            // 1. Initialize Unity Services + Authentication
            await InitializeServicesAsync();

            // 2. Create Relay allocation (maxRelayConnections = slots for OTHER players)
            Debug.Log($"[NetworkUI] Creating Relay allocation for {maxRelayConnections} connections...");
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxRelayConnections);
            Debug.Log($"[NetworkUI] Relay allocation created. AllocationId: {allocation.AllocationId}");

            // 3. Get the join code
            currentJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log($"[NetworkUI] Relay join code: {currentJoinCode}");

            // 4. Configure UnityTransport with Relay data (wss for WebGL compatibility)
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("[NetworkUI] UnityTransport component not found on NetworkManager!");
                SetButtonsInteractable(true);
                return;
            }

            var relayServerData = AllocationUtils.ToRelayServerData(allocation, "wss");
            transport.SetRelayServerData(relayServerData);
            Debug.Log("[NetworkUI] UnityTransport configured with Relay server data (wss).");

            // 5. Start Host
            bool started = NetworkManager.Singleton.StartHost();

            if (started)
            {
                Debug.Log($"[NetworkUI] Host started as '{LocalUsername}' via Relay. Join code: {currentJoinCode}");
                ShowLobby(true);
            }
            else
            {
                Debug.LogError("[NetworkUI] Failed to start Host after Relay allocation.");
                SetButtonsInteractable(true);
            }
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"[NetworkUI] Relay error during host: {e.Message}");
            SetButtonsInteractable(true);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[NetworkUI] Unexpected error during host: {e.Message}");
            SetButtonsInteractable(true);
        }
    }

    // ─────────────────────────────────────────────
    //  CLIENT Flow — Step 1: Show code input panel
    // ─────────────────────────────────────────────

    /// <summary>
    /// Main menu Join button: shows the LobbyClientSide panel where the player enters a code.
    /// Does NOT connect yet — that happens when they click "Join Game" inside the panel.
    /// </summary>
    private void OnJoinClicked()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);

        if (lobbyClientPanel != null)
        {
            lobbyClientPanel.SetActive(true);
            Debug.Log("[NetworkUI] Showing client join panel. Waiting for join code input...");
        }
        else
        {
            Debug.LogError("[NetworkUI] lobbyClientPanel is not assigned in Inspector!");
        }
    }

    // ─────────────────────────────────────────────
    //  CLIENT Flow — Step 2: Connect via Relay
    // ─────────────────────────────────────────────

    /// <summary>
    /// "Join Game" button inside LobbyClientSide: reads the join code, connects via Relay.
    /// </summary>
    private async void OnClientJoinClicked()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[NetworkUI] NetworkManager.Singleton is null.");
            return;
        }

        // Read and validate the join code
        string joinCode = (joinCodeInputField != null) ? joinCodeInputField.text.Trim().ToUpper() : "";

        if (string.IsNullOrEmpty(joinCode))
        {
            Debug.LogError("[NetworkUI] Join code is empty. Please enter a valid code.");
            return;
        }

        // Prevent double-clicks
        SetButtonsInteractable(false);
        CaptureUsername();

        try
        {
            // 1. Initialize Unity Services + Authentication
            await InitializeServicesAsync();

            // 2. Join the Relay allocation using the code
            Debug.Log($"[NetworkUI] Joining Relay with code: {joinCode}...");
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            Debug.Log($"[NetworkUI] Joined Relay allocation. AllocationId: {joinAllocation.AllocationId}");

            // 3. Configure UnityTransport with Relay data (wss for WebGL compatibility)
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("[NetworkUI] UnityTransport component not found on NetworkManager!");
                SetButtonsInteractable(true);
                return;
            }

            var relayServerData = AllocationUtils.ToRelayServerData(joinAllocation, "wss");
            transport.SetRelayServerData(relayServerData);
            Debug.Log("[NetworkUI] UnityTransport configured with Relay server data (wss).");

            // 4. Start Client
            bool started = NetworkManager.Singleton.StartClient();

            if (started)
            {
                Debug.Log($"[NetworkUI] Client started as '{LocalUsername}' via Relay.");
                ShowLobby(false);
            }
            else
            {
                Debug.LogError("[NetworkUI] Failed to start Client after Relay join.");
                SetButtonsInteractable(true);
            }
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"[NetworkUI] Relay error during join: {e.Message}");
            SetButtonsInteractable(true);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[NetworkUI] Unexpected error during join: {e.Message}");
            SetButtonsInteractable(true);
        }
    }

    // ─────────────────────────────────────────────
    //  Panel Management
    // ─────────────────────────────────────────────

    /// <summary>
    /// Hides menu/client panels and shows the host-side lobby panel.
    /// Both host and connected clients see LobbyHostSide once connected.
    /// </summary>
    private void ShowLobby(bool isHost)
    {
        // Hide everything else
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (lobbyClientPanel != null) lobbyClientPanel.SetActive(false);

        // Show the host-side lobby (used by both host and client after connecting)
        if (lobbyHostPanel != null)
        {
            lobbyHostPanel.SetActive(true);

            // Tell LobbyUI whether we are the host (controls Start Game button visibility)
            LobbyUI lobbyUI = lobbyHostPanel.GetComponent<LobbyUI>();
            if (lobbyUI != null)
            {
                lobbyUI.Initialize(isHost);
            }
        }

        // Display the join code (host only — clients don't have it)
        if (isHost && joinCodeDisplayText != null)
        {
            joinCodeDisplayText.text = $"JOIN CODE: {currentJoinCode}";
            Debug.Log($"[NetworkUI] Join code displayed: {currentJoinCode}");
        }
    }

    // ─────────────────────────────────────────────
    //  Utilities
    // ─────────────────────────────────────────────

    /// <summary>
    /// Reads the username from the InputField, trims it, and applies a fallback if empty.
    /// </summary>
    private void CaptureUsername()
    {
        string raw = (usernameInput != null) ? usernameInput.text : "";
        raw = raw.Trim();

        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = "Racer" + Random.Range(100, 999);
            Debug.Log($"[NetworkUI] No username entered — using fallback: {raw}");
        }

        // Clamp to 29 chars (FixedString32Bytes max usable UTF-8 length)
        if (raw.Length > 29)
            raw = raw.Substring(0, 29);

        LocalUsername = raw;
    }

    /// <summary>
    /// Enables or disables all connection buttons to prevent double-clicks during async operations.
    /// </summary>
    private void SetButtonsInteractable(bool interactable)
    {
        if (hostButton != null) hostButton.interactable = interactable;
        if (joinButton != null) joinButton.interactable = interactable;
        if (clientJoinButton != null) clientJoinButton.interactable = interactable;
    }

    private void OnDestroy()
    {
        if (hostButton != null) hostButton.onClick.RemoveListener(OnHostClicked);
        if (joinButton != null) joinButton.onClick.RemoveListener(OnJoinClicked);
        if (clientJoinButton != null) clientJoinButton.onClick.RemoveListener(OnClientJoinClicked);
    }
}

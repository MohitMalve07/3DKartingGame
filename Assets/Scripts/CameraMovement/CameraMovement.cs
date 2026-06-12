using UnityEngine;
using Unity.Netcode;
using Unity.Cinemachine;

/// <summary>
/// Attaches to the PLAYER PREFAB ONLY. On spawn, the local owner finds the scene's
/// CinemachineCamera and assigns Follow/LookAt to this player's transform.
/// 
/// IMPORTANT: Do NOT attach this to the Main Camera or any scene camera.
///            This script requires a PlayerMovement component on the same GameObject,
///            which acts as a safety check to prevent accidental misuse.
/// 
/// SETUP:
/// 1. Place a CinemachineCamera in the Game scene (NOT on the player prefab).
/// 2. Attach this script to the root of the player prefab (same GameObject as NetworkObject).
/// 3. No tags or Inspector drag-and-drop needed — resolution is fully automatic.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]  // Prevents attaching to non-player objects
public class CameraMovement : NetworkBehaviour
{
    [Header("Speed-based FOV")]
    public float minFOV = 60f;
    public float maxFOV = 80f;
    public float maxSpeed = 20f;

    // Resolved at runtime — never serialized
    private CinemachineCamera vcam;
    private Rigidbody targetRb;
    private bool isNetworked = false;
    private bool cameraAssigned = false;

    public override void OnNetworkSpawn()
    {
        isNetworked = true;

        // Safety: if this script is somehow on a non-player object, bail out
        if (GetComponent<PlayerMovement>() == null)
        {
            Debug.LogError($"[CameraMovement] This script is on '{gameObject.name}' which has no PlayerMovement. "
                         + "It must only be on the player prefab. Disabling.");
            enabled = false;
            return;
        }

        if (!IsOwner)
        {
            // Non-owners must never touch the camera
            enabled = false;
            return;
        }

        // Owner claims the scene camera
        AssignCamera();
    }

    private void Start()
    {
        // Offline mode: if no network session is running, assign camera immediately
        if (!isNetworked)
        {
            AssignCamera();
        }
    }

    /// <summary>
    /// Finds the scene's CinemachineCamera by component type (not by tag)
    /// and points Follow + LookAt at this player's transform.
    /// If the vcam isn't available yet (scene still loading), returns false to retry later.
    /// </summary>
    private bool AssignCamera()
    {
        if (cameraAssigned) return true;

        targetRb = GetComponent<Rigidbody>();

        vcam = FindAnyObjectByType<CinemachineCamera>();

        if (vcam == null)
        {
            // Scene may still be loading — don't disable, just wait and retry
            Debug.Log("[CameraMovement] CinemachineCamera not found yet. Will retry...");
            return false;
        }

        vcam.Follow = transform;
        vcam.LookAt = transform;
        cameraAssigned = true;

        Debug.Log($"[CameraMovement] Cinemachine vcam '{vcam.gameObject.name}' now following "
                + $"local player (Client {OwnerClientId}). "
                + $"Follow={vcam.Follow.name}, LookAt={vcam.LookAt.name}");
        return true;
    }

    private void Update()
    {
        if (isNetworked && !IsOwner) return;

        // Keep retrying camera assignment until successful
        if (!cameraAssigned)
        {
            AssignCamera();
            return;
        }

        if (vcam == null || targetRb == null) return;

        // Speed-based FOV effect
        float speed = targetRb.linearVelocity.magnitude;
        float t = Mathf.Clamp01(speed / maxSpeed);
        vcam.Lens.FieldOfView = Mathf.Lerp(minFOV, maxFOV, t);
    }
}

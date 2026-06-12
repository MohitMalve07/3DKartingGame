using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Handles player respawning at the last server-validated checkpoint.
/// Reads checkpoint progress from RaceProgress (NetworkVariable) and
/// looks up positions from CheckpointRegistry (scene singleton).
/// 
/// Attach to the player prefab alongside PlayerMovement and RaceProgress.
/// </summary>
public class RespawnSystem : NetworkBehaviour
{
    private Rigidbody rb;
    private PlayerMovement playerMovement;
    private RaceProgress raceProgress;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        playerMovement = GetComponent<PlayerMovement>();
        raceProgress = GetComponent<RaceProgress>();

        Debug.Log("[RespawnSystem] Initialized — using server-validated checkpoints via RaceProgress.");
    }

    void Update()
    {
        // Only the owning client (or offline player) should process respawn input
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (!IsOwner) return;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            Respawn();
        }
    }

    public void Respawn()
    {
        // Determine the respawn checkpoint from server-validated progress
        int checkpointIndex = 0;
        if (raceProgress != null)
        {
            checkpointIndex = raceProgress.CurrentCheckpointIndex.Value;
        }

        // Look up the position from the scene registry
        Vector3 respawnPos;
        Quaternion respawnRot;

        if (CheckpointRegistry.Instance != null &&
            CheckpointRegistry.Instance.TryGetCheckpoint(checkpointIndex, out respawnPos, out respawnRot))
        {
            ApplyRespawn(respawnPos, respawnRot, checkpointIndex);
        }
        else if (checkpointIndex == 0)
        {
            // Fallback: use the player's initial spawn position (stored at Start)
            Debug.LogWarning("[RespawnSystem] CheckpointRegistry not found or index 0 missing. "
                           + "Using current position as fallback.");
        }
        else
        {
            Debug.LogError($"[RespawnSystem] Cannot respawn — checkpoint {checkpointIndex} not found in registry.");
        }
    }

    private void ApplyRespawn(Vector3 position, Quaternion rotation, int checkpointIndex)
    {
        // 1. Zero out all physics velocity
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // 2. Zero out the custom moveForce in PlayerMovement
        if (playerMovement != null)
        {
            playerMovement.ResetMoveForce();
        }

        // 3. Teleport using Rigidbody for physics-safe repositioning
        Vector3 spawnPos = position + Vector3.up * 1f;
        rb.position = spawnPos;
        rb.rotation = rotation;

        // Also sync transform immediately (avoids one-frame desync)
        transform.position = spawnPos;
        transform.rotation = rotation;

        Debug.Log($"[RespawnSystem] Respawned at checkpoint {checkpointIndex} pos: {position}");
    }
}

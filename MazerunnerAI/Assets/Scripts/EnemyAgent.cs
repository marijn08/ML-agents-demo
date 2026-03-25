using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

/// <summary>
/// ML-Agent that learns to chase and catch the player in a maze.
/// Observations: distance to player, direction to player, line of sight, and wall raycasts.
/// Actions: move forward/backward and rotate left/right.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class EnemyAgent : Agent
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float rotateSpeed = 200f;

    [Header("Detection")]
    public float maxDetectionRange = 30f;
    public int wallRayCount = 8;
    public float wallRayLength = 10f;

    [Header("References")]
    public Transform player;
    public GameManager gameManager;

    private Rigidbody rb;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
    }

    public override void OnEpisodeBegin()
    {
        // Reset velocity
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    /// <summary>
    /// Collects observations the agent uses to make decisions.
    /// Total observations: 4 + wallRayCount = 12 (with default 8 rays)
    /// </summary>
    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 toPlayer = player.position - transform.position;
        Vector3 toPlayerFlat = new Vector3(toPlayer.x, 0f, toPlayer.z);

        // 1. Normalized distance to player (0 = on top, 1 = at max range)
        float distance = toPlayerFlat.magnitude;
        sensor.AddObservation(distance / maxDetectionRange);

        // 2. Direction to player relative to enemy's forward (dot product)
        //    +1 = directly ahead, -1 = directly behind
        Vector3 dirToPlayer = toPlayerFlat.normalized;
        float forwardDot = Vector3.Dot(transform.forward, dirToPlayer);
        sensor.AddObservation(forwardDot);

        // 3. Side direction to player (cross product y-component)
        //    +1 = player is to the right, -1 = player is to the left
        float rightDot = Vector3.Dot(transform.right, dirToPlayer);
        sensor.AddObservation(rightDot);

        // 4. Line of sight: can the enemy directly see the player?
        bool hasLineOfSight = false;
        if (distance <= maxDetectionRange)
        {
            if (Physics.Raycast(transform.position, dirToPlayer, out RaycastHit hit, maxDetectionRange))
            {
                hasLineOfSight = hit.transform == player;
            }
        }
        sensor.AddObservation(hasLineOfSight ? 1f : 0f);

        // 5. Wall detection raycasts spread around the enemy
        float angleStep = 360f / wallRayCount;
        for (int i = 0; i < wallRayCount; i++)
        {
            float angle = i * angleStep;
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * transform.forward;

            if (Physics.Raycast(transform.position, dir, out RaycastHit wallHit, wallRayLength))
            {
                // Normalized distance to wall (0 = touching, 1 = at max ray length)
                sensor.AddObservation(wallHit.distance / wallRayLength);
            }
            else
            {
                sensor.AddObservation(1f); // No wall detected = max distance
            }
        }
    }

    /// <summary>
    /// Receives actions from the neural network.
    /// Continuous actions: [0] = forward/backward, [1] = rotation
    /// </summary>
    public override void OnActionReceived(ActionBuffers actions)
    {
        float moveInput = actions.ContinuousActions[0];  // -1 to 1
        float rotateInput = actions.ContinuousActions[1]; // -1 to 1

        // Move forward/backward
        Vector3 move = transform.forward * moveInput * moveSpeed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + move);

        // Rotate left/right
        float rotation = rotateInput * rotateSpeed * Time.fixedDeltaTime;
        Quaternion turnRotation = Quaternion.Euler(0f, rotation, 0f);
        rb.MoveRotation(rb.rotation * turnRotation);

        // Small time penalty to encourage catching the player quickly
        AddReward(-0.001f);

        // Reward for getting closer to the player
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);
        float proximityReward = 1f - (distanceToPlayer / maxDetectionRange);
        AddReward(proximityReward * 0.002f);

        // Bonus reward when the enemy has line of sight of the player
        Vector3 dirToPlayer = (player.position - transform.position).normalized;
        if (Physics.Raycast(transform.position, dirToPlayer, out RaycastHit hit, maxDetectionRange))
        {
            if (hit.transform == player)
            {
                AddReward(0.005f);
            }
        }
    }

    /// <summary>
    /// Allows manual control for testing with WASD keys.
    /// </summary>
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuous = actionsOut.ContinuousActions;
        continuous[0] = 0f;
        continuous[1] = 0f;

        if (Input.GetKey(KeyCode.W)) continuous[0] = 1f;
        if (Input.GetKey(KeyCode.S)) continuous[0] = -1f;
        if (Input.GetKey(KeyCode.A)) continuous[1] = -1f;
        if (Input.GetKey(KeyCode.D)) continuous[1] = 1f;
    }

    /// <summary>
    /// Called when the enemy catches the player.
    /// </summary>
    public void OnCaughtPlayer()
    {
        AddReward(1.0f);
        EndEpisode();
    }

    /// <summary>
    /// Called when time runs out (player survived).
    /// </summary>
    public void OnTimeUp()
    {
        AddReward(-1.0f);
        EndEpisode();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.transform == player)
        {
            gameManager.OnPlayerCaught();
        }
    }

    // Debug visualization of raycasts in the Scene view
    private void OnDrawGizmosSelected()
    {
        // Wall rays
        Gizmos.color = Color.yellow;
        float angleStep = 360f / wallRayCount;
        for (int i = 0; i < wallRayCount; i++)
        {
            float angle = i * angleStep;
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * transform.forward;
            Gizmos.DrawRay(transform.position, dir * wallRayLength);
        }

        // Line of sight to player
        if (player != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, player.position);
        }
    }
}

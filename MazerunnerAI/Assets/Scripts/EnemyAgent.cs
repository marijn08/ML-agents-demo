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
    public int wallRayCount = 12;
    public float wallRayLength = 10f;

    [Header("References")]
    public Transform player;
    public GameManager gameManager;

    private Rigidbody rb;
    private float previousDistance;
    private Vector3 previousPosition;
    private Vector3 previousPlayerPosition;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
    }

    public override void OnEpisodeBegin()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        previousDistance = Vector3.Distance(transform.position, player.position);
        previousPosition = transform.position;
        previousPlayerPosition = player.position;
    }

    /// <summary>
    /// Collects observations the agent uses to make decisions.
    /// Total observations: 4 + wallRayCount = 16 (with default 12 rays)
    /// </summary>
    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 toPlayer = player.position - transform.position;
        Vector3 toPlayerFlat = new Vector3(toPlayer.x, 0f, toPlayer.z);

        // 1. Normalized distance to player
        float distance = toPlayerFlat.magnitude;
        sensor.AddObservation(distance / maxDetectionRange);

        // 2. Forward dot to player direction
        Vector3 dirToPlayer = toPlayerFlat.normalized;
        sensor.AddObservation(Vector3.Dot(transform.forward, dirToPlayer));

        // 3. Right dot to player direction
        sensor.AddObservation(Vector3.Dot(transform.right, dirToPlayer));

        // 4. Line of sight to player
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
                sensor.AddObservation(wallHit.distance / wallRayLength);
            }
            else
            {
                sensor.AddObservation(1f);
            }
        }
    }

    /// <summary>
    /// Receives actions from the neural network.
    /// Continuous actions: [0] = forward/backward, [1] = rotation
    /// </summary>
    public override void OnActionReceived(ActionBuffers actions)
    {
        float moveInput = actions.ContinuousActions[0];
        float rotateInput = actions.ContinuousActions[1];

        // Move forward/backward
        Vector3 move = transform.forward * moveInput * moveSpeed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + move);

        // Rotate left/right
        float rotation = rotateInput * rotateSpeed * Time.fixedDeltaTime;
        Quaternion turnRotation = Quaternion.Euler(0f, rotation, 0f);
        rb.MoveRotation(rb.rotation * turnRotation);

        // ── Rewards ──
        // Budget: catch=+1.0 must dominate. Shaping ~30% of terminal max.
        // (~500 decision steps per 30s episode)

        // Time penalty — standing still for 30s costs -0.25 total
        AddReward(-0.0005f);

        // Reward the agent's own movement toward the player.
        // Isolate agent contribution by subtracting what distance change
        // the player's movement alone would cause.
        float currentDistance = Vector3.Distance(transform.position, player.position);
        float totalDelta = previousDistance - currentDistance;

        float distIfAgentStill = Vector3.Distance(previousPosition, player.position);
        float playerOnlyDelta = previousDistance - distIfAgentStill;

        float agentDelta = totalDelta - playerOnlyDelta;
        AddReward(agentDelta * 0.08f);
        previousDistance = currentDistance;
        previousPlayerPosition = player.position;

        // Reward for facing toward the player — creates a turning gradient
        Vector3 dirToPlayer = (player.position - transform.position).normalized;
        float facingDot = Vector3.Dot(transform.forward, dirToPlayer);
        // facingDot: +1 = facing player, -1 = facing away. Only reward positive facing.
        if (facingDot > 0f)
        {
            AddReward(facingDot * 0.0002f); // ~0.05 per episode if always facing
        }

        // Penalty for not moving — makes waiting expensive
        float distanceMoved = Vector3.Distance(transform.position, previousPosition);
        if (distanceMoved < 0.01f)
        {
            AddReward(-0.001f); // -0.5 per episode if always stuck = as bad as timeout
        }
        previousPosition = transform.position;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuous = actionsOut.ContinuousActions;
        continuous[0] = 0f;
        continuous[1] = 0f;

        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.wKey.isPressed) continuous[0] = 1f;
        if (keyboard.sKey.isPressed) continuous[0] = -1f;
        if (keyboard.aKey.isPressed) continuous[1] = -1f;
        if (keyboard.dKey.isPressed) continuous[1] = 1f;
    }

    public void OnCaughtPlayer()
    {
        AddReward(1.0f);
        EndEpisode();
    }

    public void OnTimeUp()
    {
        AddReward(-0.5f);
        EndEpisode();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.transform == player)
        {
            gameManager.OnPlayerCaught();
        }
    }

    private void OnCollisionStay(Collision collision)
    {
        // Light penalty for pressing against walls (-0.1 per episode if constant)
        if (collision.transform != player)
        {
            AddReward(-0.00007f);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        float angleStep = 360f / wallRayCount;
        for (int i = 0; i < wallRayCount; i++)
        {
            float angle = i * angleStep;
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * transform.forward;
            Gizmos.DrawRay(transform.position, dir * wallRayLength);
        }

        if (player != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, player.position);
        }
    }
}

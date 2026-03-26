using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

/// <summary>
/// ML-Agent that patrols a maze and chases the player on sight.
/// Uses discrete actions for smooth, deliberate movement:
///   0 = move forward, 1 = turn left 90°, 2 = turn right 90°, 3 = turn around 180°
/// The agent always steps forward after turning, so it never stands still.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class EnemyAgent : Agent
{
    [Header("Movement")]
    public float moveSpeed = 4f;
    public float turnSpeed = 300f;

    [Header("Detection")]
    public float maxDetectionRange = 25f;
    public int wallRayCount = 8;
    public float wallRayLength = 8f;

    [Header("References")]
    public Transform player;
    public ArenaManager arenaManager;

    private Rigidbody rb;
    private float previousDistance;
    private Quaternion targetRotation;
    private bool isTurning;
    private Vector3 startPosition;
    private float maxDistFromStart;
    private System.Collections.Generic.HashSet<Vector2Int> visitedCells;
    private System.Collections.Generic.Dictionary<Vector2Int, int> cellLastVisitStep; // step number when cell was last visited
    private int stepsSinceLastNewCell;
    private int lastAction;
    private int decisionStep; // counts decisions within this episode

    private int stuckCounter;

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
        targetRotation = transform.rotation;
        isTurning = false;
        startPosition = transform.position;
        maxDistFromStart = 0f;
        visitedCells = new System.Collections.Generic.HashSet<Vector2Int>();
        visitedCells.Add(WorldToCell(transform.position));
        cellLastVisitStep = new System.Collections.Generic.Dictionary<Vector2Int, int>();
        cellLastVisitStep[WorldToCell(transform.position)] = 0;
        stepsSinceLastNewCell = 0;
        lastAction = 0;
        decisionStep = 0;
        stuckCounter = 0;
    }

    /// <summary>
    /// Observations: 4 base + wallRayCount + 4 neighbor tiles = 16 total (with 8 rays).
    /// </summary>
    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 toPlayer = player.position - transform.position;
        Vector3 toPlayerFlat = new Vector3(toPlayer.x, 0f, toPlayer.z);
        float distance = toPlayerFlat.magnitude;
        Vector3 dirToPlayer = toPlayerFlat.normalized;

        // 4. Line of sight (compute first, needed for other obs)
        bool hasLOS = false;
        if (distance <= maxDetectionRange)
        {
            if (Physics.Raycast(transform.position, dirToPlayer, out RaycastHit hit, maxDetectionRange))
            {
                hasLOS = hit.transform == player;
            }
        }

        // 1. Normalized distance (only when LOS, otherwise 0 = no info)
        sensor.AddObservation(hasLOS ? distance / maxDetectionRange : 0f);

        // 2-3. Direction to player in local frame (only when LOS)
        sensor.AddObservation(hasLOS ? Vector3.Dot(transform.forward, dirToPlayer) : 0f);
        sensor.AddObservation(hasLOS ? Vector3.Dot(transform.right, dirToPlayer) : 0f);

        // 4. Line of sight flag
        sensor.AddObservation(hasLOS ? 1f : 0f);

        // 5-8. Wall raycasts (relative to agent facing)
        float angleStep = 360f / wallRayCount;
        for (int i = 0; i < wallRayCount; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * angleStep, 0f) * transform.forward;
            if (Physics.Raycast(transform.position, dir, out RaycastHit wallHit, wallRayLength))
                sensor.AddObservation(wallHit.distance / wallRayLength);
            else
                sensor.AddObservation(1f);
        }

        // 13-16. Neighbor tile staleness (relative to agent facing)
        // -1 = wall (can't navigate)
        //  1 = never visited (best choice)
        //  0.01..0.99 = visited, higher = longer ago (staler = better to revisit)
        Vector3[] neighborDirs = {
            transform.forward,    // ahead  (matches action 0)
            -transform.right,     // left   (matches action 1)
            transform.right,      // right  (matches action 2)
            -transform.forward    // behind (matches action 3)
        };

        float neighborCheckDist = 1.2f;
        for (int i = 0; i < 4; i++)
        {
            if (Physics.Raycast(transform.position, neighborDirs[i], neighborCheckDist))
            {
                sensor.AddObservation(-1f); // wall blocks this direction
            }
            else
            {
                Vector2Int neighborCell = WorldToCell(transform.position + neighborDirs[i] * 2f);
                if (!cellLastVisitStep.ContainsKey(neighborCell))
                {
                    sensor.AddObservation(1f); // never visited = best
                }
                else
                {
                    // How many steps ago was this cell last visited?
                    // More steps ago = higher value = slightly better to revisit
                    // Capped at 0.5 so visited cells always feel different from unvisited (1.0)
                    int stepsAgo = decisionStep - cellLastVisitStep[neighborCell];
                    float staleness = Mathf.Clamp(stepsAgo / 10000f, 0.01f, 0.5f);
                    sensor.AddObservation(staleness);
                }
            }
        }
    }

    /// <summary>
    /// Mask actions that would face a wall. The agent can't choose directions
    /// it can't walk — this prevents it from getting stuck.
    /// </summary>
    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        float checkDist = 1.2f;

        bool forwardBlocked = Physics.Raycast(transform.position, transform.forward, checkDist);
        bool leftBlocked = Physics.Raycast(transform.position, -transform.right, checkDist);
        bool rightBlocked = Physics.Raycast(transform.position, transform.right, checkDist);
        bool behindBlocked = Physics.Raycast(transform.position, -transform.forward, checkDist);

        // Count how many directions are open — never mask all of them
        int openCount = 0;
        if (!forwardBlocked) openCount++;
        if (!leftBlocked) openCount++;
        if (!rightBlocked) openCount++;
        if (!behindBlocked) openCount++;

        if (openCount == 0) return; // all blocked (corner/stuck) — let agent pick freely

        if (forwardBlocked) actionMask.SetActionEnabled(0, 0, false);
        if (leftBlocked) actionMask.SetActionEnabled(0, 1, false);
        if (rightBlocked) actionMask.SetActionEnabled(0, 2, false);
        if (behindBlocked) actionMask.SetActionEnabled(0, 3, false);
    }

    /// <summary>
    /// Discrete actions: 0=forward, 1=turn left, 2=turn right, 3=turn around.
    /// The agent always moves forward — the choice is which direction to face.
    /// </summary>
    public override void OnActionReceived(ActionBuffers actions)
    {
        int action = actions.DiscreteActions[0];

        // Apply turn
        switch (action)
        {
            case 0: // Keep going straight
                break;
            case 1: // Turn left 90°
                targetRotation = Quaternion.Euler(0f, -90f, 0f) * transform.rotation;
                isTurning = true;
                break;
            case 2: // Turn right 90°
                targetRotation = Quaternion.Euler(0f, 90f, 0f) * transform.rotation;
                isTurning = true;
                break;
            case 3: // Turn around 180°
                targetRotation = Quaternion.Euler(0f, 180f, 0f) * transform.rotation;
                isTurning = true;
                break;
        }

        // Smooth rotation toward target
        if (isTurning)
        {
            rb.MoveRotation(Quaternion.RotateTowards(
                rb.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime));

            if (Quaternion.Angle(rb.rotation, targetRotation) < 2f)
            {
                rb.MoveRotation(targetRotation);
                isTurning = false;
            }
        }

        // Always move forward (smooth walking, no twitching)
        if (!Physics.Raycast(transform.position, transform.forward, 0.6f))
        {
            rb.MovePosition(rb.position + transform.forward * moveSpeed * Time.fixedDeltaTime);
            stuckCounter = 0;
        }
        else
        {
            stuckCounter++;
            // If stuck, snap to the center of current cell and face an open direction
            if (stuckCounter > 5)
            {
                Vector2Int cell = WorldToCell(transform.position);
                Vector3 cellCenter = arenaManager.mazeGenerator.CellToWorld(cell.x, cell.y);

                rb.MovePosition(cellCenter);
                rb.linearVelocity = Vector3.zero;

                // Face the first open cardinal direction
                Vector3[] cardinals = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
                foreach (var d in cardinals)
                {
                    if (!Physics.Raycast(cellCenter, d, 1.2f))
                    {
                        targetRotation = Quaternion.LookRotation(d, Vector3.up);
                        rb.MoveRotation(targetRotation);
                        isTurning = false;
                        break;
                    }
                }
                stuckCounter = 0;
            }
        }

        // ── Rewards ──
        // Terminal rewards drive learning; shaping is minimal.
        //   Catch:   +1.0   |   Timeout: -0.3

        float currentDistance = Vector3.Distance(transform.position, player.position);

        // Line-of-sight check for chase rewards
        Vector3 toP = (player.position - transform.position);
        toP.y = 0f;
        bool canSeePlayer = false;
        if (toP.magnitude <= maxDetectionRange)
        {
            if (Physics.Raycast(transform.position, toP.normalized, out RaycastHit losHit, maxDetectionRange))
                canSeePlayer = losHit.transform == player;
        }

        // Only reward distance-closing when agent has LOS
        float distanceDelta = previousDistance - currentDistance;
        if (canSeePlayer)
        {
            AddReward(distanceDelta * 0.05f);
            AddReward(0.002f);  // LOS maintenance bonus
        }
        previousDistance = currentDistance;

        // ── Exploration (only on decision steps, not repeated actions) ──
        Vector2Int currentCell = WorldToCell(transform.position);
        decisionStep++;

        // Small one-time bonus for discovering new cells
        if (visitedCells.Add(currentCell))
        {
            AddReward(0.003f);
            stepsSinceLastNewCell = 0;
        }
        else
        {
            stepsSinceLastNewCell++;
        }

        // Update last-visit timestamp
        cellLastVisitStep[currentCell] = decisionStep;

        // ── Floor tile coloring ──
        // Just visited = dark red, older = lighter red, never back to white
        MazeGenerator maze = arenaManager.mazeGenerator;
        if (maze != null)
        {
            foreach (var kvp in cellLastVisitStep)
            {
                int stepsAgo = decisionStep - kvp.Value;
                float t = Mathf.Clamp01(stepsAgo / 10000f); // 0 = just visited, 1 = 10000+ steps ago
                // Dark red → light pink (never white)
                Color c = Color.Lerp(new Color(0.6f, 0f, 0f), new Color(1f, 0.7f, 0.7f), t);
                maze.SetFloorColor(kvp.Key, c);
            }
        }

        lastAction = action;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discrete = actionsOut.DiscreteActions;
        discrete[0] = 0; // Default: forward

        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.aKey.isPressed) discrete[0] = 1;      // Left
        else if (keyboard.dKey.isPressed) discrete[0] = 2;  // Right
        else if (keyboard.sKey.isPressed) discrete[0] = 3;  // Turn around
    }

    public void OnCaughtPlayer()
    {
        AddReward(1.0f);
        EndEpisode();
    }

    public void OnTimeUp()
    {
        AddReward(-0.3f);
        EndEpisode();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.transform == player)
        {
            arenaManager.OnPlayerCaught();
        }
    }

    /// <summary>
    /// Converts a world position to a grid cell coordinate for exploration tracking.
    /// Uses the arena's MazeGenerator to get correct local coordinates.
    /// </summary>
    private Vector2Int WorldToCell(Vector3 pos)
    {
        if (arenaManager != null && arenaManager.mazeGenerator != null)
            return arenaManager.mazeGenerator.WorldToCellCoord(pos);
        return new Vector2Int(
            Mathf.FloorToInt(pos.x / 2f),
            Mathf.FloorToInt(pos.z / 2f)
        );
    }

    /// <summary>
    /// Counts how many of the 4 cardinal directions are open (no wall within 1.2 units).
    /// Returns 1 for a dead end, 2 for a corridor, 3-4 for junctions.
    /// </summary>
    private int CountOpenDirections()
    {
        int count = 0;
        float checkDist = 1.2f;
        if (!Physics.Raycast(transform.position, Vector3.forward, checkDist)) count++;
        if (!Physics.Raycast(transform.position, Vector3.back, checkDist)) count++;
        if (!Physics.Raycast(transform.position, Vector3.left, checkDist)) count++;
        if (!Physics.Raycast(transform.position, Vector3.right, checkDist)) count++;
        return count;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        float angleStep = 360f / wallRayCount;
        for (int i = 0; i < wallRayCount; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * angleStep, 0f) * transform.forward;
            Gizmos.DrawRay(transform.position, dir * wallRayLength);
        }

        if (player != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, player.position);
        }
    }
}

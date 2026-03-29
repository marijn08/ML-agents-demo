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
    public int wallRayCount = 4;
    public float wallRayLength = 8f;

    [Header("References")]
    public Transform player;
    public ArenaManager arenaManager;

    private Rigidbody rb;
    private float previousDistance;
    private Quaternion targetRotation;
    private bool isTurning;
    private System.Collections.Generic.HashSet<Vector2Int> visitedCells;
    private System.Collections.Generic.Dictionary<Vector2Int, int> cellLastVisitStep; // step number when cell was last visited
    private int stepsSinceLastNewCell;
    private int lastAction;
    private int decisionStep; // counts decisions within this episode
    private Vector2Int previousCell;
    private Vector2Int cellBeforePrevious;

    private int stuckCounter;

    // Cached BFS structures to avoid GC allocations every frame
    private System.Collections.Generic.Queue<Vector2Int> bfsQueue;
    private System.Collections.Generic.HashSet<Vector2Int> bfsVisited;
    private static readonly Vector2Int[] bfsOffsets = {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(0, 1), new Vector2Int(0, -1)
    };

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        bfsQueue = new System.Collections.Generic.Queue<Vector2Int>(256);
        bfsVisited = new System.Collections.Generic.HashSet<Vector2Int>();
    }

    public override void OnEpisodeBegin()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        previousDistance = Vector3.Distance(transform.position, player.position);
        targetRotation = transform.rotation;
        isTurning = false;
        visitedCells = new System.Collections.Generic.HashSet<Vector2Int>();
        visitedCells.Add(WorldToCell(transform.position));
        cellLastVisitStep = new System.Collections.Generic.Dictionary<Vector2Int, int>();
        cellLastVisitStep[WorldToCell(transform.position)] = 0;
        stepsSinceLastNewCell = 0;
        lastAction = 0;
        decisionStep = 0;
        stuckCounter = 0;
        previousCell = WorldToCell(transform.position);
        cellBeforePrevious = previousCell;
    }

    /// <summary>
    /// Observations (13 total):
    ///   1-4:  Player info (LOS-gated): distance, forward dot, right dot, LOS flag
    ///   5-8:  Wall raycasts (4 cardinal dirs relative to facing)
    ///   9-10: Explore compass (forward + right dot toward nearest unvisited cell)
    ///   11:   Exploration progress (fraction of maze visited)
    ///   12:   Normalized time remaining (0 if infinite mode)
    ///   13:   Junction type (open directions / 4)
    /// </summary>
    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 toPlayer = player.position - transform.position;
        Vector3 toPlayerFlat = new Vector3(toPlayer.x, 0f, toPlayer.z);
        float distance = toPlayerFlat.magnitude;
        Vector3 dirToPlayer = toPlayerFlat.normalized;

        // LOS check — multi-ray so we don't miss the player clipping past wall edges
        bool hasLOS = CheckLineOfSight(distance);

        // 1. Normalized distance (LOS-gated)
        sensor.AddObservation(hasLOS ? distance / maxDetectionRange : 0f);

        // 2-3. Direction to player in local frame (LOS-gated)
        sensor.AddObservation(hasLOS ? Vector3.Dot(transform.forward, dirToPlayer) : 0f);
        sensor.AddObservation(hasLOS ? Vector3.Dot(transform.right, dirToPlayer) : 0f);

        // 4. LOS flag
        sensor.AddObservation(hasLOS ? 1f : 0f);

        // 5-8. Wall raycasts — 4 cardinal directions relative to agent facing
        Vector3[] rayDirs = { transform.forward, -transform.right, transform.right, -transform.forward };
        for (int i = 0; i < 4; i++)
        {
            if (Physics.Raycast(transform.position, rayDirs[i], out RaycastHit wallHit, wallRayLength))
                sensor.AddObservation(wallHit.distance / wallRayLength);
            else
                sensor.AddObservation(1f);
        }

        // 9-10. Compass toward nearest unvisited cell (2D local frame)
        Vector2Int myCell = WorldToCell(transform.position);
        Vector3 toNearest = FindNearestUnvisitedDirection(myCell);
        sensor.AddObservation(Vector3.Dot(transform.forward, toNearest));
        sensor.AddObservation(Vector3.Dot(transform.right, toNearest));

        // 10. Exploration progress: fraction of maze cells visited
        MazeGenerator maze = arenaManager.mazeGenerator;
        int totalCells = maze.width * maze.height;
        sensor.AddObservation((float)visitedCells.Count / totalCells);

        // 11. Normalized time remaining (0 when timer is off)
        sensor.AddObservation(arenaManager.useTimer ? arenaManager.NormalizedTimeRemaining : 0f);

        // 12. Junction type: how many open directions at current cell (0.25 = dead end, 1.0 = 4-way)
        Vector2Int cell = WorldToCell(transform.position);
        Vector3 cellCenter = maze.CellToWorld(cell.x, cell.y);
        int openDirs = 0;
        if (!Physics.Raycast(cellCenter, Vector3.forward, 1.2f)) openDirs++;
        if (!Physics.Raycast(cellCenter, Vector3.back, 1.2f)) openDirs++;
        if (!Physics.Raycast(cellCenter, Vector3.right, 1.2f)) openDirs++;
        if (!Physics.Raycast(cellCenter, Vector3.left, 1.2f)) openDirs++;
        sensor.AddObservation(openDirs / 4f);
    }

    /// <summary>
    /// Mask actions that would face a wall. The agent can't choose directions
    /// it can't walk — this prevents it from getting stuck.
    /// Raycasts from cell center to avoid edge-clipping false positives.
    /// </summary>
    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        // Use cell center for reliable raycasts (avoids wall-edge clipping when slightly off-center)
        Vector2Int cell = WorldToCell(transform.position);
        Vector3 origin = arenaManager.mazeGenerator.CellToWorld(cell.x, cell.y);
        float checkDist = 1.2f;

        // Check world-cardinal directions, then map to agent-relative actions
        bool northOpen = !Physics.Raycast(origin, Vector3.forward, checkDist);
        bool southOpen = !Physics.Raycast(origin, Vector3.back, checkDist);
        bool eastOpen  = !Physics.Raycast(origin, Vector3.right, checkDist);
        bool westOpen  = !Physics.Raycast(origin, Vector3.left, checkDist);

        // Map world directions to agent-relative actions based on current facing
        // Action 0=forward, 1=turn left, 2=turn right, 3=turn around
        bool[] actionOpen = new bool[4];
        actionOpen[0] = IsDirectionOpen(transform.forward, northOpen, southOpen, eastOpen, westOpen);
        actionOpen[1] = IsDirectionOpen(-transform.right, northOpen, southOpen, eastOpen, westOpen);
        actionOpen[2] = IsDirectionOpen(transform.right, northOpen, southOpen, eastOpen, westOpen);
        actionOpen[3] = IsDirectionOpen(-transform.forward, northOpen, southOpen, eastOpen, westOpen);

        int openCount = 0;
        for (int i = 0; i < 4; i++) if (actionOpen[i]) openCount++;

        if (openCount == 0) return; // safety: never mask everything

        // Block turning around when there are other open directions
        // This prevents corridor oscillation — agent must commit to a direction
        int forwardOpenCount = 0;
        for (int i = 0; i < 3; i++) if (actionOpen[i]) forwardOpenCount++;
        if (forwardOpenCount > 0 && actionOpen[3])
        {
            actionOpen[3] = false; // mask turn-around
        }

        for (int i = 0; i < 4; i++)
            if (!actionOpen[i]) actionMask.SetActionEnabled(0, i, false);
    }

    /// <summary>
    /// Returns true if the given agent-relative direction roughly aligns with an open world direction.
    /// </summary>
    private bool IsDirectionOpen(Vector3 agentDir, bool northOpen, bool southOpen, bool eastOpen, bool westOpen)
    {
        // Find which world cardinal the agent direction is closest to
        float dotN = Vector3.Dot(agentDir, Vector3.forward);
        float dotS = Vector3.Dot(agentDir, Vector3.back);
        float dotE = Vector3.Dot(agentDir, Vector3.right);
        float dotW = Vector3.Dot(agentDir, Vector3.left);

        float max = Mathf.Max(dotN, dotS, dotE, dotW);
        if (max == dotN) return northOpen;
        if (max == dotS) return southOpen;
        if (max == dotE) return eastOpen;
        return westOpen;
    }

    /// <summary>
    /// Discrete actions: 0=forward, 1=turn left, 2=turn right, 3=turn around.
    /// The agent always moves forward — the choice is which direction to face.
    /// </summary>
    public override void OnActionReceived(ActionBuffers actions)
    {
        int action = actions.DiscreteActions[0];

        // Auto-turn in dead ends: if only one direction is open, face it immediately
        Vector2Int curCell = WorldToCell(transform.position);
        Vector3 curCellCenter = arenaManager.mazeGenerator.CellToWorld(curCell.x, curCell.y);
        int openCount = 0;
        Vector3 onlyOpenDir = Vector3.forward;
        if (!Physics.Raycast(curCellCenter, Vector3.forward, 1.2f)) { openCount++; onlyOpenDir = Vector3.forward; }
        if (!Physics.Raycast(curCellCenter, Vector3.back, 1.2f)) { openCount++; onlyOpenDir = Vector3.back; }
        if (!Physics.Raycast(curCellCenter, Vector3.right, 1.2f)) { openCount++; onlyOpenDir = Vector3.right; }
        if (!Physics.Raycast(curCellCenter, Vector3.left, 1.2f)) { openCount++; onlyOpenDir = Vector3.left; }
        if (openCount == 1)
        {
            // Dead end — force turn to the only exit
            targetRotation = Quaternion.LookRotation(onlyOpenDir, Vector3.up);
            if (Quaternion.Angle(rb.rotation, targetRotation) > 2f)
                isTurning = true;
        }
        else
        {
            // Normal action processing
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
            // If stuck for 2+ frames, snap to cell center and face an open direction
            if (stuckCounter > 2)
            {
                Vector2Int cell = WorldToCell(transform.position);
                Vector3 cellCenter = arenaManager.mazeGenerator.CellToWorld(cell.x, cell.y);

                rb.MovePosition(cellCenter);
                rb.linearVelocity = Vector3.zero;

                // Face the first open cardinal direction (prefer directions with unvisited neighbors)
                Vector3[] cardinals = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
                Vector3 bestDir = Vector3.forward;
                bool foundUnvisited = false;

                foreach (var d in cardinals)
                {
                    if (Physics.Raycast(cellCenter, d, 1.2f)) continue;

                    if (!foundUnvisited)
                    {
                        // Check if neighbor cell in this direction is unvisited
                        Vector2Int neighborCell = WorldToCell(cellCenter + d * 2f);
                        if (!cellLastVisitStep.ContainsKey(neighborCell))
                        {
                            bestDir = d;
                            foundUnvisited = true;
                            continue;
                        }
                        bestDir = d; // fallback to first open direction
                    }
                }

                targetRotation = Quaternion.LookRotation(bestDir, Vector3.up);
                rb.MoveRotation(targetRotation);
                isTurning = false;
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
        bool canSeePlayer = CheckLineOfSight(toP.magnitude);

        // Only reward distance-closing when agent has LOS
        float distanceDelta = previousDistance - currentDistance;
        if (canSeePlayer)
        {
            AddReward(distanceDelta * 0.2f);  // strong chase signal
            AddReward(0.01f);  // LOS maintenance bonus
        }
        previousDistance = currentDistance;

        // ── Exploration (only on decision steps, not repeated actions) ──
        Vector2Int currentCell = WorldToCell(transform.position);
        decisionStep++;

        // Penalize oscillation: stepping back to the cell before previous = bouncing
        if (currentCell == cellBeforePrevious && currentCell != previousCell)
        {
            AddReward(-0.005f);
        }

        // Exploration reward: new cells get full bonus, revisited cells get
        // a penalty (recent) or small reward (stale), encouraging exploration
        if (visitedCells.Add(currentCell))
        {
            AddReward(0.002f); // brand new cell
            stepsSinceLastNewCell = 0;
        }
        else
        {
            int stepsAgo = decisionStep - cellLastVisitStep[currentCell];
            // Recently visited = small penalty, long ago = neutral
            float revisitReward = Mathf.Lerp(-0.002f, 0.0f, Mathf.Clamp01(stepsAgo / 5000f));
            AddReward(revisitReward);
            stepsSinceLastNewCell++;
        }

        // Update last-visit timestamp and cell history
        cellLastVisitStep[currentCell] = decisionStep;
        if (currentCell != previousCell)
        {
            cellBeforePrevious = previousCell;
            previousCell = currentCell;
        }

        // ── Floor tile coloring (throttled to every 50 steps) ──
        if (decisionStep % 50 == 0)
        {
            MazeGenerator maze = arenaManager.mazeGenerator;
            if (maze != null)
            {
                foreach (var kvp in cellLastVisitStep)
                {
                    int stepsAgo = decisionStep - kvp.Value;
                    float t = Mathf.Clamp01(stepsAgo / 10000f);
                    Color c = Color.Lerp(new Color(0.6f, 0f, 0f), new Color(1f, 0.7f, 0.7f), t);
                    maze.SetFloorColor(kvp.Key, c);
                }
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
        AddReward(5.0f);
        EndEpisode();
    }

    public void OnTimeUp()
    {
        AddReward(-0.1f);
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
    /// Multi-ray line-of-sight check. Casts rays to the player's center and 4 offsets
    /// (left, right, front, back of the player collider) so we don't miss visibility
    /// when the ray barely clips a wall corner.
    /// </summary>
    private bool CheckLineOfSight(float distance)
    {
        if (distance > maxDetectionRange) return false;

        Vector3 origin = transform.position;
        Vector3 playerPos = player.position;
        float offset = 0.3f;

        // Check 5 target points without allocating an array
        if (RayHitsPlayer(origin, playerPos)) return true;
        if (RayHitsPlayer(origin, playerPos + Vector3.right * offset)) return true;
        if (RayHitsPlayer(origin, playerPos - Vector3.right * offset)) return true;
        if (RayHitsPlayer(origin, playerPos + Vector3.forward * offset)) return true;
        if (RayHitsPlayer(origin, playerPos - Vector3.forward * offset)) return true;
        return false;
    }

    private bool RayHitsPlayer(Vector3 origin, Vector3 target)
    {
        Vector3 dir = target - origin;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return false;
        if (Physics.Raycast(origin, dir.normalized, out RaycastHit hit, maxDetectionRange))
            return hit.transform == player;
        return false;
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

    /// <summary>
    /// BFS through the maze grid to find the nearest unvisited cell.
    /// Returns a normalized world-space direction from agent toward that cell,
    /// or Vector3.zero if all cells have been visited.
    /// </summary>
    private Vector3 FindNearestUnvisitedDirection(Vector2Int startCell)
    {
        MazeGenerator maze = arenaManager.mazeGenerator;
        if (maze == null) return Vector3.zero;

        bfsQueue.Clear();
        bfsVisited.Clear();
        bfsQueue.Enqueue(startCell);
        bfsVisited.Add(startCell);

        while (bfsQueue.Count > 0)
        {
            Vector2Int cell = bfsQueue.Dequeue();

            if (!cellLastVisitStep.ContainsKey(cell))
            {
                Vector3 targetWorld = maze.CellToWorld(cell.x, cell.y);
                Vector3 dir = targetWorld - transform.position;
                dir.y = 0f;
                return dir.magnitude > 0.01f ? dir.normalized : Vector3.zero;
            }

            for (int i = 0; i < 4; i++)
            {
                Vector2Int neighbor = cell + bfsOffsets[i];
                if (neighbor.x < 0 || neighbor.x >= maze.width ||
                    neighbor.y < 0 || neighbor.y >= maze.height)
                    continue;
                if (bfsVisited.Contains(neighbor)) continue;
                if (maze.HasWallBetween(cell, neighbor)) continue;
                bfsVisited.Add(neighbor);
                bfsQueue.Enqueue(neighbor);
            }
        }

        return Vector3.zero;
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

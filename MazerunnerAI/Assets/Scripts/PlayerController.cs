using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Player controller that supports both manual (arrow keys) and
/// autonomous (flee from enemy) modes. During training the player
/// actively runs away from the enemy so the agent must learn to chase effectively.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 4f;
    public float rotateSpeed = 180f;

    [Header("Autonomous Mode")]
    [Tooltip("When true the player flees from enemies (for training).")]
    public bool autonomous = true;
    [Tooltip("Enemy transforms to flee from. Flees the nearest one.")]
    public Transform[] enemies;
    public float wallDetectRange = 2.5f;
    public int escapeRayCount = 8;
    public float enemySightRange = 12f;

    private Rigidbody rb;

    // Random wander state
    private Vector3 wanderDir;
    private float wanderTimer;
    private float wanderInterval = 1.5f; // seconds between direction changes

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        PickRandomWanderDir();
    }

    private void FixedUpdate()
    {
        if (autonomous && enemies != null && enemies.Length > 0)
        {
            MoveAutonomous();
        }
        else
        {
            MoveManual();
        }
    }

    /// <summary>
    /// Arrow-key movement for playing the game manually.
    /// </summary>
    private void MoveManual()
    {
        float moveInput = 0f;
        float rotateInput = 0f;

        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.upArrowKey.isPressed) moveInput = 1f;
        if (keyboard.downArrowKey.isPressed) moveInput = -1f;
        if (keyboard.leftArrowKey.isPressed) rotateInput = -1f;
        if (keyboard.rightArrowKey.isPressed) rotateInput = 1f;

        Vector3 move = transform.forward * moveInput * moveSpeed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + move);

        float rotation = rotateInput * rotateSpeed * Time.fixedDeltaTime;
        Quaternion turnRotation = Quaternion.Euler(0f, rotation, 0f);
        rb.MoveRotation(rb.rotation * turnRotation);
    }

    /// <summary>
    /// Autonomous behavior: wanders randomly through the maze, but flees
    /// when it has line of sight to an enemy.
    /// </summary>
    private void MoveAutonomous()
    {
        // Check if any enemy is visible (line of sight)
        Transform visibleEnemy = null;
        float closestVisibleDist = Mathf.Infinity;

        for (int i = 0; i < enemies.Length; i++)
        {
            Vector3 toEnemy = enemies[i].position - transform.position;
            float dist = toEnemy.magnitude;
            if (dist > enemySightRange) continue;

            if (Physics.Raycast(transform.position, toEnemy.normalized, out RaycastHit hit, dist + 0.5f))
            {
                if (hit.transform == enemies[i] && dist < closestVisibleDist)
                {
                    closestVisibleDist = dist;
                    visibleEnemy = enemies[i];
                }
            }
        }

        if (visibleEnemy != null)
        {
            FleeFromEnemy(visibleEnemy);
        }
        else
        {
            Wander();
        }
    }

    /// <summary>
    /// Random wander: picks a direction periodically and walks that way,
    /// bouncing off walls by choosing a new direction on impact.
    /// </summary>
    private void Wander()
    {
        wanderTimer -= Time.fixedDeltaTime;

        // Check if current direction is blocked
        bool blocked = Physics.Raycast(transform.position, wanderDir, 0.8f);
        if (blocked || wanderTimer <= 0f)
        {
            PickRandomWanderDir();
        }

        // Smoothly rotate toward wander direction
        Quaternion targetRot = Quaternion.LookRotation(wanderDir);
        rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRot, rotateSpeed * Time.fixedDeltaTime));

        // Move forward if path is clear
        if (!Physics.Raycast(transform.position, transform.forward, 0.6f))
        {
            rb.MovePosition(rb.position + transform.forward * moveSpeed * Time.fixedDeltaTime);
        }
    }

    private void PickRandomWanderDir()
    {
        // Pick a random horizontal direction that isn't immediately blocked
        for (int attempt = 0; attempt < 10; attempt++)
        {
            float angle = Random.Range(0f, 360f);
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            if (!Physics.Raycast(transform.position, dir, 0.8f))
            {
                wanderDir = dir;
                wanderTimer = Random.Range(wanderInterval * 0.5f, wanderInterval * 1.5f);
                return;
            }
        }
        // Fallback: just pick any direction
        wanderDir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
        wanderTimer = wanderInterval;
    }

    /// <summary>
    /// Flee behavior: casts rays to find the best open direction away from the enemy.
    /// </summary>
    private void FleeFromEnemy(Transform enemy)
    {
        Vector3 awayFromEnemy = (transform.position - enemy.position).normalized;
        Vector3 bestDir = awayFromEnemy;
        float bestScore = -Mathf.Infinity;

        float angleStep = 360f / escapeRayCount;
        for (int i = 0; i < escapeRayCount; i++)
        {
            float angle = i * angleStep;
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;

            float openDist = wallDetectRange;
            if (Physics.Raycast(transform.position, dir, out RaycastHit hit, wallDetectRange))
            {
                openDist = hit.distance;
            }

            if (openDist < 0.5f) continue;

            float awayDot = Vector3.Dot(dir, awayFromEnemy);
            float score = awayDot + (openDist / wallDetectRange) * 0.5f;

            if (score > bestScore)
            {
                bestScore = score;
                bestDir = dir;
            }
        }

        Quaternion targetRot = Quaternion.LookRotation(bestDir);
        rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRot, rotateSpeed * Time.fixedDeltaTime));

        if (!Physics.Raycast(transform.position, transform.forward, 0.6f))
        {
            rb.MovePosition(rb.position + transform.forward * moveSpeed * Time.fixedDeltaTime);
        }
    }
}

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
    [Tooltip("When true the player flees from the enemy (for training).")]
    public bool autonomous = true;
    [Tooltip("The enemy transform to flee from.")]
    public Transform enemy;
    public float wallDetectRange = 2.5f;
    public int escapeRayCount = 8;

    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
    }

    private void FixedUpdate()
    {
        if (autonomous && enemy != null)
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
    /// Flee behavior: picks the best open direction away from the enemy
    /// by casting rays and scoring each direction based on distance from
    /// the enemy and available open space.
    /// </summary>
    private void MoveAutonomous()
    {
        Vector3 awayFromEnemy = (transform.position - enemy.position).normalized;
        Vector3 bestDir = awayFromEnemy;
        float bestScore = -Mathf.Infinity;

        float angleStep = 360f / escapeRayCount;
        for (int i = 0; i < escapeRayCount; i++)
        {
            float angle = i * angleStep;
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;

            // Check how much open space is in this direction
            float openDist = wallDetectRange;
            if (Physics.Raycast(transform.position, dir, out RaycastHit hit, wallDetectRange))
            {
                openDist = hit.distance;
            }

            // Skip directions with a wall right in front
            if (openDist < 0.5f) continue;

            // Score: prefer directions that face away from the enemy AND have open space
            float awayDot = Vector3.Dot(dir, awayFromEnemy);
            float score = awayDot + (openDist / wallDetectRange) * 0.5f;

            if (score > bestScore)
            {
                bestScore = score;
                bestDir = dir;
            }
        }

        // Smoothly rotate toward the best direction
        Quaternion targetRot = Quaternion.LookRotation(bestDir);
        rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRot, rotateSpeed * Time.fixedDeltaTime));

        // Move forward if the path is clear
        if (!Physics.Raycast(transform.position, transform.forward, 0.6f))
        {
            Vector3 move = transform.forward * moveSpeed * Time.fixedDeltaTime;
            rb.MovePosition(rb.position + move);
        }
    }
}

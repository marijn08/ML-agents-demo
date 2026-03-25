using UnityEngine;

/// <summary>
/// Simple player controller using arrow keys.
/// Moves the player through the maze with basic physics.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 4f;
    public float rotateSpeed = 180f;

    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
    }

    private void FixedUpdate()
    {
        float moveInput = 0f;
        float rotateInput = 0f;

        // Arrow keys for movement
        if (Input.GetKey(KeyCode.UpArrow)) moveInput = 1f;
        if (Input.GetKey(KeyCode.DownArrow)) moveInput = -1f;
        if (Input.GetKey(KeyCode.LeftArrow)) rotateInput = -1f;
        if (Input.GetKey(KeyCode.RightArrow)) rotateInput = 1f;

        // Move forward/backward
        Vector3 move = transform.forward * moveInput * moveSpeed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + move);

        // Rotate left/right
        float rotation = rotateInput * rotateSpeed * Time.fixedDeltaTime;
        Quaternion turnRotation = Quaternion.Euler(0f, rotation, 0f);
        rb.MoveRotation(rb.rotation * turnRotation);
    }
}

using UnityEngine;

public class FreeCameraController : MonoBehaviour
{
    public float moveSpeed = 10f;
    public float lookSpeed = 2f;

    // Camera constraints
    public float minDistanceToBoard = 5f;
    public float minPitch = 10f;
    public float maxPitch = 85f;
    public float minYaw = -80f;
    public float maxYaw = 80f;

    // Board dimensions (from BoardManager)
    private const int BOARD_WIDTH = 30;
    private const int BOARD_HEIGHT = 30;

    private float pitch = 0f; // up/down angle
    private float yaw = 0f;   // left/right angle
    private float mouseWheel;

    void Start()
    {
        // Initialize angles from current camera rotation
        Vector3 currentAngles = transform.eulerAngles;
        pitch = currentAngles.x;
        yaw = currentAngles.y;
    }

    void LateUpdate()
    {
        HandleMovement();
        HandleRotation();
        HandleZoom();
    }

    void HandleZoom()
    {
        mouseWheel = Input.GetAxis("Mouse ScrollWheel");
        if (mouseWheel != 0f)
        {
            // Calculate focal point on the board (y=0)
            float distanceToLookPoint = -transform.position.y / transform.forward.y;
            Vector3 lookPoint = transform.position + transform.forward * distanceToLookPoint;

            Vector3 newPosition = transform.position + transform.forward * mouseWheel * moveSpeed;

            // Calculate new distance to focal point
            float newDistanceToLookPoint = Vector3.Distance(newPosition, lookPoint);

            // Clamp to minimum distance and prevent going through board
            if (newDistanceToLookPoint < minDistanceToBoard || newPosition.y < 0)
            {
                // Position camera exactly at minimum distance from focal point
                Vector3 direction = (transform.position - lookPoint).normalized;
                newPosition = lookPoint + direction * minDistanceToBoard;
            }

            transform.position = newPosition;
        }
    }

    void HandleMovement()
    {
        Vector3 movement = Vector3.zero;

        // Forward/back (W/S)
        Vector3 forwardXZ = transform.forward;
        forwardXZ.y = 0;
        forwardXZ.Normalize();
        if (Input.GetKey(KeyCode.W)) movement += forwardXZ;
        if (Input.GetKey(KeyCode.S)) movement -= forwardXZ;

        // Left/right (A/D)
        if (Input.GetKey(KeyCode.A)) movement -= transform.right;
        if (Input.GetKey(KeyCode.D)) movement += transform.right;

        Vector3 newPosition = transform.position + movement.normalized * moveSpeed * Time.deltaTime;

        // Calculate focal point and constrain it to board bounds
        float distanceToLookPoint = -newPosition.y / transform.forward.y;
        Vector3 lookPoint = newPosition + transform.forward * distanceToLookPoint;

        // Clamp focal point to board bounds
        lookPoint.x = Mathf.Clamp(lookPoint.x, 0, BOARD_WIDTH);
        lookPoint.z = Mathf.Clamp(lookPoint.z, 0, BOARD_HEIGHT);

        // Recalculate camera position to maintain same distance from clamped focal point
        transform.position = lookPoint - transform.forward * distanceToLookPoint;
    }

    void HandleRotation()
    {
        // Only rotate while holding right mouse button
        if (Input.GetMouseButton(1))
        {
            // Calculate the point on the ground (y=0) where camera is looking
            float distanceToLookPoint = -transform.position.y / transform.forward.y;
            Vector3 lookPoint = transform.position + transform.forward * distanceToLookPoint;

            yaw += Input.GetAxis("Mouse X") * lookSpeed;
            pitch -= Input.GetAxis("Mouse Y") * lookSpeed;

            // Clamp pitch to prevent going beneath the board
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

            // Clamp yaw to prevent facing opposite the board
            yaw = Mathf.Clamp(yaw, minYaw, maxYaw);

            // Calculate new rotation and direction
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 direction = rotation * Vector3.forward;

            // Constrain focal point to board bounds
            lookPoint.x = Mathf.Clamp(lookPoint.x, 0, BOARD_WIDTH);
            lookPoint.z = Mathf.Clamp(lookPoint.z, 0, BOARD_HEIGHT);

            // Position camera at same distance from look point, in new direction
            transform.position = lookPoint - direction * distanceToLookPoint;
            transform.rotation = rotation;
        }
    }
}
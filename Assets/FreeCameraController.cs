using UnityEngine;

public class FreeCameraController : MonoBehaviour
{
    public float moveSpeed = 10f;
    public float lookSpeed = 2f;

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
            transform.position += transform.forward * mouseWheel * moveSpeed;
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

        transform.position += movement.normalized * moveSpeed * Time.deltaTime;
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

            // Clamp pitch so you can't flip upside down
            pitch = Mathf.Clamp(pitch, -90f, 90f);

            // Calculate new rotation and direction
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 direction = rotation * Vector3.forward;

            // Position camera at same distance from look point, in new direction
            transform.position = lookPoint - direction * distanceToLookPoint;
            transform.rotation = rotation;
        }
    }
}
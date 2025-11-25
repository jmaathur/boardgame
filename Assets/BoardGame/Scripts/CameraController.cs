using UnityEngine;

public class CameraController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float panSpeed = 20f;
    public float keyboardMoveSpeed = 10f;
    public float zoomSpeed = 0.1f;

    [Header("Rotation Settings")]
    public float rotationSpeed = 0.5f;
    public float keyboardRotationSpeed = 50f;

    [Header("Bounds")]
    public float minZoom = 3f;
    public float maxZoom = 25f;
    public float minX = -5f;
    public float maxX = 35f;
    public float minZ = -5f;
    public float maxZ = 35f;
    public float minY = 2f;
    public float maxY = 30f;

    private Vector3 lastMousePosition;
    private Vector3 touchStart;

    void Update()
    {
        HandleMobileInput();
        HandleDesktopInput();
    }

    void HandleMobileInput()
    {
        if (Input.touchCount == 1)
        {
            // Single finger - pan camera
            Touch touch = Input.GetTouch(0);

            if (touch.phase == TouchPhase.Began)
            {
                touchStart = touch.position;
            }
            else if (touch.phase == TouchPhase.Moved)
            {
                Vector2 delta = touch.deltaPosition;
                PanCamera(new Vector3(delta.x, delta.y, 0));
            }
        }
        else if (Input.touchCount == 2)
        {
            // Two fingers - pinch zoom and rotate
            Touch touch0 = Input.GetTouch(0);
            Touch touch1 = Input.GetTouch(1);

            // Pinch to zoom
            Vector2 touch0PrevPos = touch0.position - touch0.deltaPosition;
            Vector2 touch1PrevPos = touch1.position - touch1.deltaPosition;

            float prevMagnitude = (touch0PrevPos - touch1PrevPos).magnitude;
            float currentMagnitude = (touch0.position - touch1.position).magnitude;
            float difference = currentMagnitude - prevMagnitude;

            Zoom(difference * zoomSpeed);

            // Two-finger rotation (swipe in same direction)
            Vector2 touch0Delta = touch0.deltaPosition;
            Vector2 touch1Delta = touch1.deltaPosition;

            // Check if both fingers moving horizontally in same direction
            if (Mathf.Abs(touch0Delta.x) > Mathf.Abs(touch0Delta.y) &&
                Mathf.Abs(touch1Delta.x) > Mathf.Abs(touch1Delta.y))
            {
                if (Mathf.Sign(touch0Delta.x) == Mathf.Sign(touch1Delta.x))
                {
                    float avgDelta = (touch0Delta.x + touch1Delta.x) / 2f;
                    RotateAroundCenter(avgDelta * rotationSpeed);
                }
            }
        }
    }

    void HandleDesktopInput()
    {
        // WASD or Arrow Keys - Pan camera
        float horizontal = Input.GetAxis("Horizontal"); // A/D or Left/Right arrows
        float vertical = Input.GetAxis("Vertical");     // W/S or Up/Down arrows

        if (horizontal != 0 || vertical != 0)
        {
            Vector3 move = new Vector3(horizontal, 0, vertical) * keyboardMoveSpeed * Time.deltaTime;
            Vector3 newPos = transform.position + move;

            // Clamp to bounds
            newPos.x = Mathf.Clamp(newPos.x, minX, maxX);
            newPos.z = Mathf.Clamp(newPos.z, minZ, maxZ);
            newPos.y = Mathf.Clamp(newPos.y, minY, maxY);

            transform.position = newPos;
        }

        // Q/E - Rotate camera
        if (Input.GetKey(KeyCode.Q))
        {
            transform.RotateAround(GetBoardCenter(), Vector3.up, -keyboardRotationSpeed * Time.deltaTime);
        }
        if (Input.GetKey(KeyCode.E))
        {
            transform.RotateAround(GetBoardCenter(), Vector3.up, keyboardRotationSpeed * Time.deltaTime);
        }

        // Mouse middle button - Pan camera
        if (Input.GetMouseButton(2) || (Input.GetMouseButton(0) && Input.GetKey(KeyCode.LeftShift)))
        {
            if (Input.GetMouseButtonDown(2) || (Input.GetMouseButtonDown(0) && Input.GetKey(KeyCode.LeftShift)))
            {
                lastMousePosition = Input.mousePosition;
            }

            Vector3 delta = Input.mousePosition - lastMousePosition;
            PanCamera(delta);
            lastMousePosition = Input.mousePosition;
        }

        // Mouse right button - Rotate camera
        if (Input.GetMouseButton(1))
        {
            float mouseX = Input.GetAxis("Mouse X");
            RotateAroundCenter(mouseX * rotationSpeed * 50f);
        }

        // Mouse wheel - Zoom
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0)
        {
            Zoom(scroll * 50f);
        }

        // Store last mouse position for next frame
        if (Input.GetMouseButton(2) || Input.GetMouseButton(1))
        {
            lastMousePosition = Input.mousePosition;
        }
    }

    void PanCamera(Vector3 delta)
    {
        // Convert screen space delta to world space movement
        Vector3 right = transform.right;
        Vector3 forward = transform.forward;

        // Remove Y component to keep movement horizontal
        right.y = 0;
        forward.y = 0;
        right.Normalize();
        forward.Normalize();

        Vector3 move = (-right * delta.x - forward * delta.y) * panSpeed * Time.deltaTime;
        Vector3 newPos = transform.position + move;

        // Clamp to bounds
        newPos.x = Mathf.Clamp(newPos.x, minX, maxX);
        newPos.z = Mathf.Clamp(newPos.z, minZ, maxZ);
        newPos.y = Mathf.Clamp(newPos.y, minY, maxY);

        transform.position = newPos;
    }

    void Zoom(float increment)
    {
        // Move camera forward/backward along its look direction
        Vector3 forward = transform.forward;
        Vector3 newPos = transform.position + forward * increment * Time.deltaTime;

        // Clamp Y position to zoom limits
        newPos.y = Mathf.Clamp(newPos.y, minZoom, maxZoom);

        // Also respect other bounds
        newPos.x = Mathf.Clamp(newPos.x, minX, maxX);
        newPos.z = Mathf.Clamp(newPos.z, minZ, maxZ);

        transform.position = newPos;
    }

    void RotateAroundCenter(float delta)
    {
        Vector3 center = GetBoardCenter();
        transform.RotateAround(center, Vector3.up, delta * Time.deltaTime);
    }

    Vector3 GetBoardCenter()
    {
        // Center of the 30x30 board
        return new Vector3(15f, 0f, 15f);
    }
}

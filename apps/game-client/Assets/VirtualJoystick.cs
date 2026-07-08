using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    [Header("Joystick Settings")]
    public float joystickRadius = 100f;
    public RectTransform joystickBackground;
    public RectTransform joystickHandle;

    private Vector2 inputVector;
    private Vector2 joystickCenter;
    private bool isDragging = false;
    private Canvas canvas;

    void Start()
    {
        canvas = GetComponentInParent<Canvas>();
        // Keep joystick visible for testing
        // HideJoystick();

        // Store the center position of the joystick background
        joystickCenter = joystickBackground.anchoredPosition;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        ShowJoystick();
        isDragging = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging) return;

        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            joystickBackground,
            eventData.position,
            eventData.pressEventCamera,
            out localPoint
        );

        Vector2 direction = localPoint.normalized;
        float distance = Mathf.Min(localPoint.magnitude, joystickRadius);

        inputVector = direction * (distance / joystickRadius);
        joystickHandle.anchoredPosition = direction * distance;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isDragging = false;
        inputVector = Vector2.zero;
        joystickHandle.anchoredPosition = Vector2.zero;
        // HideJoystick();
    }

    private void ShowJoystick()
    {
        joystickBackground.gameObject.SetActive(true);
        joystickHandle.gameObject.SetActive(true);
    }

    private void HideJoystick()
    {
        joystickBackground.gameObject.SetActive(false);
        joystickHandle.gameObject.SetActive(false);
    }

    public Vector2 GetInputVector()
    {
        return inputVector;
    }

    public bool IsDragging()
    {
        return isDragging;
    }
}

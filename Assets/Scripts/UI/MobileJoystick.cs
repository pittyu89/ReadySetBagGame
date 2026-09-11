using UnityEngine;
using UnityEngine.EventSystems;

public class MobileJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [SerializeField] private RectTransform joystickBase;
    [SerializeField] private RectTransform joystickHandle;
    [SerializeField] private float handleRange = 30f;

    private Vector2 inputDirection = Vector2.zero;
    private bool isActive = false;

    private static MobileJoystick instance;

    private void Awake()
    {
        instance = this;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        isActive = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            joystickBase, 
            eventData.position, 
            eventData.pressEventCamera, 
            out Vector2 localPos))
        {
            // Clamp position to handleRange circle
            if (localPos.magnitude > handleRange)
            {
                inputDirection = localPos.normalized;
            }
            else
            {
                inputDirection = (localPos / handleRange).normalized;
            }

            // Update handle position (follow touch up to handleRange limit)
            Vector2 handlePosition = localPos;
            if (handlePosition.magnitude > handleRange)
            {
                handlePosition = handlePosition.normalized * handleRange;
            }
            joystickHandle.anchoredPosition = handlePosition;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isActive = false;
        inputDirection = Vector2.zero;
        joystickHandle.anchoredPosition = Vector2.zero;
    }

    /// <summary>
    /// Get horizontal joystick input (-1 to 1)
    /// </summary>
    public static float GetHorizontalInput()
    {
        return instance != null ? instance.inputDirection.x : 0f;
    }

    /// <summary>
    /// Get vertical joystick input (-1 to 1)
    /// </summary>
    public static float GetVerticalInput()
    {
        return instance != null ? instance.inputDirection.y : 0f;
    }

    /// <summary>
    /// Check if joystick is currently being used
    /// </summary>
    public static bool IsActive()
    {
        return instance != null && instance.isActive;
    }
}

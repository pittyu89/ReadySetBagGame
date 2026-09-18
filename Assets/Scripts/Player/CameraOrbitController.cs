using UnityEngine;
using UnityEngine.EventSystems;
using Cinemachine;

/// <summary>
/// Orbits the third-person follow camera around the player when the screen is held and dragged.
///
/// Drives the Transposer's follow offset in world space; the Composer aim keeps the camera
/// looking at the player, and the CinemachineCollider on the same virtual camera pulls it in
/// front of any wall between it and the player.
///
/// A drag only orbits when it starts off the UI, so the joystick, buttons and open panels keep
/// their touches. Mobile: any finger that lands on empty screen orbits. Editor/desktop: hold
/// either mouse button on empty screen and drag.
/// </summary>
[RequireComponent(typeof(CinemachineVirtualCamera))]
public class CameraOrbitController : MonoBehaviour
{
    [Header("Framing")]
    [Tooltip("Height above the character's feet that the camera orbits around and looks at.")]
    [SerializeField] private float pivotHeight = 1.1f;
    [SerializeField] private float distance = 4f;

    [Header("Starting Angle")]
    [SerializeField] private float startYaw = 0f;
    [SerializeField] private float startPitch = 14f;

    [Header("Limits")]
    [SerializeField] private float minPitch = -5f;
    [SerializeField] private float maxPitch = 60f;

    [Header("Input")]
    [Tooltip("Degrees turned when dragging the full height of the screen. Measured against " +
             "screen height so the feel is the same on every resolution.")]
    [SerializeField] private float sensitivity = 220f;
    [SerializeField] private bool invertY = false;

    private CinemachineTransposer transposer;
    private CinemachineComposer composer;
    private float yaw;
    private float pitch;

    // Finger currently orbiting the camera, or -1. Only one finger orbits at a time so a
    // joystick thumb plus a camera thumb never fight over the view.
    private int orbitFingerId = -1;
    private bool mouseOrbiting;
    private Vector3 lastMousePosition;

    void Awake()
    {
        var vcam = GetComponent<CinemachineVirtualCamera>();
        transposer = vcam.GetCinemachineComponent<CinemachineTransposer>();
        composer = vcam.GetCinemachineComponent<CinemachineComposer>();

        yaw = startYaw;
        pitch = startPitch;
        ApplyOffset();
    }

    void Update()
    {
        // The view holds still while the character shows off the go-bag. Any drag in progress
        // is dropped, so the camera doesn't jump when the pose ends mid-drag.
        if (BagPickupPose.IsPlaying)
        {
            orbitFingerId = -1;
            mouseOrbiting = false;
            ApplyOffset();
            return;
        }

        Vector2 delta = Input.touchCount > 0 ? ReadTouchDelta() : ReadMouseDelta();

        if (delta != Vector2.zero)
        {
            float scale = sensitivity / Mathf.Max(1, Screen.height);
            yaw += delta.x * scale;
            pitch += (invertY ? delta.y : -delta.y) * scale;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            yaw = Mathf.Repeat(yaw, 360f);
        }

        ApplyOffset();
    }

    private Vector2 ReadTouchDelta()
    {
        mouseOrbiting = false;
        Vector2 delta = Vector2.zero;

        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);

            if (touch.phase == TouchPhase.Began)
            {
                if (orbitFingerId == -1 && !IsOverUI(touch.fingerId))
                    orbitFingerId = touch.fingerId;
            }
            else if (touch.fingerId == orbitFingerId)
            {
                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                    orbitFingerId = -1;
                else
                    delta = touch.deltaPosition;
            }
        }
        return delta;
    }

    private Vector2 ReadMouseDelta()
    {
        orbitFingerId = -1;

        bool pressed = Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
        bool held = Input.GetMouseButton(0) || Input.GetMouseButton(1);

        if (pressed && !mouseOrbiting && !IsOverUI(-1))
        {
            mouseOrbiting = true;
            lastMousePosition = Input.mousePosition;
            return Vector2.zero;
        }

        if (!held)
        {
            mouseOrbiting = false;
            return Vector2.zero;
        }

        if (!mouseOrbiting)
            return Vector2.zero;

        Vector2 delta = Input.mousePosition - lastMousePosition;
        lastMousePosition = Input.mousePosition;
        return delta;
    }

    private static bool IsOverUI(int pointerId)
    {
        if (EventSystem.current == null)
            return false;
        return pointerId < 0
            ? EventSystem.current.IsPointerOverGameObject()
            : EventSystem.current.IsPointerOverGameObject(pointerId);
    }

    private void ApplyOffset()
    {
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 pivot = Vector3.up * pivotHeight;

        if (transposer != null)
            transposer.m_FollowOffset = pivot + rotation * new Vector3(0f, 0f, -distance);

        if (composer != null)
            composer.m_TrackedObjectOffset = pivot;
    }
}

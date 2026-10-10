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

    [Header("Petting the Cat")]
    [Tooltip("Camera distance while petting; it eases in on the player and cat and holds still.")]
    [SerializeField] private float petDistance = 3f;
    [Tooltip("Height aimed at while petting: lower than the usual pivot so the short cat is " +
             "framed along with the player.")]
    [SerializeField] private float petPivotHeight = 1f;
    [Tooltip("Seconds to zoom in (and back out).")]
    [SerializeField] private float petZoomTime = 0.5f;

    [Header("Go-Bag Reveal")]
    [Tooltip("Share of the screen height the character and the bag overhead fill while the " +
             "\"You got a ...\" reveal is up. The camera levels out and moves in to frame them.")]
    [SerializeField] private float revealFill = 0.7f;
    [Tooltip("Widest the lens may open when a wall stops the camera backing far enough away " +
             "to fit the figure. Wider fits more but bends the room at the edges.")]
    [SerializeField] private float maxRevealFov = 90f;
    [Tooltip("Seconds to turn to the reveal (and back).")]
    [SerializeField] private float revealZoomTime = 0.6f;

    [Header("Input")]
    [Tooltip("Degrees turned when dragging the full height of the screen. Measured against " +
             "screen height so the feel is the same on every resolution.")]
    [SerializeField] private float sensitivity = 220f;
    [SerializeField] private bool invertY = false;

    private CinemachineVirtualCamera vcam;
    private CinemachineTransposer transposer;
    private CinemachineComposer composer;
    private float yaw;
    private float pitch;

    // 0 = normal view, 1 = zoomed in on the petting. The shift from the player to the petting
    // focus is kept after petting ends, so the zoom-out drifts back instead of snapping.
    private PlayerController player;
    private float petBlend;
    private Vector3 petShift;

    // 0 = normal view, 1 = level and framed on the go-bag reveal. The framing is kept after the
    // reveal ends so the camera eases back from it.
    private float revealBlend;
    private Vector3 revealPivot;
    private float revealDistance;
    // In a cramped room the collider stops the camera short of revealDistance, so the lens
    // widens instead to keep the whole figure on screen
    private float baseFov;
    private float revealFov;

    // Finger currently orbiting the camera, or -1. Only one finger orbits at a time so a
    // joystick thumb plus a camera thumb never fight over the view.
    private int orbitFingerId = -1;
    private bool mouseOrbiting;
    private Vector3 lastMousePosition;

    void Awake()
    {
        vcam = GetComponent<CinemachineVirtualCamera>();
        transposer = vcam.GetCinemachineComponent<CinemachineTransposer>();
        composer = vcam.GetCinemachineComponent<CinemachineComposer>();

        yaw = startYaw;
        pitch = startPitch;
        baseFov = revealFov = vcam.m_Lens.FieldOfView;
        ApplyOffset();
    }

    public float Yaw => yaw;
    public float Pitch => pitch;

    /// <summary>Turns the camera to a saved angle, for resuming a drill.</summary>
    public void SetAngles(float newYaw, float newPitch)
    {
        yaw = Mathf.Repeat(newYaw, 360f);
        pitch = Mathf.Clamp(newPitch, minPitch, maxPitch);
        ApplyOffset();
    }

    void Update()
    {
        UpdatePetZoom();
        UpdateRevealZoom();

        // The view holds still while the character shows off the go-bag or pets the cat. Any
        // drag in progress is dropped, so the camera doesn't jump when that ends mid-drag.
        if (BagPickupPose.IsPlaying || petBlend > 0f || revealBlend > 0f)
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

    private void UpdatePetZoom()
    {
        // The follow target is assigned when the character spawns
        if (player == null && vcam.Follow != null)
            player = vcam.Follow.GetComponentInParent<PlayerController>();

        bool petting = player != null && player.IsPetting;
        if (petting)
        {
            // Offsets are world-space (the transposer binds in world space)
            petShift = player.PetFocus - player.transform.position;
            petShift.y = 0f;
        }

        float step = petZoomTime > 0f ? Time.deltaTime / petZoomTime : 1f;
        petBlend = Mathf.MoveTowards(petBlend, petting ? 1f : 0f, step);
    }

    /// <summary>
    /// Frames the character and the bag overhead while the go-bag reveal is up, from straight
    /// in front. The game is paused then, so this runs on real time.
    /// </summary>
    private void UpdateRevealZoom()
    {
        Bounds figure;
        if (BagPickupPose.TryGetRevealFigure(out figure) && vcam.Follow != null)
        {
            float height = Mathf.Max(0.01f, figure.size.y);
            // Aim a little above the middle, so the figure sits low enough to leave room for
            // the words above it
            revealPivot = figure.center + Vector3.up * height * 0.08f - vcam.Follow.position;
            float halfFov = baseFov * 0.5f * Mathf.Deg2Rad;
            float fill = Mathf.Max(0.05f, revealFill);
            revealDistance = height / (fill * 2f * Mathf.Tan(halfFov));

            // Where the camera actually got to, after the collider pulled it in front of walls
            Camera cam = Camera.main;
            float actual = cam != null ? Vector3.Distance(cam.transform.position, figure.center) : revealDistance;
            float neededFov = 2f * Mathf.Atan(height / (fill * 2f * Mathf.Max(0.1f, actual))) * Mathf.Rad2Deg;
            revealFov = Mathf.Clamp(neededFov, baseFov, Mathf.Max(baseFov, maxRevealFov));

            float step = revealZoomTime > 0f ? Time.unscaledDeltaTime / revealZoomTime : 1f;
            revealBlend = Mathf.MoveTowards(revealBlend, 1f, step);
        }
        else
        {
            float step = revealZoomTime > 0f ? Time.deltaTime / revealZoomTime : 1f;
            revealBlend = Mathf.MoveTowards(revealBlend, 0f, step);
        }
    }

    private void ApplyOffset()
    {
        float zoom = Mathf.SmoothStep(0f, 1f, petBlend);
        Vector3 pivot = Vector3.up * Mathf.Lerp(pivotHeight, petPivotHeight, zoom) + petShift * zoom;
        float currentDistance = Mathf.Lerp(distance, petDistance, zoom);

        // The reveal levels the camera out, so the character is seen flat on
        float reveal = Mathf.SmoothStep(0f, 1f, revealBlend);
        Quaternion rotation = Quaternion.Euler(Mathf.Lerp(pitch, 0f, reveal), yaw, 0f);
        pivot = Vector3.Lerp(pivot, revealPivot, reveal);
        currentDistance = Mathf.Lerp(currentDistance, revealDistance, reveal);
        vcam.m_Lens.FieldOfView = Mathf.Lerp(baseFov, revealFov, reveal);

        if (transposer != null)
            transposer.m_FollowOffset = pivot + rotation * new Vector3(0f, 0f, -currentDistance);

        // The aim offset is in the look-at target's local space, and BillboardToCamera turns
        // the character to the camera's yaw - so undo that turn, or the sideways shift toward
        // the cat would swing around with the camera
        if (composer != null)
        {
            Transform target = vcam.LookAt;
            composer.m_TrackedObjectOffset = target != null
                ? Quaternion.Inverse(target.rotation) * pivot
                : pivot;
        }
    }
}

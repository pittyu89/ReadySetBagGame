using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The window onto the arm in the first-aid minigame: turns it so the player can look it
/// over, and converts a touch on that window into a spot on the arm's texture.
///
/// The arm is a real mesh filmed by its own camera into a RenderTexture, which this sits
/// on as a RawImage. That keeps a 3D prop inside a UI panel without putting it in the
/// room, and means the same drag can either spin the arm or dab at it depending on
/// whether a tool is in hand.
///
/// Input goes through the EventSystem rather than polling Input, so it behaves the same on
/// a phone and in the Editor.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class ArmInspector : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Header("Rig")]
    [Tooltip("Films the arm into the RawImage this sits on.")]
    [SerializeField] private Camera armCamera;
    [Tooltip("Spun by a drag. Usually the arm's own transform.")]
    [SerializeField] private Transform armPivot;
    [Tooltip("Layer the arm sits on, so the pick ray cannot hit the room.")]
    [SerializeField] private LayerMask armLayer = ~0;

    [Header("Rotation")]
    [Tooltip("Degrees turned per unit dragged across the window.")]
    [SerializeField] private float degreesPerPixel = 0.45f;
    [Tooltip("Eases the spin instead of pinning it to the finger, so letting go coasts.")]
    [SerializeField] private float spinDamping = 9f;
    [Tooltip("Slow drift while untouched, hinting the arm can be turned. 0 holds it still.")]
    [SerializeField] private float idleSpinSpeed = 6f;

    /// <summary>
    /// False while a tool is in hand: a drag then dabs at the arm rather than spinning it,
    /// or the player could never hold the swab on one spot.
    /// </summary>
    public bool AllowRotation { get; set; } = true;

    /// <summary>True while a finger is down on the window.</summary>
    public bool IsHeld { get; private set; }

    /// <summary>Where that finger is, in screen space. Only meaningful while held.</summary>
    public Vector2 PointerPosition { get; private set; }

    private RectTransform selfRect;
    private Canvas parentCanvas;
    private float spinVelocity = 0f;
    private bool hasDragged = false;

    private void Awake()
    {
        selfRect = (RectTransform)transform;
        parentCanvas = GetComponentInParent<Canvas>();
    }

    private void OnDisable()
    {
        IsHeld = false;
        spinVelocity = 0f;
    }

    private void Update()
    {
        if (armPivot == null)
            return;

        if (!IsHeld || !AllowRotation)
        {
            // Coast to a stop, then fall back to the idle drift
            spinVelocity = Mathf.Lerp(spinVelocity, 0f, 1f - Mathf.Exp(-spinDamping * Time.unscaledDeltaTime));

            // Only drift again when nothing is being held against the arm. With a tool in
            // hand the arm has to stand still, or the bruise the player is carefully
            // holding the bottle on creeps out from under it.
            if (Mathf.Abs(spinVelocity) < 0.5f && !IsHeld && AllowRotation)
                spinVelocity = idleSpinSpeed;
        }

        armPivot.Rotate(Vector3.up, spinVelocity * Time.unscaledDeltaTime, Space.Self);
    }

    /// <summary>
    /// Puts the arm back to its starting pose. Called before a run so the player always
    /// gets the same first look at it.
    /// </summary>
    public void ResetPose(Quaternion pose)
    {
        if (armPivot != null)
            armPivot.localRotation = pose;

        spinVelocity = 0f;
        IsHeld = false;
    }

    /// <summary>
    /// Works out which point of the arm's texture is under a screen position, by firing
    /// the same ray the arm camera would see through that pixel of the window.
    ///
    /// Needs a MeshCollider on the arm and Read/Write enabled on its mesh — without the
    /// readable mesh Unity cannot report a texture coordinate and this always fails.
    /// </summary>
    public bool TryGetUV(Vector2 screenPosition, out Vector2 uv)
    {
        uv = Vector2.zero;

        if (armCamera == null || selfRect == null)
            return false;

        Camera uiCamera = null;
        if (parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            uiCamera = parentCanvas.worldCamera;

        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(selfRect, screenPosition, uiCamera, out local))
            return false;

        // Local point to 0-1 across the window, which is what the arm camera calls a
        // viewport coordinate — the RawImage shows exactly that camera's view.
        Rect rect = selfRect.rect;
        Vector2 viewport = new Vector2(
            Mathf.InverseLerp(rect.xMin, rect.xMax, local.x),
            Mathf.InverseLerp(rect.yMin, rect.yMax, local.y));

        if (viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
            return false;

        Ray ray = armCamera.ViewportPointToRay(viewport);

        RaycastHit hit;
        if (!Physics.Raycast(ray, out hit, armCamera.farClipPlane, armLayer))
            return false;

        uv = hit.textureCoord;
        return true;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        IsHeld = true;
        hasDragged = false;
        PointerPosition = eventData.position;
        spinVelocity = 0f;
    }

    public void OnDrag(PointerEventData eventData)
    {
        PointerPosition = eventData.position;
        hasDragged = true;

        if (!AllowRotation)
            return;

        // Straight from the finger's movement, so the arm tracks the drag rather than
        // lagging behind it; the damping above only takes over once the finger lifts.
        float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        spinVelocity = -eventData.delta.x * degreesPerPixel / dt;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        IsHeld = false;
        PointerPosition = eventData.position;

        // A tap with no movement should not fling the arm
        if (!hasDragged)
            spinVelocity = 0f;
    }
}

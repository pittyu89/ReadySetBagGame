using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// The tuning knob on the radio. The player puts a finger on it and turns, the way a real
/// dial is turned, and the knob follows the hand round rather than jumping to it.
///
/// This sits on an invisible hit area over the knob; the sprite it turns is handed over as
/// <see cref="dialVisual"/> and is rotated about its own pivot, which is authored on the
/// knob rather than in the middle of the artwork. Keeping the two apart means the hit area
/// can be made finger-sized without the visible knob growing to match.
///
/// Kept as its own component the way PocketKnifeSlider is: the minigame owns the rules,
/// this owns the input.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class RadioTunerDial : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    /// <summary>Raised on every change to the dial, turned or set in code.</summary>
    public event Action Turned;

    [Tooltip("The knob sprite. Rotated about its own pivot, so that pivot has to sit on the " +
             "knob in the artwork — see the note in RadioMinigame on how the pieces line up.")]
    [SerializeField] private RectTransform dialVisual;

    [Tooltip("How far the knob turns each way from the middle, in degrees. The whole band is " +
             "covered in this much of a turn, so a bigger number means finer tuning: at 150 " +
             "the player crosses the whole dial in about five sixths of a turn.")]
    [SerializeField] private float sweepAngle = 150f;

    [Tooltip("How far from the centre of the knob a finger has to be for its angle to be " +
             "trusted, in the hit area's own units. Right on the middle a pixel of movement " +
             "swings the angle wildly, which reads as the dial jerking on its own.")]
    [SerializeField] private float deadZoneRadius = 18f;

    private RectTransform rect;
    private Canvas canvas;

    // Where the dial is now, in degrees either side of the middle. Clockwise is negative,
    // which is what a Z rotation of -angle gives, and is the direction the band runs.
    private float angle;

    private bool armed;
    private bool hasLastAngle;
    private float lastPointerAngle;

    /// <summary>True while the player has hold of the knob.</summary>
    public bool IsTurning { get; private set; }

    /// <summary>
    /// Where the dial sits across its whole sweep, 0 at the anticlockwise end and 1 at the
    /// clockwise one. This is what the minigame tunes against.
    /// </summary>
    public float Normalised
    {
        get { return Mathf.InverseLerp(sweepAngle, -sweepAngle, angle); }
    }

    private RectTransform Rect
    {
        get
        {
            if (rect == null)
                rect = (RectTransform)transform;
            return rect;
        }
    }

    /// <summary>
    /// Hands over the canvas the dial is drawn on, which is needed to turn a touch back into
    /// a point on the knob.
    /// </summary>
    public void Configure(Canvas owningCanvas)
    {
        canvas = owningCanvas;
    }

    /// <summary>Input is ignored entirely until the minigame arms it.</summary>
    public void SetArmed(bool value)
    {
        armed = value;

        if (!armed)
        {
            IsTurning = false;
            hasLastAngle = false;
        }
    }

    /// <summary>Puts the dial at a point on its sweep, 0 to 1. Used to set a run up.</summary>
    public void SetNormalised(float t)
    {
        angle = Mathf.Lerp(sweepAngle, -sweepAngle, Mathf.Clamp01(t));
        ApplyRotation();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!armed)
            return;

        IsTurning = true;

        // Only where the finger went down is remembered here, not how far round it is. The
        // dial turns by how much the finger has moved since the last frame, so grabbing the
        // knob anywhere on its edge never makes it snap round to meet the hand.
        hasLastAngle = TryPointerAngle(eventData, out lastPointerAngle);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!armed || !IsTurning)
            return;

        float current;
        if (!TryPointerAngle(eventData, out current))
            return;

        if (!hasLastAngle)
        {
            // The finger has come back out of the dead zone. Start again from here rather
            // than counting everything that happened while it was in the middle.
            lastPointerAngle = current;
            hasLastAngle = true;
            return;
        }

        float delta = Mathf.DeltaAngle(lastPointerAngle, current);
        lastPointerAngle = current;

        float clamped = Mathf.Clamp(angle + delta, -sweepAngle, sweepAngle);
        if (Mathf.Approximately(clamped, angle))
            return;

        angle = clamped;
        ApplyRotation();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        IsTurning = false;
        hasLastAngle = false;
    }

    /// <summary>
    /// Where the finger is around the middle of the knob, in degrees. False while it is too
    /// near the centre for that angle to mean anything — see deadZoneRadius.
    /// </summary>
    private bool TryPointerAngle(PointerEventData eventData, out float result)
    {
        result = 0f;

        // A Screen Space - Overlay canvas takes a null camera here; anything else needs the
        // one actually rendering it, or the point comes back in the wrong space.
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                Rect, eventData.position, cam, out local))
            return false;

        if (local.sqrMagnitude < deadZoneRadius * deadZoneRadius)
            return false;

        result = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
        return true;
    }

    private void ApplyRotation()
    {
        if (dialVisual != null)
            dialVisual.localEulerAngles = new Vector3(0f, 0f, angle);

        if (Turned != null)
            Turned();
    }
}

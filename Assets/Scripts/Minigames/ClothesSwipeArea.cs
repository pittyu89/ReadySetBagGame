using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// The swipes that fold the spare clothes, one fold per gesture.
///
/// Two kinds of gesture, because the reference sheet asks for two:
///
///   Straight — the same as the thermal blanket's swipe. The nudge arrow faces the fold and
///   drifts along it, and a drag counts once it has gone far enough roughly that way.
///
///   Circular — the last step turns the folded shirt over. The finger has to travel most of
///   the way round a circle about the shirt, in either direction, and the rotate arrow spins
///   in place to show it.
///
/// Only one gesture is taken per drag either way, so a wandering finger cannot fold the whole
/// shirt in a single stroke.
/// </summary>
public class ClothesSwipeArea : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Straight swipe")]
    [Tooltip("The nudge arrow for straight swipes. Optional — the swipe still works without one.")]
    [SerializeField] private RectTransform arrow;

    [Tooltip("The direction the arrow art points at zero rotation. The sprite is drawn " +
             "pointing up, so 90 degrees.")]
    [SerializeField] private float arrowSpriteBaseAngle = 90f;

    [Tooltip("How far the arrow drifts along its own direction, in canvas units.")]
    [SerializeField] private float bobDistance = 22f;
    [SerializeField] private float bobSpeed = 3.2f;

    [Tooltip("How far a finger has to travel, in canvas units, before it counts as a swipe.")]
    [SerializeField] private float minSwipeDistance = 90f;

    [Tooltip("How closely the swipe has to follow the arrow. 1 demands a perfect match, 0 " +
             "takes any direction at all; the default allows a little under 60 degrees off.")]
    [SerializeField, Range(0f, 1f)] private float directionTolerance = 0.5f;

    [Header("Circular swipe")]
    [Tooltip("The rotate arrow shown for the circular swipe. Spins in place while armed.")]
    [SerializeField] private RectTransform rotateArrow;

    [Tooltip("Degrees per second the rotate arrow spins at, clockwise.")]
    [SerializeField] private float rotateArrowSpinSpeed = 120f;

    [Tooltip("How far round the circle the finger has to go, in degrees. A little short of a " +
             "full turn, since nobody draws a perfect circle with a thumb.")]
    [SerializeField] private float circleDegreesNeeded = 300f;

    [Tooltip("Points closer than this to the circle's centre are ignored, in canvas units. " +
             "Near the centre the angle swings wildly for tiny movements.")]
    [SerializeField] private float circleMinRadius = 40f;

    /// <summary>Raised once per drag, when the armed gesture is completed.</summary>
    public event Action Swiped;

    private RectTransform selfRect;
    private Canvas canvas;
    private bool armed;
    private bool circular;
    private Vector2 direction = Vector2.down;
    private Vector2 arrowRest;
    private Vector2 circleCentre;
    private bool dragging;
    private bool consumedThisDrag;
    private Vector2 dragStart;

    // Circular bookkeeping: the last usable angle and how far round the finger has gone,
    // signed so that going back the other way undoes progress rather than adding to it
    private bool hasLastAngle;
    private float lastAngle;
    private float swept;

    private void Awake()
    {
        selfRect = (RectTransform)transform;
        canvas = GetComponentInParent<Canvas>();
    }

    /// <summary>Arms a straight swipe and parks the arrow where it should sit for it.</summary>
    /// <param name="swipeDirection">Which way the fold goes.</param>
    /// <param name="arrowPosition">Where the arrow rests, in this rect's space.</param>
    public void SetDirection(Vector2 swipeDirection, Vector2 arrowPosition)
    {
        circular = false;

        direction = swipeDirection.sqrMagnitude > 0.0001f
            ? swipeDirection.normalized
            : Vector2.down;

        arrowRest = arrowPosition;

        if (arrow != null)
        {
            arrow.anchoredPosition = arrowRest;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            arrow.localEulerAngles = new Vector3(0f, 0f, angle - arrowSpriteBaseAngle);
        }
    }

    /// <summary>Arms a circular swipe about <paramref name="centre"/>.</summary>
    /// <param name="centre">What the finger circles, in this rect's space.</param>
    /// <param name="arrowPosition">Where the rotate arrow sits, in this rect's space.</param>
    public void SetCircular(Vector2 centre, Vector2 arrowPosition)
    {
        circular = true;
        circleCentre = centre;

        if (rotateArrow != null)
        {
            rotateArrow.anchoredPosition = arrowPosition;
            rotateArrow.localEulerAngles = Vector3.zero;
        }
    }

    public void SetArmed(bool value)
    {
        armed = value;
        dragging = false;
        consumedThisDrag = false;
        ResetCircle();

        if (arrow != null)
            arrow.gameObject.SetActive(value && !circular);

        if (rotateArrow != null)
            rotateArrow.gameObject.SetActive(value && circular);
    }

    private void Update()
    {
        if (!armed)
            return;

        // Unscaled, so the hints keep moving behind the blurred quiz
        if (circular)
        {
            if (rotateArrow != null)
                rotateArrow.Rotate(0f, 0f, -rotateArrowSpinSpeed * Time.unscaledDeltaTime);
        }
        else if (arrow != null && bobDistance > 0f)
        {
            float bob = Mathf.Sin(Time.unscaledTime * bobSpeed) * bobDistance;
            arrow.anchoredPosition = arrowRest + direction * bob;
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!armed)
            return;

        Vector2 local;
        if (!ToLocal(eventData, out local))
            return;

        dragging = true;
        consumedThisDrag = false;
        dragStart = local;
        ResetCircle();
        TrackCircle(local);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!armed || !dragging || consumedThisDrag)
            return;

        Vector2 local;
        if (!ToLocal(eventData, out local))
            return;

        bool done = circular ? TrackCircle(local) : IsStraightSwipe(local);
        if (!done)
            return;

        consumedThisDrag = true;

        if (Swiped != null)
            Swiped();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        dragging = false;
        consumedThisDrag = false;
        ResetCircle();
    }

    private bool IsStraightSwipe(Vector2 local)
    {
        Vector2 travelled = local - dragStart;
        if (travelled.magnitude < minSwipeDistance)
            return false;

        // Far enough — now, was it the right way?
        return Vector2.Dot(travelled.normalized, direction) >= directionTolerance;
    }

    /// <summary>
    /// Adds the finger's latest movement round the centre, and says whether it has gone far
    /// enough round yet.
    /// </summary>
    private bool TrackCircle(Vector2 local)
    {
        Vector2 offset = local - circleCentre;
        if (offset.magnitude < circleMinRadius)
            return false;

        float angle = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg;

        if (hasLastAngle)
            swept += Mathf.DeltaAngle(lastAngle, angle);

        lastAngle = angle;
        hasLastAngle = true;

        return Mathf.Abs(swept) >= circleDegreesNeeded;
    }

    private void ResetCircle()
    {
        hasLastAngle = false;
        swept = 0f;
    }

    /// <summary>Pointer to a point inside this rect, so swipes measure in canvas units.</summary>
    private bool ToLocal(PointerEventData eventData, out Vector2 local)
    {
        local = Vector2.zero;

        if (selfRect == null)
            selfRect = (RectTransform)transform;

        // A Screen Space - Overlay canvas takes a null camera here; anything else needs the
        // one actually rendering it, or the point comes back in the wrong space.
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            selfRect, eventData.position, cam, out local);
    }
}

using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// The swipe that unfolds the thermal blanket, one fold per swipe.
///
/// Each fold names the direction it opens in, and the nudge arrow is turned to match and
/// drifts back and forth along that line — the same idea as the ziplock's zipper hint, but
/// free to point any way rather than only along x, because the blanket opens diagonally.
///
/// A swipe counts once it has travelled far enough <em>and</em> gone roughly the way the arrow
/// points. Roughly, not exactly: this is played with a thumb on a phone, so the tolerance is
/// deliberately loose and only one swipe is taken per drag — a wandering finger cannot unfold
/// the whole blanket in a single stroke.
/// </summary>
public class ThermalBlanketSwipeArea : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Tooltip("The nudge arrow. Turned to face the current fold's direction and drifted along " +
             "it. Optional — the swipe still works without one.")]
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

    /// <summary>Raised once per drag, when a swipe goes the way the arrow points.</summary>
    public event Action Swiped;

    private RectTransform selfRect;
    private Canvas canvas;
    private bool armed;
    private Vector2 direction = Vector2.down;
    private Vector2 arrowRest;
    private bool dragging;
    private bool consumedThisDrag;
    private Vector2 dragStart;

    private void Awake()
    {
        selfRect = (RectTransform)transform;
        canvas = GetComponentInParent<Canvas>();
    }

    /// <summary>
    /// Points this fold's swipe, and parks the arrow where it should sit for it.
    /// </summary>
    /// <param name="swipeDirection">Which way the blanket opens.</param>
    /// <param name="arrowPosition">Where the arrow rests, in this rect's space.</param>
    public void SetDirection(Vector2 swipeDirection, Vector2 arrowPosition)
    {
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

    public void SetArmed(bool value)
    {
        armed = value;
        dragging = false;
        consumedThisDrag = false;

        if (arrow != null)
            arrow.gameObject.SetActive(value);
    }

    private void Update()
    {
        if (!armed || arrow == null || bobDistance <= 0f)
            return;

        // Drifts along the swipe line itself, so the arrow demonstrates the gesture rather
        // than just labelling it. Unscaled, so it keeps moving behind the blurred quiz.
        float bob = Mathf.Sin(Time.unscaledTime * bobSpeed) * bobDistance;
        arrow.anchoredPosition = arrowRest + direction * bob;
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
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!armed || !dragging || consumedThisDrag)
            return;

        Vector2 local;
        if (!ToLocal(eventData, out local))
            return;

        Vector2 travelled = local - dragStart;
        if (travelled.magnitude < minSwipeDistance)
            return;

        // Far enough — now, was it the right way?
        if (Vector2.Dot(travelled.normalized, direction) < directionTolerance)
            return;

        consumedThisDrag = true;

        if (Swiped != null)
            Swiped();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        dragging = false;
        consumedThisDrag = false;
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

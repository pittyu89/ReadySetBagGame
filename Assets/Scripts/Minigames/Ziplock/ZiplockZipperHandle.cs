using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The zipper on the ziplock bag, dragged from the left corner of the seal across to the
/// right to open it.
///
/// The zipper art is authored inside the same 64x64 frame as the bag itself, sitting on the
/// seal at the top-left, so the graphic is given exactly the bag's rect and lands in place
/// with nothing to line up by hand. Only <see cref="travel"/> — the rect that carries both
/// the graphic and the touch target — is moved, and it slides along x alone.
///
/// The seal behind the zipper is filled in as it goes, so the part already unzipped reads as
/// an opening rather than staying sealed all the way until the end.
///
/// A zipper let go part-way stays where it was left, the way a real one does; it is not a
/// swipe that has to be completed in one go. Once it reaches the far end the bag is open for
/// the rest of the round and the zipper stops responding.
/// </summary>
public class ZiplockZipperHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    [Tooltip("The rect slid along x. It carries both the zipper graphic and this handle, so " +
             "the drawn zipper and the thing you grab never come apart.")]
    [SerializeField] private RectTransform travel;

    [Tooltip("The gap left behind the zipper. Its fill is driven from 0 to 1 as the zipper " +
             "crosses, turning the sealed strip into an opening. Optional.")]
    [SerializeField] private Image seam;

    [Tooltip("Optional. A nudge to drag, hidden once the bag is open.")]
    [SerializeField] private GameObject dragHint;

    [Tooltip("How far the nudge arrow drifts left and right, in canvas units. 0 holds it still.")]
    [SerializeField] private float hintBobDistance = 14f;
    [SerializeField] private float hintBobSpeed = 3.4f;

    [Tooltip("How close to the far end counts as open. Short of 1 so the bag does not hang " +
             "on the last couple of pixels of a drag that has plainly been finished.")]
    [SerializeField, Range(0.7f, 1f)] private float openThreshold = 0.94f;

    /// <summary>Raised once, the moment the zipper reaches the far end.</summary>
    public event Action Opened;

    public bool IsOpen { get; private set; }

    /// <summary>0 while shut, 1 with the zipper at the far end.</summary>
    public float Progress { get; private set; }

    // Where the zipper rests shut, and how far it can travel. The resting spot is the one it
    // was authored at; the distance is handed over by the panel, which reads it off the bag
    // sprite.
    private Vector2 closedPosition;
    private bool hasClosedPosition;
    private float travelDistance = 1f;
    private RectTransform dragSpace;
    private Canvas canvas;
    private bool armed;

    /// <summary>Offset from the pointer to the zipper when the drag started.</summary>
    private float grabOffset;

    private float hintRestX;
    private bool hasHintRest;

    private void Awake()
    {
        EnsureInit();
    }

    /// <summary>
    /// Caches the authored resting places the first time anything needs them.
    ///
    /// Not left to Awake alone, because the panel resets its zipper on the way through its
    /// own Awake, which can land before this one. Anything that moves the zipper has to come
    /// through here first: a reset that ran before the resting place was captured would park
    /// the zipper at the origin and then take *that* for the closed position, leaving it
    /// sitting off the seal for the rest of the game.
    /// </summary>
    private void EnsureInit()
    {
        if (!hasClosedPosition && travel != null)
        {
            closedPosition = travel.anchoredPosition;
            hasClosedPosition = true;
        }

        if (!hasHintRest && dragHint != null)
        {
            RectTransform hint = dragHint.transform as RectTransform;
            if (hint != null)
            {
                hintRestX = hint.anchoredPosition.x;
                hasHintRest = true;
            }
        }
    }

    /// <summary>
    /// Handed how far right the zipper can go, which the panel reads off the bag sprite.
    ///
    /// Where it sits shut is not passed in — it is wherever the zipper was authored, captured
    /// once by <see cref="EnsureInit"/>. A later run finds it parked at the far end of the
    /// previous one, so capturing it again would take that for the closed position and the
    /// bag could never be opened twice.
    /// </summary>
    public void Configure(float distance, RectTransform space, Canvas owningCanvas)
    {
        EnsureInit();

        travelDistance = Mathf.Max(1f, distance);
        dragSpace = space;
        canvas = owningCanvas;
    }

    public void SetArmed(bool value)
    {
        armed = value;
    }

    /// <summary>Shuts the bag and puts the zipper back in the corner, ready for a run.</summary>
    public void ResetZipper()
    {
        EnsureInit();

        IsOpen = false;
        Progress = 0f;
        Apply(0f);

        if (dragHint != null)
            dragHint.SetActive(true);
    }

    private void Update()
    {
        BobHint();
    }

    /// <summary>
    /// Drifts the nudge arrow along the seal while the bag is still shut, so dragging the
    /// zipper reads as the thing to do rather than something to guess at.
    /// </summary>
    private void BobHint()
    {
        if (dragHint == null || IsOpen || hintBobDistance <= 0f || !hasHintRest)
            return;

        RectTransform rect = dragHint.transform as RectTransform;
        if (rect == null)
            return;

        float bob = Mathf.Sin(Time.unscaledTime * hintBobSpeed) * hintBobDistance;
        rect.anchoredPosition = new Vector2(hintRestX + bob, rect.anchoredPosition.y);
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (!armed || IsOpen || travel == null)
            return;

        // Grabbing anywhere on the handle must not snap the zipper's centre to the finger,
        // or the bag jumps open a little the instant it is touched.
        float x;
        if (ToLocalX(e, out x))
            grabOffset = travel.anchoredPosition.x - x;
    }

    public void OnDrag(PointerEventData e)
    {
        if (!armed || IsOpen || travel == null)
            return;

        float x;
        if (!ToLocalX(e, out x))
            return;

        // Forward only. A zipper does not un-zip by dragging back, and allowing it means a
        // sloppy drag that overshoots and returns closes the bag again mid-pull.
        float moved = (x + grabOffset) - closedPosition.x;
        Progress = Mathf.Max(Progress, Mathf.Clamp01(moved / travelDistance));

        Apply(Progress);

        if (Progress >= openThreshold)
            Finish();
    }

    /// <summary>Puts the zipper, and the opening trailing behind it, at the given progress.</summary>
    private void Apply(float t)
    {
        if (travel != null)
            travel.anchoredPosition = closedPosition + new Vector2(travelDistance * t, 0f);

        if (seam != null)
            seam.fillAmount = t;
    }

    private void Finish()
    {
        Progress = 1f;
        Apply(1f);

        IsOpen = true;

        if (dragHint != null)
            dragHint.SetActive(false);

        if (Opened != null)
            Opened();
    }

    /// <summary>Pointer to an x inside the play area, so the zipper tracks the finger.</summary>
    private bool ToLocalX(PointerEventData e, out float x)
    {
        x = 0f;
        if (dragSpace == null)
            return false;

        // A Screen Space - Overlay canvas takes a null camera here; anything else needs the
        // one actually rendering it, or the point comes back in the wrong space.
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                dragSpace, e.position, cam, out local))
            return false;

        x = local.x;
        return true;
    }
}

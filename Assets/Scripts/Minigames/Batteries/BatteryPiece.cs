using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Something the player can pick up in the batteries round — the cover over the radio's
/// battery compartment, one of the worn-out batteries inside it, or one of the fresh ones
/// waiting beside the radio.
///
/// It only reports drags and taps and remembers where it was authored. Whether a drop takes
/// the cover off, pulls an old battery out or seats a new one is decided by
/// <see cref="BatteriesMinigame"/>, the way <see cref="DocumentCard"/> leaves the filing rules
/// to its panel.
///
/// The rect is the drawn object only — the battery, or the cover plate — so it is also exactly
/// what can be grabbed. The sprite itself is a full sheet on a child image, offset so the art
/// lands on this rect's centre; that keeps it at the same pixel scale as the radio and means a
/// flip turns the battery about its own middle rather than the middle of the sheet.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class BatteryPiece : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler,
    IPointerClickHandler
{
    public enum Kind
    {
        Cover,
        OldBattery,
        NewBattery
    }

    public event Action<BatteryPiece> PickedUp;
    public event Action<BatteryPiece> Dropped;
    public event Action<BatteryPiece> Tapped;

    [Tooltip("Cover: taken off and thrown away first. Old Battery: pulled out and thrown " +
             "away. New Battery: flipped with a tap and dragged into a channel.")]
    [SerializeField] private Kind kind = Kind.NewBattery;

    private RectTransform rect;
    private RectTransform dragSpace;
    private Canvas canvas;
    private CanvasGroup group;
    private bool armed;

    // Kept from the moment it is picked up, so the piece stays under the finger at the spot
    // it was grabbed instead of jumping to centre itself on it.
    private Vector3 grabOffset;

    public Kind PieceKind { get { return kind; } }

    /// <summary>Where this piece was authored, so a reset puts it back.</summary>
    public Transform HomeParent { get; private set; }
    public Vector2 HomePosition { get; private set; }
    public int HomeSiblingIndex { get; private set; }

    /// <summary>Which way round it was authored — read off its rotation, see <see cref="PositiveUp"/>.</summary>
    public bool HomePositiveUp { get; private set; }

    /// <summary>
    /// Whether the + end is at the top. Kept as a flag rather than read off the rotation, so it
    /// is already the new answer while a flip is still turning the art over.
    /// </summary>
    public bool PositiveUp { get; set; }

    public bool IsDragging { get; private set; }

    public RectTransform Rect
    {
        get
        {
            if (rect == null)
                rect = (RectTransform)transform;
            return rect;
        }
    }

    /// <summary>Faded out when the piece is thrown away. Added if the scene did not author one.</summary>
    public CanvasGroup Group
    {
        get
        {
            if (group == null)
            {
                group = GetComponent<CanvasGroup>();
                if (group == null)
                    group = gameObject.AddComponent<CanvasGroup>();
            }
            return group;
        }
    }

    /// <summary>The middle of the drawn object, in world space.</summary>
    public Vector3 WorldCentre
    {
        get { return Rect.TransformPoint(Rect.rect.center); }
    }

    /// <summary>The rotation that shows the + end the way <see cref="PositiveUp"/> says.</summary>
    public Quaternion UprightRotation
    {
        get { return PositiveUp ? Quaternion.identity : Quaternion.Euler(0f, 0f, 180f); }
    }

    public void Configure(RectTransform space, Canvas owningCanvas)
    {
        dragSpace = space;
        canvas = owningCanvas;
    }

    /// <summary>
    /// Remembers where the piece was laid in the editor. Called once, before the first run
    /// moves anything, so repeated runs all start from the same radio.
    /// </summary>
    public void CaptureHome()
    {
        HomeParent = Rect.parent;
        HomePosition = Rect.anchoredPosition;
        HomeSiblingIndex = Rect.GetSiblingIndex();
        HomePositiveUp = Mathf.Abs(Mathf.DeltaAngle(Rect.localEulerAngles.z, 0f)) < 90f;
        PositiveUp = HomePositiveUp;
    }

    public void GoHome()
    {
        IsDragging = false;

        if (HomeParent != null)
            Rect.SetParent(HomeParent, false);

        Rect.anchoredPosition = HomePosition;
        Rect.SetSiblingIndex(HomeSiblingIndex);

        PositiveUp = HomePositiveUp;
        Rect.localRotation = UprightRotation;
        Rect.localScale = Vector3.one;

        gameObject.SetActive(true);
        Group.alpha = 1f;
        Group.blocksRaycasts = true;
    }

    public void SetArmed(bool value)
    {
        armed = value;
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (!armed)
            return;

        IsDragging = true;

        // Told first, so the panel can lift the piece onto the drag layer before the grab
        // point is measured against where it now is.
        if (PickedUp != null)
            PickedUp(this);

        Vector3 pointer;
        grabOffset = ToWorld(e, out pointer) ? Rect.position - pointer : Vector3.zero;
    }

    public void OnDrag(PointerEventData e)
    {
        // Follows through to the end of a drag even if the round disarms it part-way, so a
        // piece is never left hanging off the finger.
        if (!IsDragging)
            return;

        Vector3 pointer;
        if (ToWorld(e, out pointer))
            Rect.position = pointer + grabOffset;
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (!IsDragging)
            return;

        IsDragging = false;

        if (Dropped != null)
            Dropped(this);
    }

    public void OnPointerClick(PointerEventData e)
    {
        // The event system never counts a drag as a click, so this is only ever a tap.
        if (!armed || IsDragging)
            return;

        if (Tapped != null)
            Tapped(this);
    }

    /// <summary>
    /// Screen point to world space on the play area. Going through world space rather than an
    /// anchored position means the piece follows the finger whichever layer it is parented to.
    /// </summary>
    private bool ToWorld(PointerEventData e, out Vector3 world)
    {
        world = Vector3.zero;
        if (dragSpace == null)
            return false;

        // A Screen Space - Overlay canvas takes a null camera here; anything else needs the
        // one actually rendering it, or the point comes back in the wrong space.
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        return RectTransformUtility.ScreenPointToWorldPointInRectangle(
            dragSpace, e.position, cam, out world);
    }
}

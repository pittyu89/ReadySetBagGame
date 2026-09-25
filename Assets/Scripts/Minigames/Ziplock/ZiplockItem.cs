using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// One of the things laid out around the ziplock bag. Some belong in it (the pad and pen, the
/// contact card, the dust mask, the batteries, the medicine) and some are go-bag Nuisance items that the
/// player has to leave out (the phone, the power bank, the matches).
///
/// It only reports drag events. Whether the bag is open, whether a drop landed inside it and
/// where the item comes to rest are all decided by <see cref="ZiplockMinigame"/>, the way
/// <see cref="DraggablePill"/> leaves the sorting rules to the medication panel.
///
/// An item stays draggable once it is in the bag, so anything put in can be taken back out.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ZiplockItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public event Action<ZiplockItem> PickedUp;
    public event Action<ZiplockItem> Dropped;

    [Tooltip("Off for the Nuisance items. The bag refuses them, and the round only needs the " +
             "ones with this on to be packed.")]
    [SerializeField] private bool belongsInBag = true;

    public bool BelongsInBag { get { return belongsInBag; } }

    private RectTransform rect;
    private RectTransform dragSpace;
    private Canvas canvas;
    private bool armed;

    /// <summary>
    /// Where this item was authored, so a reset puts it back on the table rather than
    /// wherever the last run happened to leave it.
    /// </summary>
    public Vector2 HomePosition { get; private set; }

    /// <summary>Authored size, restored when an item is taken back out of the bag.</summary>
    public Vector2 HomeSize { get; private set; }

    /// <summary>Set by the panel once the item has come to rest inside the bag.</summary>
    public bool IsPacked { get; set; }

    /// <summary>Which slot inside the bag it is filling, or -1 when it is still outside.</summary>
    public int Slot { get; set; }

    public RectTransform Rect
    {
        get
        {
            if (rect == null)
                rect = (RectTransform)transform;
            return rect;
        }
    }

    public void Configure(RectTransform space, Canvas owningCanvas)
    {
        dragSpace = space;
        canvas = owningCanvas;
        Slot = -1;
    }

    /// <summary>
    /// Remembers where the item was placed in the editor. Called once, before the first run
    /// moves anything, so repeated runs all start from the authored layout.
    /// </summary>
    public void CaptureHome()
    {
        HomePosition = Rect.anchoredPosition;
        HomeSize = Rect.sizeDelta;
    }

    public void GoHome()
    {
        Rect.anchoredPosition = HomePosition;
        Rect.sizeDelta = HomeSize;
        Rect.localRotation = Quaternion.identity;
        IsPacked = false;
        Slot = -1;
    }

    public void SetArmed(bool value)
    {
        armed = value;
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (!armed)
            return;

        if (PickedUp != null)
            PickedUp(this);

        MoveTo(e);
    }

    public void OnDrag(PointerEventData e)
    {
        if (!armed)
            return;

        MoveTo(e);
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (!armed)
            return;

        if (Dropped != null)
            Dropped(this);
    }

    private void MoveTo(PointerEventData e)
    {
        Vector2 local;
        if (!ToLocal(e, out local))
            return;

        Rect.anchoredPosition = local;
    }

    /// <summary>
    /// Screen point to a position inside the play area, so the item sits under the finger
    /// whatever the canvas scale or screen size.
    /// </summary>
    private bool ToLocal(PointerEventData e, out Vector2 local)
    {
        local = Vector2.zero;
        if (dragSpace == null)
            return false;

        // A Screen Space - Overlay canvas takes a null camera here; anything else needs the
        // one actually rendering it, or the point comes back in the wrong space.
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            dragSpace, e.position, cam, out local);
    }
}

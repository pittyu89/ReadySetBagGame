using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// One pill in the medication minigame. Carries which day it belongs to and reports drag
/// events; the sorting rules live in <see cref="MedicationMinigame"/>, the way the other
/// minigames split input from scoring.
///
/// A pill stays draggable for the whole round, including after it has landed in a container,
/// so a pill dropped in the wrong day can be lifted back out.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class DraggablePill : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public event Action<DraggablePill> PickedUp;
    public event Action<DraggablePill, Vector2> Dragged;
    public event Action<DraggablePill> Dropped;

    /// <summary>0 = blue / day 1, 1 = red / day 2, 2 = green / day 3.</summary>
    public int ColourIndex { get; private set; }

    private RectTransform rect;
    private RectTransform dragSpace;
    private Canvas canvas;
    private bool armed;

    public RectTransform Rect
    {
        get
        {
            if (rect == null)
                rect = (RectTransform)transform;
            return rect;
        }
    }

    public void Configure(int colourIndex, RectTransform space, Canvas owningCanvas)
    {
        ColourIndex = colourIndex;
        dragSpace = space;
        canvas = owningCanvas;
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

        if (Dragged != null)
            Dragged(this, local);
    }

    /// <summary>
    /// Screen point to a position inside the play area, so the pill sits under the finger
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

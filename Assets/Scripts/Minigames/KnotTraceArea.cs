using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// The surface the player drags a finger across to trace the knot. Reports where the pointer
/// is in the path's own local space and nothing else — the rules live in
/// <see cref="RopeKnotMinigame"/>, the way PocketKnifeSlider splits input from scoring.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class KnotTraceArea : MonoBehaviour,
    IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerUpHandler
{
    /// <summary>Pointer pressed down, with the local point it landed on.</summary>
    public event Action<Vector2> Pressed;

    /// <summary>Pointer moved while held.</summary>
    public event Action<Vector2> Moved;

    /// <summary>Pointer released.</summary>
    public event Action Released;

    private RectTransform rect;
    private RectTransform space;
    private Canvas canvas;
    private bool armed;

    public bool IsHeld { get; private set; }

    /// <summary>
    /// <paramref name="localSpace"/> is the rect that points are reported relative to —
    /// normally the same rect the guide and the checkpoints live in.
    /// </summary>
    public void Configure(RectTransform localSpace, Canvas owningCanvas)
    {
        rect = (RectTransform)transform;
        space = localSpace;
        canvas = owningCanvas;
    }

    public void SetArmed(bool value)
    {
        armed = value;
        if (!armed)
            IsHeld = false;
    }

    public void OnPointerDown(PointerEventData e)
    {
        if (!armed)
            return;

        Vector2 p;
        if (!ToLocal(e, out p))
            return;

        IsHeld = true;
        if (Pressed != null)
            Pressed(p);
    }

    public void OnBeginDrag(PointerEventData e)
    {
        // Pointer down already opened the stroke; this only matters if the press was missed
        if (armed && !IsHeld)
            OnPointerDown(e);
    }

    public void OnDrag(PointerEventData e)
    {
        if (!armed || !IsHeld)
            return;

        Vector2 p;
        if (ToLocal(e, out p) && Moved != null)
            Moved(p);
    }

    public void OnEndDrag(PointerEventData e)
    {
        Finish();
    }

    public void OnPointerUp(PointerEventData e)
    {
        Finish();
    }

    private void Finish()
    {
        if (!IsHeld)
            return;

        IsHeld = false;
        if (Released != null)
            Released();
    }

    /// <summary>
    /// Screen point to a position inside the path's rect, so tracing lines up with the guide
    /// whatever the canvas scale or screen size.
    /// </summary>
    private bool ToLocal(PointerEventData e, out Vector2 local)
    {
        local = Vector2.zero;

        RectTransform target = space != null ? space : rect;
        if (target == null)
            return false;

        // A Screen Space - Overlay canvas takes a null camera here; anything else needs the
        // camera actually rendering it, or the point comes back in the wrong space.
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            target, e.position, cam, out local);
    }
}

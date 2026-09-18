using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// The draggable knife in the rope minigame. Slides along one axis only, clamped between
/// the 0 m and 10 m anchors, and tells the minigame whenever it has moved so the two rope
/// halves can be resized to meet it.
///
/// Kept as its own component the way WhistleTapButton is: the minigame owns the rules, this
/// owns the input.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class PocketKnifeSlider : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerDownHandler
{
    /// <summary>Raised on every change to the knife's position, dragged or set in code.</summary>
    public event Action Moved;

    private RectTransform rect;
    private RectTransform dragArea;
    private Canvas canvas;

    private float minX;
    private float maxX;
    private bool armed;

    /// <summary>True while the player has hold of the knife.</summary>
    public bool IsDragging { get; private set; }

    public float PositionX
    {
        get { return Rect.anchoredPosition.x; }
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
    /// Hands over the space the knife slides in. <paramref name="area"/> is the rect the
    /// pointer is measured against — normally the knife's own parent.
    /// </summary>
    public void Configure(RectTransform area, Canvas owningCanvas, float clampMinX, float clampMaxX)
    {
        dragArea = area;
        canvas = owningCanvas;
        minX = Mathf.Min(clampMinX, clampMaxX);
        maxX = Mathf.Max(clampMinX, clampMaxX);

        // Re-clamp wherever it was left by the last run
        SetPositionX(PositionX);
    }

    /// <summary>Input is ignored entirely until the minigame arms it.</summary>
    public void SetArmed(bool value)
    {
        armed = value;

        if (!armed)
            IsDragging = false;
    }

    public void SetPositionX(float x)
    {
        Vector2 p = Rect.anchoredPosition;
        p.x = Mathf.Clamp(x, minX, maxX);
        Rect.anchoredPosition = p;

        if (Moved != null)
            Moved();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        // Tapping anywhere on the blade counts as grabbing it, so a slow drag does not need
        // to start exactly on the handle.
        if (armed)
            MoveTo(eventData);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!armed)
            return;

        IsDragging = true;
        MoveTo(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!armed || !IsDragging)
            return;

        MoveTo(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        IsDragging = false;
    }

    /// <summary>
    /// Puts the knife under the pointer, in the drag area's own coordinates so it tracks
    /// correctly whatever the canvas scale or screen size.
    /// </summary>
    private void MoveTo(PointerEventData eventData)
    {
        if (dragArea == null)
            return;

        // A Screen Space - Overlay canvas takes a null camera here; anything else needs the
        // one actually rendering it, or the point comes back in the wrong space.
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                dragArea, eventData.position, cam, out local))
            return;

        SetPositionX(local.x);
    }
}

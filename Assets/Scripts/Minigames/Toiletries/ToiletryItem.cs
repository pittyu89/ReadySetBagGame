using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One of the toiletries piled beside the bag — the soap, toothpaste, toothbrush, shampoo,
/// razor and deodorant.
///
/// It reports its drags and knows how to move itself: back to the pile, into its place in
/// the bag, or a shake for the wrong spot. Which silhouette a drop landed on, and whether
/// that is the right one, is <see cref="ToiletriesMinigame"/>'s business.
///
/// The item keeps the offset it was grabbed at rather than jumping its centre to the
/// finger. The sprites are mostly empty space around a narrow object, so centring would
/// make the toothbrush leap sideways out from under the thumb.
/// </summary>
[RequireComponent(typeof(Image))]
public class ToiletryItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Tooltip("The silhouette in the bag this item belongs in. Its position is where the " +
             "item's centre goes, and its rect is the area a drop has to land in.")]
    [SerializeField] private RectTransform targetSlot;
    [Tooltip("The item's rotation once it is in the bag, in degrees. Most go in upright; " +
             "the soap and the toothbrush lie on their sides.")]
    [SerializeField] private float slotRotation = 0f;

    public event Action<ToiletryItem> PickedUp;
    public event Action<ToiletryItem, PointerEventData> Dropped;

    private RectTransform rect;
    private Canvas canvas;
    private bool armed = false;

    private Vector2 homePosition;
    private float homeRotation;
    private bool homeRecorded = false;

    // Finger to item centre at the moment of the grab, in the parent's space
    private Vector2 grabOffset;

    public RectTransform Rect
    {
        get
        {
            if (rect == null)
                rect = (RectTransform)transform;
            return rect;
        }
    }

    public RectTransform TargetSlot => targetSlot;
    public bool IsPlaced { get; private set; }
    public bool IsDragging { get; private set; }

    private void Awake()
    {
        canvas = GetComponentInParent<Canvas>();

        // The sprites are mostly transparent and the pile overlaps, so only the drawn part
        // should catch a press. Needs Read/Write on the texture.
        Image image = GetComponent<Image>();
        if (image != null && image.sprite != null && image.sprite.texture != null && image.sprite.texture.isReadable)
            image.alphaHitTestMinimumThreshold = 0.5f;

        RecordHome();
    }

    private void RecordHome()
    {
        if (homeRecorded)
            return;

        homePosition = Rect.anchoredPosition;
        homeRotation = Rect.localEulerAngles.z;
        homeRecorded = true;
    }

    /// <summary>Back on the pile where it was authored, loose and not dragging.</summary>
    public void ResetItem()
    {
        RecordHome();
        StopAllCoroutines();

        Rect.anchoredPosition = homePosition;
        Rect.localEulerAngles = new Vector3(0f, 0f, homeRotation);
        IsPlaced = false;
        IsDragging = false;
        SetArmed(false);
    }

    public void SetArmed(bool value)
    {
        armed = value && !IsPlaced;

        if (!armed)
            IsDragging = false;
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (!armed)
            return;

        StopAllCoroutines();
        IsDragging = true;

        Vector2 finger;
        if (ToParent(e, out finger))
            grabOffset = Rect.anchoredPosition - finger;

        PickedUp?.Invoke(this);
    }

    public void OnDrag(PointerEventData e)
    {
        if (!armed || !IsDragging)
            return;

        Vector2 finger;
        if (ToParent(e, out finger))
            Rect.anchoredPosition = finger + grabOffset;
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (!armed || !IsDragging)
            return;

        IsDragging = false;
        Dropped?.Invoke(this, e);
    }

    /// <summary>
    /// Into its silhouette: slides onto it and turns to match on the way, then stays put.
    /// </summary>
    public IEnumerator SnapIntoPlace(float duration)
    {
        IsPlaced = true;
        SetArmed(false);

        if (targetSlot == null)
            yield break;

        Vector3 fromPosition = Rect.position;
        float fromRotation = Rect.localEulerAngles.z;

        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.Clamp01(t / duration);
            float eased = 1f - Mathf.Pow(1f - k, 3f);

            Rect.position = Vector3.Lerp(fromPosition, targetSlot.position, eased);
            Rect.localEulerAngles = new Vector3(0f, 0f, Mathf.LerpAngle(fromRotation, slotRotation, eased));
            yield return null;
        }

        Rect.position = targetSlot.position;
        Rect.localEulerAngles = new Vector3(0f, 0f, slotRotation);
    }

    /// <summary>
    /// The wrong silhouette: a quick side-to-side shake where it was dropped, then back to
    /// the pile.
    /// </summary>
    public IEnumerator ShakeAndReturn(float shakeDuration, float shakeDistance, float returnDuration)
    {
        SetArmed(false);

        Vector2 origin = Rect.anchoredPosition;

        for (float t = 0f; t < shakeDuration; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.Clamp01(t / shakeDuration);

            // Four swings that die away, so it reads as a "no" rather than a vibration
            float offset = Mathf.Sin(k * Mathf.PI * 8f) * shakeDistance * (1f - k);
            Rect.anchoredPosition = origin + new Vector2(offset, 0f);
            yield return null;
        }

        Rect.anchoredPosition = origin;

        yield return ReturnHome(returnDuration);
    }

    /// <summary>Glides back to its spot on the pile.</summary>
    public IEnumerator ReturnHome(float duration)
    {
        SetArmed(false);

        Vector2 fromPosition = Rect.anchoredPosition;
        float fromRotation = Rect.localEulerAngles.z;

        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.Clamp01(t / duration);
            float eased = k * k * (3f - 2f * k);

            Rect.anchoredPosition = Vector2.Lerp(fromPosition, homePosition, eased);
            Rect.localEulerAngles = new Vector3(0f, 0f, Mathf.LerpAngle(fromRotation, homeRotation, eased));
            yield return null;
        }

        Rect.anchoredPosition = homePosition;
        Rect.localEulerAngles = new Vector3(0f, 0f, homeRotation);
    }

    /// <summary>
    /// Screen point to the parent's space, so the item stays under the finger whatever the
    /// canvas scale or screen size.
    /// </summary>
    private bool ToParent(PointerEventData e, out Vector2 local)
    {
        local = Vector2.zero;
        RectTransform space = Rect.parent as RectTransform;
        if (space == null)
            return false;

        // A Screen Space - Overlay canvas takes a null camera here; anything else needs the
        // one actually rendering it, or the point comes back in the wrong space.
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(space, e.position, cam, out local))
            return false;

        // anchoredPosition is measured from the anchor, not the parent's pivot
        local += Rect.anchoredPosition - (Vector2)Rect.localPosition;
        return true;
    }
}

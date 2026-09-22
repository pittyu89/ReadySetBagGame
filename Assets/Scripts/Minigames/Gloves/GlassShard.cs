using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One piece of broken glass in the gloves minigame.
///
/// It owns its own look and the press — which shard is being picked up, the ring and the
/// hand are <see cref="GlovesMinigame"/>'s business. Where it was placed in the scene is
/// remembered as its home, so every run starts from the authored layout rather than from
/// wherever a missed timing last threw it.
///
/// Presses go through the EventSystem rather than polling Input, so it behaves the same on
/// a phone and in the Editor.
/// </summary>
[RequireComponent(typeof(Image))]
public class GlassShard : MonoBehaviour, IPointerDownHandler
{
    [Tooltip("Gentle breathing while the shard can be picked, so the pieces read as " +
             "things to press rather than as part of the floor. Set 0 to hold them still.")]
    [SerializeField] private float idlePulseAmount = 0.05f;
    [SerializeField] private float idlePulseSpeed = 2.6f;

    /// <summary>Raised once per press, and only while the shard is armed.</summary>
    public event Action<GlassShard> Pressed;

    private RectTransform rectTransform;
    private Image image;

    private Vector2 homePosition;
    private Vector3 homeScale;
    private bool homeRecorded = false;

    private bool isArmed = false;

    // Each shard breathes on its own beat, so the pile shimmers rather than pulsing as one
    private float pulseOffset;

    // Set while the shard is being moved or scaled by a coroutine, so the idle pulse does
    // not fight it for the scale
    private bool isAnimating = false;

    public RectTransform Rect => rectTransform;
    public bool IsCollected { get; private set; }

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        image = GetComponent<Image>();
        pulseOffset = UnityEngine.Random.value * Mathf.PI * 2f;

        // A shard is a small shape in the middle of a big empty square, and the squares
        // overlap. Without this the press goes to whichever square is drawn last rather
        // than to the piece of glass actually under the finger. Needs Read/Write on the
        // texture.
        if (image != null && image.sprite != null && image.sprite.texture != null && image.sprite.texture.isReadable)
            image.alphaHitTestMinimumThreshold = 0.5f;

        RecordHome();
    }

    /// <summary>
    /// The middle of the drawn glass, in the parent's space — not the middle of the sprite's
    /// square, which for most of these sits well off the shard itself. Used to work out
    /// which shard a press in the gap between two of them was meant for.
    /// </summary>
    public Vector2 ContentCenter
    {
        get
        {
            if (rectTransform == null)
                return Vector2.zero;

            Vector2 offset = ContentOffsetFor(image != null ? image.sprite : null);

            // Scaled and turned with the shard, so it still points at the glass after a
            // rotation or the idle pulse
            Vector3 world = rectTransform.TransformVector(new Vector3(offset.x * rectTransform.rect.width,
                                                                      offset.y * rectTransform.rect.height, 0f));
            RectTransform space = rectTransform.parent as RectTransform;
            Vector2 local = space != null ? (Vector2)space.InverseTransformVector(world) : (Vector2)world;

            return rectTransform.anchoredPosition + local;
        }
    }

    // Worked out once per sprite: where the opaque pixels sit inside the square, as a
    // fraction of it from the middle
    private static readonly System.Collections.Generic.Dictionary<Sprite, Vector2> contentOffsets =
        new System.Collections.Generic.Dictionary<Sprite, Vector2>();

    private static Vector2 ContentOffsetFor(Sprite sprite)
    {
        if (sprite == null)
            return Vector2.zero;

        Vector2 cached;
        if (contentOffsets.TryGetValue(sprite, out cached))
            return cached;

        Vector2 offset = Vector2.zero;

        if (sprite.texture != null && sprite.texture.isReadable)
        {
            Rect rect = sprite.textureRect;
            int x0 = Mathf.RoundToInt(rect.x), y0 = Mathf.RoundToInt(rect.y);
            int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
            Color32[] pixels = sprite.texture.GetPixels32();
            int texWidth = sprite.texture.width;

            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (pixels[(y0 + y) * texWidth + x0 + x].a < 128)
                        continue;

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (minX <= maxX)
            {
                offset = new Vector2(((minX + maxX + 1) * 0.5f) / w - 0.5f,
                                     ((minY + maxY + 1) * 0.5f) / h - 0.5f);
            }
        }

        contentOffsets[sprite] = offset;
        return offset;
    }

    private void RecordHome()
    {
        if (homeRecorded)
            return;

        homePosition = rectTransform.anchoredPosition;
        homeScale = rectTransform.localScale;
        homeRecorded = true;
    }

    private void Update()
    {
        if (isAnimating || IsCollected)
            return;

        float pulse = 1f;
        if (isArmed && idlePulseAmount > 0f)
            pulse += Mathf.Sin(Time.unscaledTime * idlePulseSpeed + pulseOffset) * idlePulseAmount;

        rectTransform.localScale = homeScale * pulse;
    }

    /// <summary>
    /// Puts the shard back where it was authored, whole and on the floor.
    /// </summary>
    public void ResetShard()
    {
        if (rectTransform == null)
            Awake();

        StopAllCoroutines();
        isAnimating = false;
        IsCollected = false;

        rectTransform.anchoredPosition = homePosition;
        rectTransform.localScale = homeScale;

        if (image != null)
            image.color = Color.white;

        gameObject.SetActive(true);
        SetArmed(false);
    }

    /// <summary>
    /// Starts or stops accepting presses. Only an armed shard catches the raycast, so a
    /// disarmed one never swallows a press aimed at the tap area behind it.
    /// </summary>
    public void SetArmed(bool armed)
    {
        isArmed = armed && !IsCollected;

        if (image != null)
            image.raycastTarget = isArmed;

        if (!isArmed && !isAnimating && rectTransform != null)
            rectTransform.localScale = homeScale;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!isArmed)
            return;

        Pressed?.Invoke(this);
    }

    /// <summary>
    /// A hand is on its way to it. From here the hand moves it, so it stops breathing and
    /// stops taking presses.
    /// </summary>
    public void BeginCarry()
    {
        SetArmed(false);
        isAnimating = true;
        rectTransform.localScale = homeScale;
    }

    /// <summary>
    /// The hand has closed on it: the shard shrinks into the glove and is gone.
    /// </summary>
    public IEnumerator Collect(float duration)
    {
        IsCollected = true;
        SetArmed(false);
        isAnimating = true;

        Vector3 from = rectTransform.localScale;
        Color start = image != null ? image.color : Color.white;

        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.Clamp01(t / duration);
            rectTransform.localScale = Vector3.Lerp(from, from * 0.2f, k * k);

            if (image != null)
                image.color = new Color(start.r, start.g, start.b, 1f - k);

            yield return null;
        }

        isAnimating = false;
        gameObject.SetActive(false);
    }

    /// <summary>
    /// A missed timing: the shard slips out of reach, fading out where it was and in again
    /// somewhere else, so the player has to find it and try again.
    /// </summary>
    public IEnumerator Relocate(Vector2 newPosition, float duration)
    {
        isAnimating = true;
        SetArmed(false);

        float half = duration * 0.5f;
        Color start = image != null ? image.color : Color.white;

        for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.Clamp01(t / half);
            rectTransform.localScale = homeScale * (1f - 0.5f * k);
            if (image != null)
                image.color = new Color(start.r, start.g, start.b, 1f - k);
            yield return null;
        }

        rectTransform.anchoredPosition = newPosition;

        for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.Clamp01(t / half);
            float eased = 1f - Mathf.Pow(1f - k, 3f);
            rectTransform.localScale = homeScale * (0.5f + 0.5f * eased);
            if (image != null)
                image.color = new Color(start.r, start.g, start.b, k);
            yield return null;
        }

        rectTransform.localScale = homeScale;
        if (image != null)
            image.color = new Color(start.r, start.g, start.b, 1f);

        isAnimating = false;
    }
}

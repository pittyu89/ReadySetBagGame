using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The layer of mud over the contact card, and the finger that rubs it off.
///
/// The smudges are not separate objects. They are stamped into one runtime texture laid over
/// the card at its own pixel size, so a wipe takes off the part of a blob it actually crossed
/// rather than making a whole blob disappear — and blobs that overlap read as one patch of
/// mud, the way they do on the reference sheet.
///
/// The authored Dirt sprites are never touched; they are read once and composited into the
/// working copy, which is rebuilt from scratch at the start of every run. Nothing is written
/// to disk and a second play never opens on a card the last player already cleaned.
///
/// Kept as its own component the way WoundMaskPainter is, except that this one takes the
/// drag itself: the surface being wiped and the thing being pointed at are the same rect
/// here, so there is nothing to be gained by splitting them.
/// </summary>
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(RawImage))]
public class ContactCardSmudges : MonoBehaviour,
    IPointerDownHandler, IBeginDragHandler, IDragHandler, IPointerUpHandler, IEndDragHandler
{
    [Header("Smudges")]
    [Tooltip("The mud blobs stamped over the card. Picked from at random, so two entries " +
             "already give plenty of variety once scale and flipping are on top.")]
    [SerializeField] private Texture2D[] dirtTextures;
    [Tooltip("How many blobs go on the card. Around nine covers it the way the reference " +
             "sheet does without burying it completely.")]
    [SerializeField, Range(1, 40)] private int smudgeCount = 9;
    [Tooltip("How big each blob is stamped, as a fraction of the card's height, picked per " +
             "blob. Given as a fraction rather than a scale factor because the Dirt sprites " +
             "are not all authored at the same size — this way both land at the size the " +
             "card wants, and their pixels come out chunkier than the card's, which is what " +
             "makes the mud read as muck on top rather than part of the print.")]
    [SerializeField] private Vector2 smudgeSizeRange = new Vector2(0.28f, 0.5f);
    [Tooltip("How far the mud stays clear of the card's edge, in the mud layer's own pixels. " +
             "The card art has a border and rounded corners, so a blob drawn right to the " +
             "edge of the rect would hang off the card and float in mid-air.")]
    [SerializeField] private int edgeMargin = 14;

    [Header("Working Copy")]
    [Tooltip("Size of the mud layer in pixels. Matching the card's own art keeps the two on " +
             "the same pixel grid; this is also what gets uploaded on every wipe, so there " +
             "is no reason to go above it.")]
    [SerializeField] private Vector2Int workingResolution = new Vector2Int(400, 272);

    [Header("Brush")]
    [Tooltip("Radius of the finger, as a fraction of the card's width.")]
    [SerializeField, Range(0.01f, 0.3f)] private float brushRadius = 0.075f;
    [Tooltip("0 is a hard-edged rub, 1 fades out across the whole radius. A soft edge keeps " +
             "a stroke from leaving a visible circle rim behind in the mud.")]
    [SerializeField, Range(0f, 1f)] private float brushSoftness = 0.55f;
    [Tooltip("How much a single frame of contact takes off. Below 1 the mud thins out over a " +
             "couple of passes rather than vanishing the instant it is touched.")]
    [SerializeField, Range(0.05f, 1f)] private float brushStrength = 0.6f;

    private RectTransform rect;
    private RawImage image;
    private Canvas canvas;

    private Texture2D workingTexture;
    private Color32[] pixels;

    // Total mud on the card at the start, and what is left of it. Kept as a running sum
    // rather than rescanned, so progress costs nothing per frame.
    private float initialDirt = 0f;
    private float remainingDirt = 0f;

    private bool armed;

    // Where the finger was last frame, in texture pixels. A fast drag is filled in between
    // the two rather than leaving dabs with gaps between them.
    private bool hasLastPoint;
    private Vector2 lastPoint;

    /// <summary>0 when the card is as filthy as it started, 1 once it is wiped clean.</summary>
    public float Cleanliness
    {
        get { return initialDirt <= 0f ? 1f : Mathf.Clamp01(1f - remainingDirt / initialDirt); }
    }

    /// <summary>True while the player has a finger on the card.</summary>
    public bool IsWiping { get; private set; }

    /// <summary>
    /// Unscaled time the finger last moved across the card, so a finger resting still can
    /// be told apart from one rubbing.
    /// </summary>
    public float LastRubTime { get; private set; } = float.NegativeInfinity;

    private RectTransform Self
    {
        get
        {
            if (rect == null)
                rect = (RectTransform)transform;
            return rect;
        }
    }

    private RawImage Image
    {
        get
        {
            if (image == null)
                image = GetComponent<RawImage>();
            return image;
        }
    }

    private void OnDestroy()
    {
        // Created here rather than loaded, so nothing else will free it
        if (workingTexture != null)
            Destroy(workingTexture);
    }

    /// <summary>
    /// Hands over the canvas the card is drawn on, which is needed to turn a touch back into
    /// a point on the mud.
    /// </summary>
    public void Configure(Canvas owningCanvas)
    {
        canvas = owningCanvas;
    }

    /// <summary>Input is ignored entirely until the minigame arms it.</summary>
    public void SetArmed(bool value)
    {
        armed = value;

        if (!armed)
        {
            IsWiping = false;
            hasLastPoint = false;
        }
    }

    /// <summary>
    /// Throws the mud back on, in a fresh arrangement. Call before each run.
    /// </summary>
    public void Rebuild()
    {
        EnsureTexture();

        if (pixels == null)
            return;

        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color32(0, 0, 0, 0);

        if (dirtTextures != null && dirtTextures.Length > 0)
        {
            List<Vector2> centres = ScatterCentres(smudgeCount);

            for (int i = 0; i < centres.Count; i++)
                StampSmudge(dirtTextures[Random.Range(0, dirtTextures.Length)], centres[i]);
        }
        else
        {
            Debug.LogWarning("[ContactCardSmudges] No dirt textures assigned — the card is " +
                             "already clean.", this);
        }

        initialDirt = 0f;
        for (int i = 0; i < pixels.Length; i++)
            initialDirt += pixels[i].a / 255f;

        remainingDirt = initialDirt;

        SetLayerAlpha(1f);
        Upload();
    }

    /// <summary>
    /// Fades what is left of the mud, whole. The last few flecks are not worth hunting for,
    /// so once the card is clean enough to count the rest is simply taken off.
    /// </summary>
    public void SetLayerAlpha(float alpha)
    {
        Color c = Image.color;
        c.a = Mathf.Clamp01(alpha);
        Image.color = c;
    }

    // ------------------------------------------------------------------ input

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!armed)
            return;

        IsWiping = true;

        // A tap is a wipe too, so a blob under a stationary finger still comes off
        Vector2 point;
        hasLastPoint = TryTexturePoint(eventData, out point);
        if (hasLastPoint)
        {
            WipeAt(point);
            lastPoint = point;
            Upload();
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (armed)
            IsWiping = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!armed || !IsWiping)
            return;

        Vector2 point;
        if (!TryTexturePoint(eventData, out point))
            return;

        if (!hasLastPoint)
        {
            WipeAt(point);
        }
        else
        {
            // Filled in along the way, or a quick swipe would leave untouched mud between
            // one frame's dab and the next
            float radius = brushRadius * workingTexture.width;
            float distance = Vector2.Distance(lastPoint, point);
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance / Mathf.Max(1f, radius * 0.5f)));

            for (int s = 1; s <= steps; s++)
                WipeAt(Vector2.Lerp(lastPoint, point, s / (float)steps));
        }

        lastPoint = point;
        hasLastPoint = true;
        LastRubTime = Time.unscaledTime;

        Upload();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        IsWiping = false;
        hasLastPoint = false;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        IsWiping = false;
        hasLastPoint = false;
    }

    /// <summary>
    /// Where the finger is on the mud layer, in its own pixels. False when the touch is off
    /// the card entirely.
    /// </summary>
    private bool TryTexturePoint(PointerEventData eventData, out Vector2 result)
    {
        result = Vector2.zero;

        if (workingTexture == null)
            return false;

        // A Screen Space - Overlay canvas takes a null camera here; anything else needs the
        // one actually rendering it, or the point comes back in the wrong space.
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                Self, eventData.position, cam, out local))
            return false;

        Rect r = Self.rect;
        result = new Vector2(
            Mathf.InverseLerp(r.xMin, r.xMax, local.x) * workingTexture.width,
            Mathf.InverseLerp(r.yMin, r.yMax, local.y) * workingTexture.height);

        return true;
    }

    // ------------------------------------------------------------------ the mud

    /// <summary>
    /// Rubs at one spot, taking alpha off the mud so a half-cleaned blob thins out rather
    /// than dropping away in pieces. Framerate independent, so a swipe takes off the same
    /// amount on a slow phone.
    /// </summary>
    private void WipeAt(Vector2 centre)
    {
        int w = workingTexture.width;
        int h = workingTexture.height;
        float radius = brushRadius * w;

        int minX = Mathf.Max(0, Mathf.FloorToInt(centre.x - radius));
        int maxX = Mathf.Min(w - 1, Mathf.CeilToInt(centre.x + radius));
        int minY = Mathf.Max(0, Mathf.FloorToInt(centre.y - radius));
        int maxY = Mathf.Min(h - 1, Mathf.CeilToInt(centre.y + radius));

        if (minX > maxX || minY > maxY)
            return;

        float strength = Mathf.Clamp01(brushStrength * Time.unscaledDeltaTime * 60f);
        float inner = radius * (1f - brushSoftness);
        float removed = 0f;

        for (int y = minY; y <= maxY; y++)
        {
            int row = y * w;

            for (int x = minX; x <= maxX; x++)
            {
                float dx = x - centre.x;
                float dy = y - centre.y;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                if (distance > radius)
                    continue;

                int index = row + x;
                byte before = pixels[index].a;
                if (before == 0)
                    continue;

                // 1 at the middle of the dab, easing to 0 at its rim
                float falloff = radius <= inner
                    ? 1f
                    : Mathf.Clamp01(1f - (distance - inner) / (radius - inner));

                byte after = (byte)Mathf.Max(0f, before - 255f * strength * falloff);
                if (after >= before)
                    continue;

                removed += (before - after) / 255f;
                pixels[index].a = after;
            }
        }

        if (removed <= 0f)
            return;

        remainingDirt = Mathf.Max(0f, remainingDirt - removed);
    }

    /// <summary>
    /// Picks where the blobs go: one per cell of a loose grid over the card, jittered inside
    /// its own cell.
    ///
    /// Straight random positions were the obvious thing and the wrong one — they clump, and
    /// a run that drops six of nine blobs in one corner leaves half the card already clean
    /// and the other half a solid wall of mud. A jittered grid keeps the mess spread over
    /// the whole card while still looking thrown on rather than laid out.
    /// </summary>
    private List<Vector2> ScatterCentres(int count)
    {
        var centres = new List<Vector2>(count);

        if (count <= 0)
            return centres;

        int w = workingTexture.width;
        int h = workingTexture.height;

        // Cells as close to square as the card allows, so the jitter is even in both axes
        float aspect = w / (float)h;
        int cols = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(count * aspect)));
        int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)cols));

        var cells = new List<int>(cols * rows);
        for (int i = 0; i < cols * rows; i++)
            cells.Add(i);

        // Shuffled and trimmed, so a count that does not fill the grid leaves its gaps
        // scattered rather than always along the last row
        for (int i = cells.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int swap = cells[i]; cells[i] = cells[j]; cells[j] = swap;
        }

        float cellW = w / (float)cols;
        float cellH = h / (float)rows;

        for (int i = 0; i < count && i < cells.Count; i++)
        {
            int col = cells[i] % cols;
            int row = cells[i] / cols;

            centres.Add(new Vector2(
                Random.Range(col * cellW, (col + 1) * cellW),
                Random.Range(row * cellH, (row + 1) * cellH)));
        }

        return centres;
    }

    /// <summary>
    /// Drops one blob on the card, at its own size or larger and possibly mirrored. Nearest
    /// sampled on purpose: the mud is pixel art and should stay so when it is blown up,
    /// rather than going soft against a crisp card.
    /// </summary>
    private void StampSmudge(Texture2D source, Vector2 centre)
    {
        if (source == null)
            return;

        Color32[] src;
        try
        {
            src = source.GetPixels32();
        }
        catch (UnityException)
        {
            // The only way this throws is a texture without Read/Write enabled, which is a
            // setup mistake rather than something to handle at runtime
            Debug.LogWarning("[ContactCardSmudges] '" + source.name + "' needs Read/Write " +
                             "enabled in its importer to be stamped.", this);
            return;
        }

        int w = workingTexture.width;
        int h = workingTexture.height;

        float fraction = Random.Range(Mathf.Min(smudgeSizeRange.x, smudgeSizeRange.y),
                                      Mathf.Max(smudgeSizeRange.x, smudgeSizeRange.y));
        int drawH = Mathf.Max(1, Mathf.RoundToInt(h * fraction));
        int drawW = Mathf.Max(1, Mathf.RoundToInt(drawH * (source.width / (float)source.height)));

        bool flipX = Random.value < 0.5f;
        bool flipY = Random.value < 0.5f;

        // The whole blob is pulled inside the card, not just its middle. A blob drawn over
        // the border would sit on the transparent margin of the card art and read as mud
        // hanging in mid-air beside the card.
        int margin = Mathf.Max(0, edgeMargin);
        int originX = Mathf.RoundToInt(centre.x) - drawW / 2;
        int originY = Mathf.RoundToInt(centre.y) - drawH / 2;

        originX = drawW >= w - margin * 2
            ? (w - drawW) / 2
            : Mathf.Clamp(originX, margin, w - margin - drawW);

        originY = drawH >= h - margin * 2
            ? (h - drawH) / 2
            : Mathf.Clamp(originY, margin, h - margin - drawH);

        for (int y = 0; y < drawH; y++)
        {
            int destY = originY + y;
            if (destY < 0 || destY >= h)
                continue;

            int sy = Mathf.Min(source.height - 1, y * source.height / drawH);
            if (flipY)
                sy = source.height - 1 - sy;

            int srcRow = sy * source.width;
            int destRow = destY * w;

            for (int x = 0; x < drawW; x++)
            {
                int destX = originX + x;
                if (destX < 0 || destX >= w)
                    continue;

                int sx = Mathf.Min(source.width - 1, x * source.width / drawW);
                if (flipX)
                    sx = source.width - 1 - sx;

                Color32 s = src[srcRow + sx];
                if (s.a == 0)
                    continue;

                // Straight over the top. Blobs are opaque in the middle, so overlaps read as
                // one patch of mud rather than as one blob showing through another.
                Color32 d = pixels[destRow + destX];
                if (s.a == 255 || d.a == 0)
                {
                    pixels[destRow + destX] = s;
                    continue;
                }

                float sa = s.a / 255f;
                pixels[destRow + destX] = new Color32(
                    (byte)Mathf.RoundToInt(s.r * sa + d.r * (1f - sa)),
                    (byte)Mathf.RoundToInt(s.g * sa + d.g * (1f - sa)),
                    (byte)Mathf.RoundToInt(s.b * sa + d.b * (1f - sa)),
                    (byte)Mathf.Max(s.a, d.a));
            }
        }
    }

    private void EnsureTexture()
    {
        int w = Mathf.Max(32, workingResolution.x);
        int h = Mathf.Max(32, workingResolution.y);

        if (workingTexture != null && workingTexture.width == w && workingTexture.height == h)
            return;

        if (workingTexture != null)
            Destroy(workingTexture);

        workingTexture = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            name = "CardSmudges (runtime)",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point
        };

        pixels = new Color32[w * h];
        Image.texture = workingTexture;
    }

    private void Upload()
    {
        if (workingTexture == null)
            return;

        // Apply uploads the whole texture whatever was touched, which is why the working
        // copy is kept at the card's own size rather than anything larger
        workingTexture.SetPixels32(pixels);
        workingTexture.Apply(false);
    }
}

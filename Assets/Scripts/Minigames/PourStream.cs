using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The falling water between the bottle's spout and the surface in the glass.
///
/// It is one RawImage stretched from the spout down to the water line, with its UV rect
/// scrolled downwards to sell the flow. A RawImage rather than an Image because only
/// RawImage exposes uvRect — scrolling an Image would mean instancing a material per
/// stream to animate its texture offset.
///
/// The stream re-measures every frame while pouring: the spout moves as the bottle tips,
/// and the water line climbs as the glass fills, so both ends are live.
/// </summary>
public class PourStream : MonoBehaviour
{
    [Header("Stream")]
    [SerializeField] private RawImage stream;
    [Tooltip("Width of the falling water, in canvas units.")]
    [SerializeField] private float streamWidth = 10f;
    [Tooltip("Height of one repeat of the stream texture. Smaller values flow faster for " +
             "the same scroll speed.")]
    [SerializeField] private float tileHeight = 32f;
    [SerializeField] private float scrollSpeed = 3.2f;

    [Header("Splash")]
    [Tooltip("Optional. Sits where the water lands and pulses while pouring.")]
    [SerializeField] private RectTransform splash;
    [SerializeField] private float splashPulseScale = 0.18f;
    [SerializeField] private float splashPulseSpeed = 14f;

    [Header("Look")]
    [Tooltip("Used only when no texture is assigned to the RawImage — a plain stream is " +
             "generated so the minigame works without extra art.")]
    [SerializeField] private Color waterColor = new Color(0.42f, 0.72f, 0.95f, 1f);
    [SerializeField] private Color waterHighlight = new Color(0.78f, 0.93f, 1f, 1f);

    private RectTransform streamRect;
    private RectTransform parentRect;

    // Generated stand-in for a missing stream texture, owned by this component
    private Texture2D generatedTexture;

    private float scrollOffset;
    private bool isPouring;

    private void Awake()
    {
        if (stream == null)
            stream = GetComponentInChildren<RawImage>(true);

        if (stream != null)
        {
            streamRect = stream.rectTransform;
            parentRect = streamRect.parent as RectTransform;

            // Top-centre pivot: the stream hangs from the spout, so its length grows
            // downwards from a fixed top edge.
            streamRect.pivot = new Vector2(0.5f, 1f);
            streamRect.anchorMin = new Vector2(0.5f, 0.5f);
            streamRect.anchorMax = new Vector2(0.5f, 0.5f);

            if (stream.texture == null)
                stream.texture = BuildStreamTexture();

            stream.raycastTarget = false;
        }

        Stop();
    }

    private void OnDestroy()
    {
        if (generatedTexture != null)
            Destroy(generatedTexture);
    }

    /// <summary>
    /// Shows the stream and starts it flowing.
    /// </summary>
    public void Begin()
    {
        isPouring = true;

        if (stream != null)
            stream.enabled = true;

        if (splash != null)
            splash.gameObject.SetActive(true);
    }

    /// <summary>
    /// Hides the stream. Safe to call when it is already stopped.
    /// </summary>
    public void Stop()
    {
        isPouring = false;

        if (stream != null)
            stream.enabled = false;

        if (splash != null)
            splash.gameObject.SetActive(false);
    }

    /// <summary>
    /// Points the stream from <paramref name="spout"/> down to the water line in
    /// <paramref name="cup"/>. Call every frame while pouring — both ends move.
    /// </summary>
    public void UpdateStream(RectTransform spout, WaterCup cup)
    {
        if (!isPouring || stream == null || streamRect == null || spout == null || cup == null)
            return;

        Vector2 top = ToLocal(spout.position);
        Vector2 bottom = ToLocal(SurfaceWorldPosition(cup));

        // Fall straight down from the spout. Slanting the stream to meet a cup that is
        // off to one side would look wrong — falling water does not travel sideways.
        streamRect.anchoredPosition = top;

        float length = Mathf.Max(0f, top.y - bottom.y);
        streamRect.sizeDelta = new Vector2(streamWidth, length);

        scrollOffset += Time.unscaledDeltaTime * scrollSpeed;
        stream.uvRect = new Rect(0f, -scrollOffset, 1f, Mathf.Max(0.01f, length / tileHeight));

        if (splash == null)
            return;

        splash.anchoredPosition = new Vector2(top.x, bottom.y);
        float pulse = 1f + Mathf.Sin(Time.unscaledTime * splashPulseSpeed) * splashPulseScale;
        splash.localScale = new Vector3(pulse, pulse, 1f);
    }

    /// <summary>
    /// World position of the water line inside the glass, which is where the stream
    /// should stop rather than at the bottom of the cup.
    /// </summary>
    private Vector3 SurfaceWorldPosition(WaterCup cup)
    {
        RectTransform liquid = cup.GetLiquidRect();

        Vector3[] corners = new Vector3[4];
        liquid.GetWorldCorners(corners);

        // corners: 0 bottom-left, 1 top-left, 2 top-right, 3 bottom-right
        Vector3 bottomCentre = (corners[0] + corners[3]) * 0.5f;
        Vector3 topCentre = (corners[1] + corners[2]) * 0.5f;

        return Vector3.Lerp(bottomCentre, topCentre, cup.GetSurfaceHeightNormalized());
    }

    private Vector2 ToLocal(Vector3 worldPosition)
    {
        if (parentRect == null)
            return worldPosition;

        // Overlay canvases have no camera, and RectTransformUtility wants null in that case
        Canvas canvas = parentRect.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;

        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(cam, worldPosition);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPoint, cam, out Vector2 local);
        return local;
    }

    /// <summary>
    /// Builds a small repeating stream so the minigame has water to show without a
    /// dedicated texture asset. Rows vary in brightness to give the scroll something
    /// to carry; columns shade the edges so the stream reads as round.
    /// </summary>
    private Texture2D BuildStreamTexture()
    {
        const int width = 8;
        const int height = 32;

        generatedTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Point
        };

        for (int y = 0; y < height; y++)
        {
            // Bands of slightly brighter water travelling down the stream
            float band = Mathf.PerlinNoise(0f, y * 0.35f);

            for (int x = 0; x < width; x++)
            {
                // Darker at the edges, with a highlight just left of centre
                float edge = 1f - Mathf.Abs((x + 0.5f) / width - 0.5f) * 2f;
                float shade = Mathf.SmoothStep(0.55f, 1f, edge);

                bool highlight = x == width / 2 - 1 && band > 0.45f;

                Color c = highlight ? waterHighlight : waterColor * shade;
                c.a = edge > 0.05f ? 1f : 0f;

                generatedTexture.SetPixel(x, y, c);
            }
        }

        generatedTexture.Apply();
        return generatedTexture;
    }
}

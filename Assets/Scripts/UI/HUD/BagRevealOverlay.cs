using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The full-screen "You got a Standard Bag" reveal shown while the character holds the go-bag
/// overhead: a dark screen, warm light rays and sparkles behind the pickup pose drawn large,
/// with the bag's name above it in the bag's own colour. It stays up until the player taps.
///
/// Built entirely in code and owned by <see cref="BagPickupPose"/>, which ends the pose once
/// <see cref="IsDone"/> and destroys it. Runs on unscaled time because the game is paused meanwhile.
/// </summary>
public class BagRevealOverlay : MonoBehaviour
{
    private static readonly Color BackdropColor = new Color(0.02f, 0.02f, 0.02f, 1f);
    private static readonly Color RayColor = new Color(1f, 0.93f, 0.8f, 1f);
    private static readonly Color HaloColor = new Color(0.66f, 0.48f, 0.3f, 0.8f);
    private static readonly Color CoreColor = new Color(1f, 0.95f, 0.85f, 1f);
    private const string HintText = "Tap to continue";

    // Sizes in canvas units at the 1280x720 reference resolution
    private const float FigureHeight = 330f;
    private const float FigureOffsetY = -40f;
    private const float TitleGap = 14f;
    private const float TitleSize = 46f;
    private const float HintSize = 30f;
    private const float HintGap = 18f;
    private const float RaysSize = 780f;
    private const float HaloSize = 700f;
    private const float CoreSize = 360f;
    private const int SparkleCount = 16;

    private const float FadeIn = 0.18f;
    private const float FadeOut = 0.25f;
    private const float PopDuration = 0.4f;
    // A tap this early is ignored, so the tap that walked the character onto the bag
    // can't dismiss the reveal before it's been seen
    private const float MinShowTime = 0.7f;

    private static Sprite raysSprite;
    private static Sprite glowSprite;

    private CanvasGroup group;
    private RectTransform raysA, raysB, figure, title, core;
    private Image coreImage;
    private TextMeshProUGUI hint;
    private Sparkle[] sparkles;
    private float elapsed;
    private float titleRestY;
    private bool dismissing;
    private float dismissedAt;

    /// <summary>True once the player has tapped and the reveal has faded out.</summary>
    public bool IsDone { get; private set; }

    private struct Sparkle
    {
        public RectTransform rect;
        public Image image;
        public Vector2 direction;
        public float startRadius, speed, phase, twinkle;
    }

    /// <summary>Builds the generated textures ahead of time so the pickup doesn't hitch on them.</summary>
    public static void Prewarm()
    {
        if (raysSprite == null)
            raysSprite = MakeSprite(BuildRaysTexture(512), "BagRevealRays");
        if (glowSprite == null)
            glowSprite = MakeSprite(BuildGlowTexture(128), "BagRevealGlow");
    }

    /// <summary>
    /// Shows the reveal until the player taps. <paramref name="bagRect"/> is where the bag's
    /// visible pixels sit, in pose-sprite pixels measured from the pose's pivot.
    /// </summary>
    public static BagRevealOverlay Show(Sprite poseSprite, Sprite bagSprite, Rect bagRect, string bagName, Color bagColor)
    {
        Prewarm();

        GameObject go = new GameObject("BagRevealOverlay", typeof(RectTransform));
        BagRevealOverlay overlay = go.AddComponent<BagRevealOverlay>();
        overlay.Build(poseSprite, bagSprite, bagRect, bagName, bagColor);
        return overlay;
    }

    private void Build(Sprite poseSprite, Sprite bagSprite, Rect bagRect, string bagName, Color bagColor)
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the HUD and the onboarding overlay, below the loading screen and menu transition
        canvas.sortingOrder = 500;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        gameObject.AddComponent<GraphicRaycaster>();
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        RectTransform root = (RectTransform)transform;

        Image backdrop = MakeImage("Backdrop", root, null, BackdropColor);
        backdrop.raycastTarget = true;
        RectTransform backdropRect = backdrop.rectTransform;
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.sizeDelta = Vector2.zero;

        // Light comes from the middle of the figure
        Vector2 centre = new Vector2(0f, FigureOffsetY + FigureHeight * 0.1f);

        MakeCentred("Halo", root, glowSprite, HaloColor, HaloSize, centre);
        raysB = MakeCentred("RaysBack", root, raysSprite, new Color(RayColor.r, RayColor.g, RayColor.b, 0.45f), RaysSize * 0.82f, centre);
        raysB.localEulerAngles = new Vector3(0f, 0f, 5f);
        raysA = MakeCentred("Rays", root, raysSprite, RayColor, RaysSize, centre);
        core = MakeCentred("Core", root, glowSprite, CoreColor, CoreSize, centre);
        coreImage = core.GetComponent<Image>();

        BuildSparkles(root, centre);
        float figureTop = BuildFigure(root, poseSprite, bagSprite, bagRect);
        BuildTitle(root, bagName, bagColor, figureTop);
        BuildHint(root, FigureOffsetY - figure.sizeDelta.y * 0.5f);
    }

    /// <summary>The pose with the bag overhead, scaled up as one piece. Returns the top of it in canvas units.</summary>
    private float BuildFigure(RectTransform root, Sprite pose, Sprite bag, Rect bagRect)
    {
        // Everything is laid out in pose-sprite pixels with the origin at the pose's pivot
        Rect poseRect = new Rect(-pose.pivot.x, -pose.pivot.y, pose.rect.width, pose.rect.height);
        Rect bounds = poseRect;
        bool hasBag = bag != null && bagRect.width > 0f && bagRect.height > 0f;
        if (hasBag)
            bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, bagRect.xMin), Mathf.Min(bounds.yMin, bagRect.yMin),
                                     Mathf.Max(bounds.xMax, bagRect.xMax), Mathf.Max(bounds.yMax, bagRect.yMax));

        float scale = FigureHeight / bounds.height;

        GameObject go = new GameObject("Figure", typeof(RectTransform));
        figure = (RectTransform)go.transform;
        figure.SetParent(root, false);
        figure.anchorMin = figure.anchorMax = new Vector2(0.5f, 0.5f);
        figure.pivot = new Vector2(0.5f, 0.5f);
        figure.sizeDelta = bounds.size * scale;
        figure.anchoredPosition = new Vector2(0f, FigureOffsetY);

        PlaceInFigure("Pose", pose, poseRect, bounds, scale);

        if (hasBag)
        {
            // The bag sheets pad each frame, so map the visible pixels onto bagRect and let
            // the padding fall around them
            Vector2 min, max;
            GoBagPickup.VisibleBounds(bag, out min, out max);
            float unitsToPixels = bagRect.width / Mathf.Max(1e-5f, max.x - min.x);
            float bagPpu = bag.pixelsPerUnit;
            Rect full = new Rect(bagRect.x + (-bag.pivot.x / bagPpu - min.x) * unitsToPixels,
                                 bagRect.y + (-bag.pivot.y / bagPpu - min.y) * unitsToPixels,
                                 bag.rect.width / bagPpu * unitsToPixels,
                                 bag.rect.height / bagPpu * unitsToPixels);
            PlaceInFigure("Bag", bag, full, bounds, scale);
        }

        return FigureOffsetY + figure.sizeDelta.y * 0.5f;
    }

    private void PlaceInFigure(string name, Sprite sprite, Rect pixels, Rect bounds, float scale)
    {
        Image image = MakeImage(name, figure, sprite, Color.white);
        RectTransform rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = (pixels.position - bounds.position) * scale;
        rect.sizeDelta = pixels.size * scale;
    }

    private void BuildTitle(RectTransform root, string bagName, Color bagColor, float figureTop)
    {
        GameObject go = new GameObject("Title", typeof(RectTransform));
        title = (RectTransform)go.transform;
        title.SetParent(root, false);
        title.anchorMin = title.anchorMax = new Vector2(0.5f, 0.5f);
        title.pivot = new Vector2(0.5f, 0f);
        title.sizeDelta = new Vector2(1100f, TitleSize * 1.4f);
        titleRestY = figureTop + TitleGap;
        title.anchoredPosition = new Vector2(0f, titleRestY);

        TextMeshProUGUI text = MakeText(go, TitleSize, TextAlignmentOptions.Bottom);
        text.text = "You got a <color=#" + ColorUtility.ToHtmlStringRGB(bagColor) + ">" + bagName + "</color>";
    }

    private void BuildHint(RectTransform root, float figureBottom)
    {
        GameObject go = new GameObject("Hint", typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(root, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(800f, HintSize * 1.4f);
        rect.anchoredPosition = new Vector2(0f, figureBottom - HintGap);

        hint = MakeText(go, HintSize, TextAlignmentOptions.Top);
        hint.text = HintText;
        hint.alpha = 0f;
    }

    private static TextMeshProUGUI MakeText(GameObject go, float size, TextAlignmentOptions alignment)
    {
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset font = Resources.Load<TMP_FontAsset>("Fonts/Jersey25-Regular SDF");
        if (font != null)
            text.font = font;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    private void BuildSparkles(RectTransform root, Vector2 centre)
    {
        sparkles = new Sparkle[SparkleCount];
        for (int i = 0; i < SparkleCount; i++)
        {
            float angle = (i + Random.value * 0.7f) / SparkleCount * Mathf.PI * 2f;
            float size = Random.Range(5f, 11f);
            Sparkle s = new Sparkle();
            s.rect = MakeCentred("Sparkle", root, glowSprite, Color.white, size, centre);
            s.image = s.rect.GetComponent<Image>();
            s.direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            s.startRadius = Random.Range(70f, 260f);
            s.speed = Random.Range(12f, 30f);
            s.phase = Random.value * Mathf.PI * 2f;
            s.twinkle = Random.Range(3f, 6f);
            s.rect.anchoredPosition = centre + s.direction * s.startRadius;
            sparkles[i] = s;
        }
    }

    void Update()
    {
        elapsed += Time.unscaledDeltaTime;
        float t = elapsed;

        // Touches arrive as mouse clicks too, so this covers taps, clicks and keys
        if (!dismissing && t >= MinShowTime && Input.anyKeyDown)
        {
            dismissing = true;
            dismissedAt = t;
        }

        float fade = Mathf.Clamp01(t / FadeIn);
        if (dismissing)
        {
            fade = Mathf.Min(fade, 1f - Mathf.Clamp01((t - dismissedAt) / FadeOut));
            if (fade <= 0f)
                IsDone = true;
        }
        group.alpha = fade;

        // The hint fades in once a tap will count, then pulses gently
        float hintIn = Mathf.Clamp01((t - MinShowTime) / 0.3f);
        hint.alpha = hintIn * (0.55f + 0.35f * Mathf.Sin((t - MinShowTime) * 3f));

        float pop = EaseOutBack(Mathf.Clamp01(t / PopDuration));

        float raysScale = Mathf.LerpUnclamped(0.55f, 1f, pop);
        raysA.localScale = Vector3.one * raysScale;
        raysA.localEulerAngles = new Vector3(0f, 0f, -t * 9f);
        raysB.localScale = Vector3.one * raysScale;
        raysB.localEulerAngles = new Vector3(0f, 0f, 5f + t * 6f);

        coreImage.color = new Color(CoreColor.r, CoreColor.g, CoreColor.b, CoreColor.a * (0.88f + 0.12f * Mathf.Sin(t * 4f)));

        figure.localScale = Vector3.one * Mathf.LerpUnclamped(0.45f, 1f, pop);

        float titleIn = EaseOutCubic(Mathf.Clamp01((t - 0.12f) / 0.3f));
        title.anchoredPosition = new Vector2(0f, titleRestY - 14f * (1f - titleIn));
        title.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, titleIn);

        for (int i = 0; i < sparkles.Length; i++)
        {
            Sparkle s = sparkles[i];
            Vector2 centre = new Vector2(0f, FigureOffsetY + FigureHeight * 0.1f);
            s.rect.anchoredPosition = centre + s.direction * (s.startRadius + s.speed * t);
            float a = 0.5f + 0.5f * Mathf.Sin(s.phase + t * s.twinkle);
            s.image.color = new Color(1f, 0.97f, 0.9f, a * a);
        }
    }

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    private static float EaseOutCubic(float x)
    {
        return 1f - Mathf.Pow(1f - x, 3f);
    }

    private static Image MakeImage(string name, RectTransform parent, Sprite sprite, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static RectTransform MakeCentred(string name, RectTransform parent, Sprite sprite, Color color, float size, Vector2 position)
    {
        RectTransform rect = MakeImage(name, parent, sprite, color).rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(size, size);
        rect.anchoredPosition = position;
        return rect;
    }

    private static Sprite MakeSprite(Texture2D texture, string name)
    {
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        sprite.name = name;
        return sprite;
    }

    /// <summary>A soft round glow, white with the falloff in alpha.</summary>
    private static Texture2D BuildGlowTexture(int size)
    {
        Color32[] pixels = new Color32[size * size];
        float c = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - c) / c, dy = (y + 0.5f - c) / c;
                float r = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                float a = (1f - r) * (1f - r);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }

        return FinishTexture(pixels, size, "BagRevealGlow");
    }

    /// <summary>
    /// Light streaks fanning out from the centre: thick at the middle and tapering to a point,
    /// each with its own length and brightness so the burst looks uneven like the reference.
    /// </summary>
    private static Texture2D BuildRaysTexture(int size)
    {
        const int rayCount = 56;
        System.Random rng = new System.Random(11);
        float[] angle = new float[rayCount], length = new float[rayCount], width = new float[rayCount], bright = new float[rayCount];
        for (int i = 0; i < rayCount; i++)
        {
            angle[i] = (i + (float)rng.NextDouble() * 0.8f) / rayCount * Mathf.PI * 2f;
            bool longRay = rng.NextDouble() < 0.45;
            length[i] = longRay ? 0.75f + (float)rng.NextDouble() * 0.25f : 0.35f + (float)rng.NextDouble() * 0.35f;
            width[i] = (longRay ? 7f : 4f) + (float)rng.NextDouble() * 6f;
            bright[i] = 0.45f + (float)rng.NextDouble() * 0.55f;
        }

        Color32[] pixels = new Color32[size * size];
        float c = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float rn = r / c;
                if (rn >= 1f)
                    continue;

                float theta = Mathf.Atan2(dy, dx);
                float a = 0f;
                for (int i = 0; i < rayCount; i++)
                {
                    if (rn >= length[i])
                        continue;

                    float delta = Mathf.Abs(Mathf.DeltaAngle(theta * Mathf.Rad2Deg, angle[i] * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                    if (delta > 1.2f)
                        continue;

                    float along = 1f - rn / length[i];
                    float halfWidth = width[i] * along + 0.6f;
                    float across = Mathf.Clamp01(1f - r * Mathf.Sin(delta) / halfWidth);
                    a += across * across * bright[i] * along;
                }

                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
        }

        return FinishTexture(pixels, size, "BagRevealRays");
    }

    private static Texture2D FinishTexture(Color32[] pixels, int size, string name)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = name;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }
}

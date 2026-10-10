using System;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// The "You got a Standard Bag" reveal played while the character holds the go-bag overhead.
/// It happens in the room itself: <see cref="CameraOrbitController"/> swings round to a level,
/// head-on view of the character, warm light rays, a glow and sparkles burst out behind them,
/// and the bag's name sits above them in the bag's own colour. It stays up until the player taps.
///
/// The burst is a set of world-space sprites facing the camera, drawn over the room but under
/// the character. The words are screen-space UI that follows the character on screen.
///
/// Built entirely in code and owned by <see cref="BagPickupPose"/>, which ends the pose once
/// <see cref="IsDone"/> and destroys it. Runs on unscaled time because the game is paused meanwhile.
/// </summary>
public class BagRevealOverlay : MonoBehaviour
{
    private static readonly Color RayColor = new Color(1f, 0.93f, 0.8f, 1f);
    private static readonly Color HaloColor = new Color(0.66f, 0.48f, 0.3f, 0.8f);
    private static readonly Color CoreColor = new Color(1f, 0.95f, 0.85f, 1f);
    private const string HintText = "Tap to continue";

    // Burst sizes as multiples of the figure's height (character plus bag)
    private const float RaysSize = 2.4f;
    private const float HaloSize = 2.1f;
    private const float CoreSize = 1.1f;
    private const int SparkleCount = 16;
    // How far behind the character the burst sits, as a share of the figure's height
    private const float BurstDepth = 0.05f;

    // Text sizes in canvas units at the 1280x720 reference resolution
    private const float TitleGap = 14f;
    private const float TitleSize = 46f;
    private const float HintSize = 30f;
    private const float HintGap = 18f;

    private const float FadeIn = 0.18f;
    private const float FadeOut = 0.25f;
    private const float PopDuration = 0.4f;
    // A tap this early is ignored, so the tap that walked the character onto the bag
    // can't dismiss the reveal before it's been seen
    private const float MinShowTime = 0.7f;

    private static Sprite raysSprite;
    private static Sprite glowSprite;

    private Func<Bounds> getFigure;
    private RectTransform root;
    private CanvasGroup group;
    private RectTransform title, hintRect;
    private TextMeshProUGUI hint;
    private float titleRise;

    private GameObject burst;
    private Material burstMaterial;
    private SpriteRenderer raysA, raysB, halo, core;
    private Sparkle[] sparkles;

    private float elapsed;
    private bool dismissing;
    private float dismissedAt;

    /// <summary>True once the player has tapped and the reveal has faded out.</summary>
    public bool IsDone { get; private set; }

    private struct Sparkle
    {
        public SpriteRenderer renderer;
        public Vector2 direction;
        public float startRadius, speed, phase, twinkle, size;
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
    /// Shows the reveal until the player taps. <paramref name="getFigure"/> gives the world
    /// bounds of the character and the bag they hold, each frame; <paramref name="character"/>
    /// is the character's sprite, which the burst is sorted beneath.
    /// </summary>
    public static BagRevealOverlay Show(Func<Bounds> getFigure, SpriteRenderer character, string bagName, Color bagColor)
    {
        Prewarm();

        GameObject go = new GameObject("BagRevealOverlay", typeof(RectTransform));
        BagRevealOverlay overlay = go.AddComponent<BagRevealOverlay>();
        overlay.getFigure = getFigure;
        overlay.BuildCanvas(bagName, bagColor);
        overlay.BuildBurst(character);
        return overlay;
    }

    private void BuildCanvas(string bagName, Color bagColor)
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

        root = (RectTransform)transform;

        // Invisible, so the game shows through, but it still catches the dismissing tap
        // before it can reach the HUD underneath
        GameObject blockerObject = new GameObject("InputBlocker", typeof(RectTransform));
        RectTransform blocker = (RectTransform)blockerObject.transform;
        blocker.SetParent(root, false);
        blocker.anchorMin = Vector2.zero;
        blocker.anchorMax = Vector2.one;
        blocker.sizeDelta = Vector2.zero;
        Image blockerImage = blockerObject.AddComponent<Image>();
        blockerImage.color = Color.clear;

        title = MakeTextRect("Title", new Vector2(0.5f, 0f), new Vector2(1100f, TitleSize * 1.4f));
        TextMeshProUGUI titleText = MakeText(title.gameObject, TitleSize, TextAlignmentOptions.Bottom);
        titleText.text = "You got a <color=#" + ColorUtility.ToHtmlStringRGB(bagColor) + ">" + bagName + "</color>";

        hintRect = MakeTextRect("Hint", new Vector2(0.5f, 1f), new Vector2(800f, HintSize * 1.4f));
        hint = MakeText(hintRect.gameObject, HintSize, TextAlignmentOptions.Top);
        hint.text = HintText;
        hint.alpha = 0f;
    }

    private RectTransform MakeTextRect(string name, Vector2 pivot, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(root, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = pivot;
        rect.sizeDelta = size;
        return rect;
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
        // With the game showing through, a dark edge keeps the words readable over busy rooms
        text.outlineWidth = 0.22f;
        text.outlineColor = new Color32(0, 0, 0, 220);
        return text;
    }

    /// <summary>
    /// The rays, glow and sparkles, as camera-facing sprites in the room. They ignore depth so
    /// walls and furniture behind the character can't cut into them, and sort just under the
    /// character's sprite so the character stays in front.
    /// </summary>
    private void BuildBurst(SpriteRenderer character)
    {
        burst = new GameObject("BagRevealBurst");
        if (character != null)
            burst.layer = character.gameObject.layer;

        burstMaterial = new Material(Shader.Find("UI/Default"));
        burstMaterial.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);

        int layer = character != null ? character.sortingLayerID : 0;
        int order = character != null ? character.sortingOrder : 0;

        halo = MakeBurstSprite("Halo", glowSprite, layer, order - 5);
        raysB = MakeBurstSprite("RaysBack", raysSprite, layer, order - 4);
        raysA = MakeBurstSprite("Rays", raysSprite, layer, order - 3);
        core = MakeBurstSprite("Core", glowSprite, layer, order - 2);

        sparkles = new Sparkle[SparkleCount];
        for (int i = 0; i < SparkleCount; i++)
        {
            float angle = (i + UnityEngine.Random.value * 0.7f) / SparkleCount * Mathf.PI * 2f;
            Sparkle s = new Sparkle();
            s.renderer = MakeBurstSprite("Sparkle", glowSprite, layer, order - 1);
            s.direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            s.size = UnityEngine.Random.Range(0.015f, 0.033f);
            s.startRadius = UnityEngine.Random.Range(0.21f, 0.8f);
            s.speed = UnityEngine.Random.Range(0.035f, 0.09f);
            s.phase = UnityEngine.Random.value * Mathf.PI * 2f;
            s.twinkle = UnityEngine.Random.Range(3f, 6f);
            sparkles[i] = s;
        }
    }

    private SpriteRenderer MakeBurstSprite(string name, Sprite sprite, int sortingLayer, int sortingOrder)
    {
        GameObject go = new GameObject(name);
        go.layer = burst.layer;
        go.transform.SetParent(burst.transform, false);

        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sharedMaterial = burstMaterial;
        renderer.sortingLayerID = sortingLayer;
        renderer.sortingOrder = sortingOrder;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
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

        float titleIn = EaseOutCubic(Mathf.Clamp01((t - 0.12f) / 0.3f));
        titleRise = -14f * (1f - titleIn);
        title.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, titleIn);
    }

    // After the camera has moved for this frame, so the burst and the words line up with it
    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null || getFigure == null)
            return;

        Bounds figure = getFigure();
        float height = Mathf.Max(0.01f, figure.size.y);
        float t = elapsed;
        float fade = group.alpha;

        // The burst faces the camera square-on, a little behind the character
        Transform camTransform = cam.transform;
        burst.transform.SetPositionAndRotation(figure.center + camTransform.forward * height * BurstDepth,
                                               camTransform.rotation);

        float pop = EaseOutBack(Mathf.Clamp01(t / PopDuration));
        float raysScale = Mathf.LerpUnclamped(0.55f, 1f, pop);
        PlaceBurst(halo, HaloSize * height, 0f, HaloColor, fade);
        PlaceBurst(raysB, RaysSize * 0.82f * height * raysScale, 5f + t * 6f,
                   new Color(RayColor.r, RayColor.g, RayColor.b, 0.45f), fade);
        PlaceBurst(raysA, RaysSize * height * raysScale, -t * 9f, RayColor, fade);
        PlaceBurst(core, CoreSize * height, 0f,
                   new Color(CoreColor.r, CoreColor.g, CoreColor.b, CoreColor.a * (0.88f + 0.12f * Mathf.Sin(t * 4f))), fade);

        for (int i = 0; i < sparkles.Length; i++)
        {
            Sparkle s = sparkles[i];
            Vector2 offset = s.direction * (s.startRadius + s.speed * t) * height;
            s.renderer.transform.localPosition = new Vector3(offset.x, offset.y, 0f);
            float twinkle = 0.5f + 0.5f * Mathf.Sin(s.phase + t * s.twinkle);
            PlaceBurst(s.renderer, s.size * height, 0f, new Color(1f, 0.97f, 0.9f, twinkle * twinkle), fade, false);
        }

        // The words sit just above the bag and just below the feet, wherever those are on screen
        Vector2 top, bottom;
        if (ToCanvas(cam, figure.center + Vector3.up * height * 0.5f, out top))
            title.anchoredPosition = top + new Vector2(0f, TitleGap + titleRise);
        if (ToCanvas(cam, figure.center - Vector3.up * height * 0.5f, out bottom))
            hintRect.anchoredPosition = bottom - new Vector2(0f, HintGap);
    }

    private static void PlaceBurst(SpriteRenderer renderer, float worldSize, float angle, Color color, float fade, bool centred = true)
    {
        // The generated sprites are 100 pixels per unit
        float spriteSize = renderer.sprite.rect.width / renderer.sprite.pixelsPerUnit;
        Transform tr = renderer.transform;
        tr.localScale = Vector3.one * (worldSize / spriteSize);
        if (centred)
        {
            tr.localPosition = Vector3.zero;
            tr.localRotation = Quaternion.Euler(0f, 0f, angle);
        }
        color.a *= fade;
        renderer.color = color;
    }

    private bool ToCanvas(Camera cam, Vector3 world, out Vector2 local)
    {
        Vector3 screen = cam.WorldToScreenPoint(world);
        local = Vector2.zero;
        if (screen.z <= 0f)
            return false;
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out local);
    }

    void OnDestroy()
    {
        if (burst != null)
            Destroy(burst);
        if (burstMaterial != null)
            Destroy(burstMaterial);
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

using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Scene change with a loading screen: a white parallelogram sweeps across the screen and
/// uncovers the loading screen, whose bag icon fills up from the bottom as the scene loads.
/// Once it is full the loading screen slides up to reveal the new scene.
/// Start one with <see cref="LoadScene"/>; the prefab lives at Resources/LoadingScreen.
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    private const string PREFAB_PATH = "LoadingScreen";

    [Header("Look")]
    [SerializeField] private Sprite bagIcon;
    [SerializeField] private TMP_FontAsset font;
    [Tooltip("Only used without a bag icon - otherwise the panel samples the icon's own frame color.")]
    [SerializeField] private Color backgroundColor = new Color32(47, 47, 47, 255);
    [SerializeField] private Color fillColor = new Color32(255, 145, 77, 255);
    [SerializeField] private Color wipeColor = new Color32(218, 218, 218, 255);    // the references' off-white
    [SerializeField] private Color wipeStripeColor = new Color32(192, 192, 192, 255);
    [Tooltip("Keep this a multiple of the icon's 64px so the pixel art stays crisp.")]
    [SerializeField] private float iconSize = 192f;
    [Tooltip("The part of the icon the fill rises through, in normalized icon space: the bag's bounds plus a pixel of the opaque frame.")]
    [SerializeField] private Vector2 fillAreaMin = new Vector2(14f / 64f, 6f / 64f);
    [SerializeField] private Vector2 fillAreaMax = new Vector2(50f / 64f, 46f / 64f);
    [SerializeField] private string loadingText = "Loading";
    [SerializeField] private float fontSize = 44f;

    [Header("Timing")]
    [SerializeField] private float wipeDuration = 1.5f;
    [Tooltip("How far the parallelogram's top corners lean ahead of its bottom ones.")]
    [SerializeField] private float wipeSlant = 420f;
    [Tooltip("Width of the grey line along the parallelogram's right edge.")]
    [SerializeField] private float wipeStripeWidth = 44f;
    [Tooltip("The bag never fills faster than this, so quick loads still show the fill.")]
    [SerializeField] private float minFillDuration = 1.2f;
    [SerializeField] private float holdWhenFull = 0.25f;
    [SerializeField] private float slideUpDuration = 0.6f;

    /// <summary>True from the moment a load starts until the loading screen has slid away.</summary>
    public static bool IsLoading { get; private set; }

    private RectTransform canvasRect;
    private RectTransform wipe;
    private RectTransform loadingPanel;
    private RectTransform fill;

    /// <summary>
    /// Plays the loading transition into <paramref name="sceneName"/>. Falls back to a plain
    /// scene load if the prefab is missing.
    /// </summary>
    public static void LoadScene(string sceneName)
    {
        if (IsLoading)
            return;

        LoadingScreen prefab = Resources.Load<LoadingScreen>(PREFAB_PATH);
        if (prefab == null)
        {
            Debug.LogWarning("LoadingScreen prefab not found in Resources - loading without a transition.");
            SceneManager.LoadScene(sceneName);
            return;
        }

        LoadingScreen screen = Instantiate(prefab);
        DontDestroyOnLoad(screen.gameObject);
        screen.StartCoroutine(screen.Run(sceneName));
    }

    private IEnumerator Run(string sceneName)
    {
        IsLoading = true;
        Build();

        // Let the canvas scaler size the canvas before measuring it
        yield return null;
        Vector2 size = canvasRect.rect.size;

        yield return Wipe(size);
        yield return FillWhileLoading(sceneName);
        yield return SlideUp(size.y);

        IsLoading = false;
        Destroy(gameObject);
    }

    private IEnumerator Wipe(Vector2 size)
    {
        // The white body is wide enough that, for a stretch of its travel, it covers the whole screen
        const float coverMargin = 120f;
        float bodyWidth = size.x + wipeSlant * 2f + coverMargin * 2f;
        float totalWidth = bodyWidth + wipeStripeWidth;
        float slantedBody = wipeSlant / bodyWidth;

        CreatePolygon("Body", wipe, wipeColor, 0f, bodyWidth, new[]
        {
            new Vector2(0f, 0f), new Vector2(slantedBody, 1f), new Vector2(1f, 1f), new Vector2(1f - slantedBody, 0f)
        });

        // Grey stripe running along the leading edge
        float stripeWidth = wipeSlant + wipeStripeWidth;
        float slantedStripe = wipeSlant / stripeWidth;
        CreatePolygon("Stripe", wipe, wipeStripeColor, bodyWidth - wipeSlant, stripeWidth, new[]
        {
            new Vector2(0f, 0f), new Vector2(slantedStripe, 1f), new Vector2(1f, 1f), new Vector2(1f - slantedStripe, 0f)
        });

        // Height comes from the stretched anchors - only the width is set
        wipe.sizeDelta = new Vector2(totalWidth, 0f);
        wipe.gameObject.SetActive(true);

        float start = -totalWidth;
        float end = size.x;
        // Middle of the stretch where the body spans the whole screen - the loading screen swaps in behind it here
        float covered = -wipeSlant - coverMargin;

        for (float t = 0f; t < 1f;)
        {
            t = Mathf.Min(t + Step() / wipeDuration, 1f);
            float x = Mathf.Lerp(start, end, EaseInOutCubic(t));
            wipe.anchoredPosition = new Vector2(x, 0f);

            if (x >= covered && !loadingPanel.gameObject.activeSelf)
                loadingPanel.gameObject.SetActive(true);

            yield return null;
        }

        wipe.gameObject.SetActive(false);
    }

    private IEnumerator FillWhileLoading(string sceneName)
    {
        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
        load.allowSceneActivation = false;

        float shown = 0f;

        // Unity reports 0.9 once everything is loaded and only activation is left
        while (shown < 1f)
        {
            float loaded = Mathf.Clamp01(load.progress / 0.9f);
            shown = Mathf.MoveTowards(shown, loaded, Step() / minFillDuration);
            SetFill(shown);
            yield return null;
        }

        for (float held = 0f; held < holdWhenFull; held += Step())
            yield return null;

        load.allowSceneActivation = true;
        while (!load.isDone)
            yield return null;

        // One frame for the new scene's Start methods before it is revealed
        yield return null;
    }

    private IEnumerator SlideUp(float height)
    {
        for (float t = 0f; t < 1f;)
        {
            t = Mathf.Min(t + Step() / slideUpDuration, 1f);
            loadingPanel.anchoredPosition = new Vector2(0f, height * EaseInOutCubic(t));
            yield return null;
        }
    }

    private void SetFill(float amount)
    {
        fill.sizeDelta = new Vector2(0f, iconSize * (fillAreaMax.y - fillAreaMin.y) * amount);
    }

    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;

        // Swallows taps so nothing underneath can be pressed mid-transition
        gameObject.AddComponent<GraphicRaycaster>();
        canvasRect = (RectTransform)transform;

        // Loading screen: full-screen panel with the bag icon and label
        loadingPanel = CreateRect("LoadingPanel", transform);
        Stretch(loadingPanel);
        if (bagIcon != null)
        {
            // Painted with a corner texel of the icon itself, so the panel renders exactly like
            // the icon's dark frame - a plain color comes out a shade off after color conversion
            RawImage background = loadingPanel.gameObject.AddComponent<RawImage>();
            Texture2D texture = bagIcon.texture;
            Rect texel = bagIcon.textureRect;
            background.texture = texture;
            background.uvRect = new Rect((texel.x + 0.5f) / texture.width, (texel.y + 0.5f) / texture.height, 0f, 0f);
        }
        else
        {
            loadingPanel.gameObject.AddComponent<Image>().color = backgroundColor;
        }

        RectTransform iconFrame = CreateRect("Icon", loadingPanel);
        iconFrame.sizeDelta = new Vector2(iconSize, iconSize);
        iconFrame.anchoredPosition = new Vector2(0f, 30f);
        iconFrame.gameObject.AddComponent<RectMask2D>();

        // The icon is see-through inside its outline, so a bar rising behind it fills the bag.
        // The bar stays inside the bag's bounds, under the icon's opaque frame, so its edges
        // can't peek out past the sprite
        RectTransform fillArea = CreateRect("FillArea", iconFrame);
        fillArea.anchorMin = fillAreaMin;
        fillArea.anchorMax = fillAreaMax;
        fillArea.offsetMin = Vector2.zero;
        fillArea.offsetMax = Vector2.zero;

        fill = CreateRect("Fill", fillArea);
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(1f, 0f);
        fill.pivot = new Vector2(0.5f, 0f);
        fill.gameObject.AddComponent<Image>().color = fillColor;
        // Empty from the start - the wipe uncovers the panel before the fill begins
        SetFill(0f);

        RectTransform iconRect = CreateRect("Bag", iconFrame);
        Stretch(iconRect);
        Image icon = iconRect.gameObject.AddComponent<Image>();
        icon.sprite = bagIcon;
        icon.preserveAspect = true;

        RectTransform labelRect = CreateRect("Label", loadingPanel);
        labelRect.sizeDelta = new Vector2(400f, 60f);
        labelRect.anchoredPosition = new Vector2(0f, 30f - iconSize * 0.5f - 40f);
        TextMeshProUGUI label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = loadingText;
        if (font != null)
            label.font = font;
        label.fontSize = fontSize;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;

        loadingPanel.gameObject.SetActive(false);

        // Wipe: slides from off the left edge to off the right edge, above the loading panel
        wipe = CreateRect("Wipe", transform);
        wipe.anchorMin = new Vector2(0f, 0f);
        wipe.anchorMax = new Vector2(0f, 1f);
        wipe.pivot = new Vector2(0f, 0.5f);
        wipe.gameObject.SetActive(false);
    }

    private static RectTransform CreatePolygon(string name, RectTransform parent, Color color, float x, float width, Vector2[] points)
    {
        RectTransform rect = CreateRect(name, parent);
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(x, 0f);
        rect.sizeDelta = new Vector2(width, 0f);

        UIPolygon polygon = rect.gameObject.AddComponent<UIPolygon>();
        polygon.color = color;
        polygon.Points = points;
        return rect;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    // Capped like the other menu animations so a loading hitch doesn't skip the motion
    private static float Step() => Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);

    private static float EaseInOutCubic(float t)
    {
        return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
    }
}

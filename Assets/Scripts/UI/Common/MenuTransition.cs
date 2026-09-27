using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Menu-to-menu scene change: the screen fills with the bag's orange, the scene swaps behind
/// it, and the new scene opens out through a bag-shaped hole. On the title screen the orange
/// comes from zooming through the scene's own logo (see <see cref="ZoomFromNext"/>); elsewhere
/// a bag-shaped hole closes in on the last tap, like an iris.
/// Start one with <see cref="LoadScene"/>; the prefab lives at Resources/MenuTransition.
/// Gameplay loads use <see cref="LoadingScreen"/> instead.
/// </summary>
public class MenuTransition : MonoBehaviour
{
    private const string PREFAB_PATH = "MenuTransition";

    [Header("Look")]
    [Tooltip("Opaque bag orange outside the bag's outline, clear inside it.")]
    [SerializeField] private Sprite bagHole;
    [Tooltip("The flap's flat orange, in normalized bag-icon space. The zoom ends with this filling the screen.")]
    [SerializeField] private Rect flapArea = Rect.MinMaxRect(100f / 655f, 1f - 245f / 690f, 560f / 655f, 1f - 120f / 690f);
    [Tooltip("A rectangle inside the hole's outline, in normalized hole space. The iris is open once this covers the screen.")]
    [SerializeField] private Rect holeInner = Rect.MinMaxRect(138f / 735f, 1f - 688f / 770f, 597f / 735f, 1f - 130f / 770f);
    [Tooltip("Height of the hole when the iris is shut - it closes down to this and opens up from it.")]
    [SerializeField] private float holeShutHeight = 30f;

    [Header("Timing")]
    [SerializeField] private float irisCloseDuration = 0.5f;
    [SerializeField] private float anticipationDuration = 0.1f;
    [SerializeField] private float anticipationScale = 0.92f;
    [SerializeField] private float zoomDuration = 0.45f;
    [SerializeField] private float fadeDuration = 0.15f;
    [SerializeField] private float revealDuration = 0.55f;
    [Tooltip("Longest the cover waits on BlockReveal callers before revealing anyway.")]
    [SerializeField] private float maxRevealHold = 1.5f;

    /// <summary>True from the moment a transition starts until the new scene is fully shown.</summary>
    public static bool IsTransitioning { get; private set; }

    /// <summary>
    /// True while the new scene is still hidden. Intro animations wait for this to clear so
    /// they don't play out behind the cover.
    /// </summary>
    public static bool IsCovering { get; private set; }

    private static int revealBlockers;

    /// <summary>
    /// Keeps the cover up until <see cref="UnblockReveal"/> - e.g. while a background video
    /// prepares. Capped at maxRevealHold so it can't stick.
    /// </summary>
    public static void BlockReveal() => revealBlockers++;

    public static void UnblockReveal() => revealBlockers = Mathf.Max(0, revealBlockers - 1);

    // Scene logo for the next transition to zoom, set by ZoomFromNext
    private static RectTransform pendingBag;
    private static RectTransform[] pendingExtras;
    private static Graphic[] pendingFades;

    /// <summary>
    /// Makes the next transition zoom the scene's own bag logo instead of popping one in.
    /// <paramref name="extras"/> ride along with the bag (e.g. overlays drawn on it) and must
    /// share its parent; <paramref name="fades"/> fade out as the zoom starts.
    /// </summary>
    public static void ZoomFromNext(RectTransform bag, RectTransform[] extras, Graphic[] fades)
    {
        pendingBag = bag;
        pendingExtras = extras;
        pendingFades = fades;
    }

    private RectTransform canvasRect;
    private RectTransform cover;
    private RectTransform hole;
    private readonly RectTransform[] surrounds = new RectTransform[4];

    public static void LoadScene(string sceneName)
    {
        if (IsTransitioning || LoadingScreen.IsLoading)
            return;

        MenuTransition prefab = Resources.Load<MenuTransition>(PREFAB_PATH);
        if (prefab == null)
        {
            Debug.LogWarning("MenuTransition prefab not found in Resources - loading without a transition.");
            SceneManager.LoadScene(sceneName);
            return;
        }

        MenuTransition transition = Instantiate(prefab);
        DontDestroyOnLoad(transition.gameObject);
        transition.StartCoroutine(transition.Run(sceneName));
    }

    private IEnumerator Run(string sceneName)
    {
        IsTransitioning = true;
        IsCovering = true;
        revealBlockers = 0;

        RectTransform bag = pendingBag;
        RectTransform[] extras = pendingExtras ?? new RectTransform[0];
        Graphic[] fades = pendingFades ?? new Graphic[0];
        pendingBag = null;
        pendingExtras = null;
        pendingFades = null;

        // Read now, before the scene changes - the press that started this transition
        Vector2 tapScreen = Input.mousePosition;

        Build();

        // Let the canvas scaler size the canvas before measuring it
        yield return null;

        // Where the hole reopens: the screen center after a zoom (that's where it ends), or the
        // tap the iris closed on, so it opens back out from the same spot
        Vector2 openPoint = Vector2.zero;
        if (bag != null)
        {
            StartCoroutine(FadeOut(fades));
            yield return Anticipate(bag, extras);
            yield return ZoomThrough(bag, extras);
        }
        else
        {
            openPoint = TapPoint(tapScreen);
            yield return IrisClose(openPoint);
        }

        // The screen is all orange now - swap in our own cover so the scene can go
        cover.gameObject.SetActive(true);
        SetHoleVisible(false);

        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
        while (!load.isDone)
            yield return null;

        // One frame for the new scene's Awake/Start to register any reveal blockers
        yield return null;
        for (float held = 0f; revealBlockers > 0 && held < maxRevealHold; held += Step())
            yield return null;

        IsCovering = false;
        yield return Reveal(openPoint);

        IsTransitioning = false;
        Destroy(gameObject);
    }

    /// <summary>The tap in canvas space, or the screen center if it was off-screen.</summary>
    private Vector2 TapPoint(Vector2 screenPoint)
    {
        if (!new Rect(0f, 0f, Screen.width, Screen.height).Contains(screenPoint))
            return Vector2.zero;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out Vector2 local);
        return local;
    }

    /// <summary>Shrinks a bag-shaped hole in the orange down onto <paramref name="center"/>.</summary>
    private IEnumerator IrisClose(Vector2 center)
    {
        SetHoleVisible(true);
        float open = OpenScale(center);
        float shut = holeShutHeight / hole.sizeDelta.y;

        for (float t = 0f; t < 1f;)
        {
            t = Mathf.Min(t + Step() / irisCloseDuration, 1f);
            SetHole(center, open * Mathf.Pow(shut / open, EaseInOutQuad(t)));
            yield return null;
        }
    }

    /// <summary>A quick squash before the zoom, so it reads as a push rather than a jump.</summary>
    private IEnumerator Anticipate(RectTransform bag, RectTransform[] extras)
    {
        RectTransform[] parts = Parts(bag, extras);
        Vector2 center = bag.localPosition;
        Vector2[] positions = new Vector2[parts.Length];
        Vector3[] scales = new Vector3[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            positions[i] = parts[i].localPosition;
            scales[i] = parts[i].localScale;
        }

        for (float t = 0f; t < 1f;)
        {
            t = Mathf.Min(t + Step() / anticipationDuration, 1f);
            float s = Mathf.Lerp(1f, anticipationScale, EaseOutQuad(t));
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i].localPosition = center + (positions[i] - center) * s;
                parts[i].localScale = scales[i] * s;
            }
            yield return null;
        }
    }

    /// <summary>
    /// Scales the bag (and anything riding on it) around the middle of its flap while carrying
    /// that point to the screen center, until the flap covers the whole screen.
    /// </summary>
    private IEnumerator ZoomThrough(RectTransform bag, RectTransform[] extras)
    {
        RectTransform[] parts = Parts(bag, extras);
        Vector2 space = ((RectTransform)bag.parent).rect.size;
        Vector2 size = Vector2.Scale(bag.rect.size, bag.localScale);

        // Flap middle in the bag's parent space
        Vector2 focusStart = (Vector2)bag.localPosition + Vector2.Scale(flapArea.center - bag.pivot, size);
        Vector2 flapHalf = Vector2.Scale(flapArea.size, size) * 0.5f;
        // A little extra so the flap's shaded edges stay off-screen
        float endScale = Mathf.Max(space.x * 0.5f / flapHalf.x, space.y * 0.5f / flapHalf.y) * 1.1f;

        Vector2[] positions = new Vector2[parts.Length];
        Vector3[] scales = new Vector3[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            positions[i] = parts[i].localPosition;
            scales[i] = parts[i].localScale;
        }

        for (float t = 0f; t < 1f;)
        {
            t = Mathf.Min(t + Step() / zoomDuration, 1f);
            float e = EaseInQuad(t);
            // Exponential scale reads as a steady push toward the camera
            float s = Mathf.Pow(endScale, e);
            Vector2 focus = Vector2.Lerp(focusStart, Vector2.zero, e);

            for (int i = 0; i < parts.Length; i++)
            {
                parts[i].localPosition = focus + (positions[i] - focusStart) * s;
                parts[i].localScale = scales[i] * s;
            }
            yield return null;
        }
    }

    private IEnumerator FadeOut(Graphic[] graphics)
    {
        float[] alphas = new float[graphics.Length];
        for (int i = 0; i < graphics.Length; i++)
            alphas[i] = graphics[i] != null ? graphics[i].color.a : 0f;

        for (float t = 0f; t < 1f;)
        {
            t = Mathf.Min(t + Step() / fadeDuration, 1f);
            for (int i = 0; i < graphics.Length; i++)
            {
                if (graphics[i] == null)
                    continue;
                Color c = graphics[i].color;
                c.a = alphas[i] * (1f - t);
                graphics[i].color = c;
            }
            yield return null;
        }
    }

    /// <summary>Grows a bag-shaped hole in the cover from <paramref name="center"/> until it's gone.</summary>
    private IEnumerator Reveal(Vector2 center)
    {
        cover.gameObject.SetActive(false);
        SetHoleVisible(true);

        float shut = holeShutHeight / hole.sizeDelta.y;
        float open = OpenScale(center);

        for (float t = 0f; t < 1f;)
        {
            t = Mathf.Min(t + Step() / revealDuration, 1f);
            SetHole(center, shut * Mathf.Pow(open / shut, EaseInOutQuad(t)));
            yield return null;
        }
    }

    /// <summary>Hole scale at which its inner rectangle, centered on <paramref name="center"/>, covers the screen.</summary>
    private float OpenScale(Vector2 center)
    {
        Vector2 space = canvasRect.rect.size;
        Vector2 innerHalf = Vector2.Scale(holeInner.size, hole.sizeDelta) * 0.5f;
        float needX = space.x * 0.5f + Mathf.Abs(center.x);
        float needY = space.y * 0.5f + Mathf.Abs(center.y);
        return Mathf.Max(needX / innerHalf.x, needY / innerHalf.y) * 1.02f;
    }

    private void SetHoleVisible(bool visible)
    {
        hole.gameObject.SetActive(visible);
        foreach (RectTransform surround in surrounds)
            surround.gameObject.SetActive(visible);
    }

    /// <summary>
    /// Places the hole with its inner rectangle's middle on <paramref name="center"/> and fills
    /// the screen around it, since the hole sprite only covers its own rect.
    /// </summary>
    private void SetHole(Vector2 center, float scale)
    {
        hole.anchoredPosition = center;
        hole.localScale = Vector3.one * scale;

        Vector2 holeSize = hole.sizeDelta * scale;
        Vector2 space = canvasRect.rect.size;
        float left = center.x - hole.pivot.x * holeSize.x;
        float right = left + holeSize.x;
        float bottom = center.y - hole.pivot.y * holeSize.y;
        float top = bottom + holeSize.y;
        // Overlap the hole's opaque border by a unit so no seam shows
        const float overlap = 1f;
        float edgeX = space.x * 0.5f + 10f;
        float edgeY = space.y * 0.5f + 10f;

        SetRect(surrounds[0], -edgeX, top - overlap, edgeX, Mathf.Max(edgeY, top));              // above
        SetRect(surrounds[1], -edgeX, Mathf.Min(-edgeY, bottom), edgeX, bottom + overlap);       // below
        SetRect(surrounds[2], Mathf.Min(-edgeX, left), bottom, left + overlap, top);             // left
        SetRect(surrounds[3], right - overlap, bottom, Mathf.Max(edgeX, right), top);            // right
    }

    private static void SetRect(RectTransform rect, float xMin, float yMin, float xMax, float yMax)
    {
        rect.anchoredPosition = new Vector2((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f);
        rect.sizeDelta = new Vector2(xMax - xMin, yMax - yMin);
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

        // Painted with the hole texture's own orange border texel, so the cover renders exactly
        // like the hole sprite - a plain color comes out a shade off after color conversion
        Texture2D texture = bagHole.texture;
        Rect texel = bagHole.textureRect;
        Rect orange = new Rect((texel.x + 0.5f) / texture.width, (texel.y + 0.5f) / texture.height, 0f, 0f);

        cover = CreateRect("Cover", transform);
        cover.anchorMin = Vector2.zero;
        cover.anchorMax = Vector2.one;
        cover.offsetMin = Vector2.zero;
        cover.offsetMax = Vector2.zero;
        AddOrange(cover, texture, orange);
        cover.gameObject.SetActive(false);

        hole = CreateRect("Hole", transform);
        hole.pivot = holeInner.center;
        hole.sizeDelta = new Vector2(300f * bagHole.rect.width / bagHole.rect.height, 300f);
        hole.gameObject.AddComponent<Image>().sprite = bagHole;
        hole.gameObject.SetActive(false);

        for (int i = 0; i < surrounds.Length; i++)
        {
            surrounds[i] = CreateRect("Surround" + i, transform);
            AddOrange(surrounds[i], texture, orange);
            surrounds[i].gameObject.SetActive(false);
        }
    }

    private static void AddOrange(RectTransform rect, Texture2D texture, Rect uv)
    {
        RawImage image = rect.gameObject.AddComponent<RawImage>();
        image.texture = texture;
        image.uvRect = uv;
    }

    private static RectTransform[] Parts(RectTransform bag, RectTransform[] extras)
    {
        RectTransform[] parts = new RectTransform[extras.Length + 1];
        parts[0] = bag;
        extras.CopyTo(parts, 1);
        return parts;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    // Capped like the other menu animations so a loading hitch doesn't skip the motion
    private static float Step() => Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);

    private static float EaseInQuad(float t) => t * t;

    private static float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);

    private static float EaseInOutQuad(float t) => t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
}

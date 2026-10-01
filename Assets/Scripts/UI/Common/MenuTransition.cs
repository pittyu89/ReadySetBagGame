using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Menu-to-menu scene change: a bag-shaped hole in the bag's orange closes in on the last tap,
/// like an iris, the scene swaps behind the orange, and the new scene opens out through the
/// hole from the same spot.
/// Start one with <see cref="LoadScene"/>; the prefab lives at Resources/MenuTransition.
/// Gameplay loads use <see cref="LoadingScreen"/> instead.
/// </summary>
public class MenuTransition : MonoBehaviour
{
    private const string PREFAB_PATH = "MenuTransition";

    [Header("Look")]
    [Tooltip("Opaque bag orange outside the bag's outline, clear inside it.")]
    [SerializeField] private Sprite bagHole;
    [Tooltip("A rectangle inside the hole's outline, in normalized hole space. The iris is open once this covers the screen.")]
    [SerializeField] private Rect holeInner = Rect.MinMaxRect(138f / 735f, 1f - 688f / 770f, 597f / 735f, 1f - 130f / 770f);
    [Tooltip("Height of the hole when the iris is shut - it closes down to this and opens up from it.")]
    [SerializeField] private float holeShutHeight = 30f;

    [Header("Timing")]
    [SerializeField] private float irisCloseDuration = 0.5f;
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

        // Read now, before the scene changes - the press that started this transition
        Vector2 tapScreen = Input.mousePosition;

        Build();

        // Let the canvas scaler size the canvas before measuring it
        yield return null;

        // The hole reopens on the tap it closed on, so it opens back out from the same spot
        Vector2 openPoint = TapPoint(tapScreen);
        yield return IrisClose(openPoint);

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

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    // Capped like the other menu animations so a loading hitch doesn't skip the motion. Holds
    // full frame rate while it runs.
    private static float Step()
    {
        FrameRateManager.KeepSmooth();
        return Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
    }

    private static float EaseInOutQuad(float t) => t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
}

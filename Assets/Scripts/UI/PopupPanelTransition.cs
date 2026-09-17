using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Menu pop-up animation (Options, About): the window picks up speed as it slides in from off
/// the left edge and hits its resting place like a wall - rebounding to the left in a few
/// shrinking bounces that never pass it - while the backdrop dims. Closing slides it back out
/// to the left before the panel is disabled. Runs on unscaled time so it also works while the
/// game is paused.
/// Open with <see cref="Show"/> and close with <see cref="Hide"/>; both fall back to plain
/// SetActive for panels without this component.
/// </summary>
public class PopupPanelTransition : MonoBehaviour
{
    [SerializeField] private RectTransform window;
    [Tooltip("Full-screen backdrop that fades in behind the window.")]
    [SerializeField] private CanvasGroup dim;

    [Header("Slide")]
    [SerializeField] private float slideInDuration = 0.35f;
    [SerializeField] private float slideOutDuration = 0.28f;
    [Tooltip("Extra distance past the left edge the window starts from.")]
    [SerializeField] private float offscreenMargin = 60f;

    [Header("Impact Bounce")]
    [Tooltip("How far the window rebounds to the left on the first bounce.")]
    [SerializeField] private float bounceDistance = 14f;
    [Tooltip("Total time of all the bounces.")]
    [SerializeField] private float bounceDuration = 0.4f;
    [SerializeField] private int bounceCount = 3;
    [Tooltip("How much of its speed the window keeps on each bounce (0-1). Lower settles faster.")]
    [Range(0.1f, 0.9f)]
    [SerializeField] private float bounceRestitution = 0.45f;

    private CanvasGroup group;
    private Vector2 home;
    private bool homeCaptured;
    private Coroutine running;
    private bool closing;

    /// <summary>Opens <paramref name="panel"/>, animated if it has this component.</summary>
    public static void Show(GameObject panel)
    {
        if (panel == null)
            return;

        PopupPanelTransition transition = panel.GetComponent<PopupPanelTransition>();

        // Reopened while it was sliding out: turn it around instead of popping it
        if (transition != null && panel.activeSelf && transition.closing)
        {
            transition.PlayIn();
            return;
        }

        panel.SetActive(true);
    }

    /// <summary>
    /// Closes <paramref name="panel"/>, animated if it has this component, then runs
    /// <paramref name="onHidden"/>.
    /// </summary>
    public static void Hide(GameObject panel, Action onHidden = null)
    {
        if (panel == null)
        {
            onHidden?.Invoke();
            return;
        }

        PopupPanelTransition transition = panel.GetComponent<PopupPanelTransition>();
        if (transition != null && transition.isActiveAndEnabled)
        {
            transition.PlayOut(onHidden);
            return;
        }

        panel.SetActive(false);
        onHidden?.Invoke();
    }

    void Awake()
    {
        group = GetComponent<CanvasGroup>();
        if (group == null)
            group = gameObject.AddComponent<CanvasGroup>();

        CaptureHome();
    }

    void OnEnable()
    {
        PlayIn();
    }

    void OnDisable()
    {
        running = null;
        closing = false;

        // Rest in place, so the next open starts from a clean pose
        if (window != null && homeCaptured)
            window.anchoredPosition = home;
        if (dim != null)
            dim.alpha = 1f;
        group.blocksRaycasts = true;
    }

    private void CaptureHome()
    {
        if (window == null || homeCaptured)
            return;

        home = window.anchoredPosition;
        homeCaptured = true;
    }

    private void PlayIn()
    {
        if (!isActiveAndEnabled)
            return;

        closing = false;
        Restart(SlideIn());
    }

    private void PlayOut(Action onHidden)
    {
        closing = true;
        Restart(SlideOut(onHidden));
    }

    private void Restart(IEnumerator routine)
    {
        if (running != null)
            StopCoroutine(running);

        running = StartCoroutine(routine);
    }

    /// <summary>X offset that puts the window's right edge just past the left of the screen.</summary>
    private float OffscreenOffset()
    {
        RectTransform canvasRect = transform as RectTransform;
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
            canvasRect = canvas.rootCanvas.transform as RectTransform;

        float canvasWidth = canvasRect != null ? canvasRect.rect.width : 1280f;
        return -(canvasWidth * 0.5f + window.rect.width * 0.5f + offscreenMargin);
    }

    private IEnumerator SlideIn()
    {
        group.blocksRaycasts = true;
        if (window == null)
            yield break;

        float offscreen = OffscreenOffset();

        // A fresh open rests at home (see OnDisable) and starts off-screen; reopening mid-close
        // carries on from wherever the window got to
        float startOffset = window.anchoredPosition.x - home.x;
        float startDim = dim != null ? dim.alpha : 0f;
        if (Mathf.Approximately(startOffset, 0f))
        {
            startOffset = offscreen;
            startDim = 0f;
        }

        // Gathers speed on the way in, so it arrives moving fast enough to rebound
        for (float t = 0f; t < 1f;)
        {
            t = Mathf.Min(t + Step() / slideInDuration, 1f);
            SetOffset(Mathf.Lerp(startOffset, 0f, EaseInMild(t)));
            SetDim(Mathf.Lerp(startDim, 1f, t));
            yield return null;
        }

        // Impact: the resting place is a wall. Each bounce is an arc back to the left and into
        // the wall again; every bounce keeps a share of the speed, so it is lower and shorter
        // than the last - the way a real rebound dies out.
        int count = Mathf.Max(1, bounceCount);
        float r = Mathf.Clamp(bounceRestitution, 0.05f, 0.95f);
        float firstDuration = bounceDuration * (1f - r) / (1f - Mathf.Pow(r, count));

        for (int i = 0; i < count; i++)
        {
            float length = firstDuration * Mathf.Pow(r, i);
            float height = bounceDistance * Mathf.Pow(r, 2 * i);

            for (float elapsed = 0f; elapsed < length;)
            {
                elapsed = Mathf.Min(elapsed + Step(), length);
                float u = length > 0f ? elapsed / length : 1f;
                SetOffset(-4f * height * u * (1f - u));
                yield return null;
            }
        }

        SetOffset(0f);
        running = null;
    }

    private IEnumerator SlideOut(Action onHidden)
    {
        // No taps on a panel that is leaving
        group.blocksRaycasts = false;

        if (window != null)
        {
            float offscreen = OffscreenOffset();
            float startOffset = window.anchoredPosition.x - home.x;
            float startDim = dim != null ? dim.alpha : 1f;

            for (float t = 0f; t < 1f;)
            {
                t = Mathf.Min(t + Step() / slideOutDuration, 1f);
                SetOffset(Mathf.Lerp(startOffset, offscreen, EaseInCubic(t)));
                SetDim(Mathf.Lerp(startDim, 0f, t));
                yield return null;
            }
        }

        running = null;
        gameObject.SetActive(false);
        onHidden?.Invoke();
    }

    private void SetOffset(float x)
    {
        window.anchoredPosition = home + new Vector2(x, 0f);
    }

    private void SetDim(float alpha)
    {
        if (dim != null)
            dim.alpha = alpha;
    }

    // Capped like the other menu animations so a hitch doesn't skip the motion
    private static float Step() => Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);

    // Starts at 40% speed and ends at 160%: quick off the mark, but still accelerating into the wall
    private static float EaseInMild(float t) => t * (0.4f + 0.6f * t);
    private static float EaseInCubic(float t) => t * t * t;
}

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Freezes the game view into a blurred still and shows it behind a full-screen panel.
///
/// Same idea as the READY-SET-BAG splash: URP has no GrabPass, and the quiz freezes the game
/// anyway, so one snapshot pushed through a downsample chain is cheaper and closer to the
/// intended look than a live blur.
///
/// The grab uses <c>ScreenCapture.CaptureScreenshotIntoRenderTexture</c> rather than
/// <c>CaptureScreenshotAsTexture</c>. The latter round-trips the frame through the CPU and
/// returns an all-black texture on Android under Vulkan, which is what turned the quiz
/// feedback backdrop into a black slab on device. This path stays on the GPU.
/// </summary>
public class ScreenBlurBackdrop : MonoBehaviour
{
    private enum FlipMode
    {
        Auto,    // follow SystemInfo.graphicsUVStartsAtTop
        Never,
        Always
    }

    [SerializeField] private RawImage backdrop;

    [Header("Blur")]
    [Tooltip("Halvings applied to the captured frame. Each step doubles the blur radius.")]
    [SerializeField, Range(1, 6)] private int blurIterations = 3;
    [Tooltip("Halvings undone afterwards. Rebuilding the image in 2x steps instead of letting " +
             "the RawImage stretch it in one jump trades blockiness for a smoother falloff, at " +
             "the cost of a little blur strength. 0 keeps the old one-jump behaviour.")]
    [SerializeField, Range(0, 4)] private int upsampleSteps = 2;
    [Tooltip("Brightness multiplier on the blurred capture. Below 1 darkens the background.")]
    [SerializeField, Range(0f, 1f)] private float backdropTint = 0.55f;

    [Header("Capture")]
    [Tooltip("Grabbed frames arrive the other way up on some graphics APIs. Auto follows the " +
             "device; override if the backdrop appears upside down.")]
    [SerializeField] private FlipMode flip = FlipMode.Auto;
    [Tooltip("Colour used when the grab comes back blank, so a failed capture degrades to a " +
             "plain dim instead of a black screen.")]
    [SerializeField] private Color fallbackColor = new Color(0f, 0f, 0f, 0.62f);

    [Header("Fade")]
    [Tooltip("Seconds to fade the blur in over. 0 snaps it on, which is right when the blur " +
             "appears together with the panel in front of it.")]
    [SerializeField] private float fadeInDuration = 0f;
    [Tooltip("Seconds to fade the blur out over when FadeOutRoutine is used. 0 snaps it off.")]
    [SerializeField] private float fadeOutDuration = 0f;

    private RenderTexture blurTexture;

    // Alpha the backdrop settles at once shown — 1 for a real capture, the fallback's own
    // alpha when the grab failed. The fades ramp to and from this rather than a hardcoded 1.
    private float shownAlpha = 1f;

    private void Awake()
    {
        if (backdrop == null)
            backdrop = GetComponent<RawImage>();

        Clear();
    }

    private void OnDisable()
    {
        Clear();
    }

    /// <summary>
    /// Captures the screen and shows the blurred result.
    /// <paramref name="uiCanvas"/> is switched off for the capture frame so the snapshot
    /// holds the game world only, not the UI that is about to be drawn over it.
    /// </summary>
    public IEnumerator CaptureRoutine(Canvas uiCanvas)
    {
        // Clear first: a stale blur left on screen would end up inside the new snapshot.
        Clear();

        bool canvasWasEnabled = uiCanvas != null && uiCanvas.enabled;
        if (uiCanvas != null)
            uiCanvas.enabled = false;

        // The grab has to happen after the scene has finished rendering.
        yield return new WaitForEndOfFrame();

        Capture();

        if (uiCanvas != null)
            uiCanvas.enabled = canvasWasEnabled;

        yield return FadeIn();
    }

    /// <summary>
    /// Same capture, but with the UI left switched on so the snapshot includes it — used to
    /// push the whole quiz back behind the correct / wrong overlay.
    /// </summary>
    public IEnumerator CaptureIncludingUIRoutine()
    {
        yield return CaptureRoutine(null);
    }

    private void Capture()
    {
        if (backdrop == null || Screen.width <= 0 || Screen.height <= 0)
            return;

        Clear();

        int width = Mathf.Max(1, Screen.width);
        int height = Mathf.Max(1, Screen.height);

        RenderTexture shot = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        ScreenCapture.CaptureScreenshotIntoRenderTexture(shot);

        bool flipNeeded = flip == FlipMode.Always ||
                          (flip == FlipMode.Auto && SystemInfo.graphicsUVStartsAtTop);

        // Each Blit into a half-size target is a 2x2 bilinear box filter; chaining them builds
        // a mip pyramid. The flip rides along on the first blit so it costs nothing extra.
        RenderTexture current = null;
        for (int i = 0; i < blurIterations; i++)
        {
            width = Mathf.Max(2, width / 2);
            height = Mathf.Max(2, height / 2);

            RenderTexture next = RenderTexture.GetTemporary(width, height, 0);
            next.filterMode = FilterMode.Bilinear;

            Texture source = current != null ? (Texture)current : shot;
            if (i == 0 && flipNeeded)
                Graphics.Blit(source, next, new Vector2(1f, -1f), new Vector2(0f, 1f));
            else
                Graphics.Blit(source, next);

            if (current != null)
                RenderTexture.ReleaseTemporary(current);
            current = next;
        }

        RenderTexture.ReleaseTemporary(shot);

        if (current == null)
            return;

        // A grab that failed comes back uniformly black. Showing that would cover the screen
        // in a black slab, so fall back to a plain dim that at least reads as deliberate.
        if (IsBlank(current))
        {
            RenderTexture.ReleaseTemporary(current);
            ShowFallback();
            return;
        }

        // Walk part of the way back up in 2x steps. Each bilinear upsample convolves the image
        // with another tent filter, and stacked tents approximate a Gaussian — so the result
        // reads as a smooth blur instead of the visibly piecewise stretch you get when the
        // RawImage scales a tiny texture straight to full screen.
        for (int i = 0; i < upsampleSteps; i++)
        {
            int upW = Mathf.Min(Screen.width, current.width * 2);
            int upH = Mathf.Min(Screen.height, current.height * 2);
            if (upW == current.width && upH == current.height)
                break;

            RenderTexture up = RenderTexture.GetTemporary(upW, upH, 0);
            up.filterMode = FilterMode.Bilinear;

            Graphics.Blit(current, up);

            RenderTexture.ReleaseTemporary(current);
            current = up;
        }

        // Copy out of the temporary pool into a texture we own for as long as it's shown.
        blurTexture = new RenderTexture(current.width, current.height, 0)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        Graphics.Blit(current, blurTexture);
        RenderTexture.ReleaseTemporary(current);

        shownAlpha = 1f;
        backdrop.texture = blurTexture;
        backdrop.color = new Color(backdropTint, backdropTint, backdropTint,
                                   fadeInDuration > 0f ? 0f : shownAlpha);
        backdrop.enabled = true;
    }

    /// <summary>
    /// Reads back an 8x8 reduction of the grab to see whether anything was captured at all.
    /// Sampling a tiny mip keeps the GPU sync negligible.
    /// </summary>
    private bool IsBlank(RenderTexture source)
    {
        RenderTexture tiny = RenderTexture.GetTemporary(8, 8, 0);
        tiny.filterMode = FilterMode.Bilinear;
        Graphics.Blit(source, tiny);

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = tiny;

        Texture2D probe = new Texture2D(8, 8, TextureFormat.RGB24, false);
        probe.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
        probe.Apply();

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(tiny);

        float brightest = 0f;
        foreach (Color c in probe.GetPixels())
            brightest = Mathf.Max(brightest, c.r + c.g + c.b);

        Destroy(probe);

        return brightest < 0.03f;
    }

    private void ShowFallback()
    {
        shownAlpha = fallbackColor.a;
        backdrop.texture = null;
        backdrop.color = new Color(fallbackColor.r, fallbackColor.g, fallbackColor.b,
                                   fadeInDuration > 0f ? 0f : shownAlpha);
        backdrop.enabled = true;
    }

    /// <summary>
    /// Fades the blur away and then releases it. Use instead of <see cref="Clear"/> when the
    /// blur should ease off rather than pop.
    /// </summary>
    public IEnumerator FadeOutRoutine()
    {
        if (backdrop == null || !backdrop.enabled || fadeOutDuration <= 0f)
        {
            Clear();
            yield break;
        }

        Color c = backdrop.color;
        float startAlpha = c.a;

        for (float t = 0f; t < fadeOutDuration; t += Time.unscaledDeltaTime)
        {
            c.a = Mathf.Lerp(startAlpha, 0f, t / fadeOutDuration);
            backdrop.color = c;
            yield return null;
        }

        Clear();
    }

    private IEnumerator FadeIn()
    {
        if (backdrop == null || !backdrop.enabled || fadeInDuration <= 0f)
            yield break;

        Color target = backdrop.color;

        for (float t = 0f; t < fadeInDuration; t += Time.unscaledDeltaTime)
        {
            target.a = Mathf.Lerp(0f, shownAlpha, t / fadeInDuration);
            backdrop.color = target;
            yield return null;
        }

        target.a = shownAlpha;
        backdrop.color = target;
    }

    /// <summary>
    /// Hides the backdrop and releases the snapshot.
    /// </summary>
    public void Clear()
    {
        if (backdrop != null)
        {
            backdrop.texture = null;
            backdrop.enabled = false;
        }

        if (blurTexture == null)
            return;

        blurTexture.Release();
        Destroy(blurTexture);
        blurTexture = null;
    }
}

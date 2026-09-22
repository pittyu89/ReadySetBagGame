using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The band across the top of the screen that drains whenever something is on the clock.
///
/// There is one of these, and the minigames and the quiz questions both count down on it, so
/// there is no second timer to keep in step — the quiz has no number of its own. Anything
/// with seconds to spend calls <see cref="SetTime"/> each frame.
///
/// THE COLOUR
///
/// It never snaps. Green is held while there is room, red is held once time is nearly gone,
/// and in between the bar bleeds from one to the next across <see cref="blendWidth"/>, so the
/// player reads the drain as it happens instead of being surprised by a colour change. The
/// thresholds are still the same two numbers as before — <see cref="warnFraction"/> and
/// <see cref="dangerFraction"/> — they are now the middle of a fade rather than a cliff.
/// Set blendWidth to 0 for the old hard switch.
///
/// THE SHAPE
///
/// A plain grey groove with the fill clipped inside it, square at both ends and flush across
/// the full width of the screen. The clipping rect is driven from here, so the scene only has
/// to hold the three pieces — track, clip, fill — and this sets their anchors itself. See
/// <see cref="LayOutFill"/>.
/// </summary>
[DisallowMultipleComponent]
public class CountdownBar : MonoBehaviour
{
    [Header("Pieces")]
    [Tooltip("The grey groove behind the fill. Optional — only recoloured, never resized.")]
    [SerializeField] private Image track;
    [Tooltip("The rect the fill is clipped to. Needs a RectMask2D; its width is this bar's " +
             "width times the fraction remaining.")]
    [SerializeField] private RectTransform fillClip;
    [Tooltip("The coloured part. Sits inside the clip at the bar's full width, so it slides " +
             "out of view rather than squashing as the time goes.")]
    [SerializeField] private Image fill;
    [Tooltip("Optional seconds readout, tinted along with the fill.")]
    [SerializeField] private TextMeshProUGUI label;

    [Header("Colour")]
    [Tooltip("While there is plenty of time left.")]
    [SerializeField] private Color fullColor = new Color(0.588f, 0.690f, 0f, 1f);
    [Tooltip("Around the warning threshold.")]
    [SerializeField] private Color warnColor = new Color(0.898f, 0.741f, 0.161f, 1f);
    [Tooltip("Once time is nearly gone.")]
    [SerializeField] private Color dangerColor = new Color(1f, 0.263f, 0.263f, 1f);
    [Tooltip("The colour of the groove behind the fill.")]
    [SerializeField] private Color trackColor = new Color(0.851f, 0.851f, 0.851f, 1f);

    [Tooltip("Fraction remaining at the middle of the green-to-yellow fade.")]
    [SerializeField, Range(0f, 1f)] private float warnFraction = 0.75f;
    [Tooltip("Fraction remaining at the middle of the yellow-to-red fade.")]
    [SerializeField, Range(0f, 1f)] private float dangerFraction = 0.25f;
    [Tooltip("How much of the bar each fade is spread over, in fractions remaining. 0.1 " +
             "means the threshold at 0.75 fades from 0.80 down to 0.70, so each colour is " +
             "still itself either side of the change and only the crossing is gradual. 0 " +
             "brings back the hard switch; large values turn the whole bar into one long " +
             "gradient with no flat colour anywhere.")]
    [SerializeField, Range(0f, 1f)] private float blendWidth = 0.1f;

    [Header("Running Out")]
    [Tooltip("How hard the bar breathes once it is into the red. Off by default — the bar " +
             "is meant to read as one flat colour at any moment. Raise it if the last few " +
             "seconds need to catch the eye of a player looking at the minigame.")]
    [SerializeField, Range(0f, 0.5f)] private float dangerPulseDepth = 0f;
    [Tooltip("Breaths per second while in the red.")]
    [SerializeField] private float dangerPulseSpeed = 3.2f;

    // What the last SetProgress was given, so the pulse can keep animating between calls
    private float fraction = 1f;
    private RectTransform rectTransform;

    private RectTransform RectTransform
    {
        get
        {
            if (rectTransform == null)
                rectTransform = (RectTransform)transform;

            return rectTransform;
        }
    }

    private void OnEnable()
    {
        if (track != null)
            track.color = trackColor;

        Apply();
    }

    private void Update()
    {
        // The width is only known once the canvas has laid out, and it changes with the
        // screen, so the fill is placed every frame rather than once on enable.
        Apply();
    }

    /// <summary>
    /// Shows the bar and sets it to full. Call this as the clock starts.
    /// </summary>
    public void Begin()
    {
        fraction = 1f;
        gameObject.SetActive(true);
        Apply();
    }

    /// <summary>
    /// Hides the bar. Call this when whatever was being timed is over.
    /// </summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>
    /// The usual way to drive the bar: how many seconds are left, out of how many.
    /// A limit of zero or less leaves the bar full, since there is nothing to count down.
    /// </summary>
    public void SetTime(float remaining, float limit)
    {
        SetProgress(limit > 0f ? remaining / limit : 1f);

        if (label != null)
            label.text = Mathf.CeilToInt(Mathf.Max(0f, remaining)).ToString();
    }

    /// <summary>
    /// True if <paramref name="candidate"/> is the readout this bar is already writing, so a
    /// caller that keeps its own reference to the same label knows to leave it alone rather
    /// than tinting it a second way.
    /// </summary>
    public bool OwnsLabel(TextMeshProUGUI candidate)
    {
        return candidate != null && label == candidate;
    }

    /// <summary>
    /// Drives the bar straight from a 0-to-1 fraction, for anything not measured in seconds.
    /// </summary>
    public void SetProgress(float value)
    {
        fraction = Mathf.Clamp01(value);
        Apply();
    }

    private void Apply()
    {
        LayOutFill();

        Color colour = ColorFor(fraction);

        if (fill != null)
            fill.color = colour;

        if (label != null)
            label.color = colour;
    }

    /// <summary>
    /// Sizes the clipping rect to the fraction remaining and keeps the fill inside it at the
    /// bar's full width, so the fill does not shrink — it is uncovered less. Clipping rather
    /// than scaling is what keeps the drained edge a clean vertical cut at every width.
    /// </summary>
    private void LayOutFill()
    {
        if (fillClip == null)
            return;

        fillClip.anchorMin = new Vector2(0f, 0f);
        fillClip.anchorMax = new Vector2(fraction, 1f);
        fillClip.pivot = new Vector2(0f, 0.5f);
        fillClip.offsetMin = Vector2.zero;
        fillClip.offsetMax = Vector2.zero;

        if (fill == null)
            return;

        RectTransform fillRect = fill.rectTransform;
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.anchoredPosition = Vector2.zero;
        fillRect.sizeDelta = new Vector2(RectTransform.rect.width, 0f);
    }

    /// <summary>
    /// The bar's colour at a given fraction remaining: green, yellow and red each held flat
    /// over their own stretch of the bar, with a fade of <see cref="blendWidth"/> centred on
    /// each threshold. Smoothstepped, so the change eases in and out rather than sliding at a
    /// constant rate and reading as a slow wipe.
    /// </summary>
    private Color ColorFor(float value)
    {
        float half = blendWidth * 0.5f;

        Color colour;

        if (value >= warnFraction + half)
            colour = fullColor;
        else if (value > warnFraction - half)
            colour = Color.Lerp(warnColor, fullColor, Ease(value, warnFraction - half, warnFraction + half));
        else if (value >= dangerFraction + half)
            colour = warnColor;
        else if (value > dangerFraction - half)
            colour = Color.Lerp(dangerColor, warnColor, Ease(value, dangerFraction - half, dangerFraction + half));
        else
            colour = dangerColor;

        return Pulsed(colour, value);
    }

    /// <summary>
    /// Brightens and dims the bar once it is into the red. Off unless
    /// <see cref="dangerPulseDepth"/> is raised; when it is on, it fades in over the red band
    /// rather than starting at full strength, so the pulse arrives rather than appears.
    /// </summary>
    private Color Pulsed(Color colour, float value)
    {
        if (dangerPulseDepth <= 0f)
            return colour;

        float onset = dangerFraction + blendWidth * 0.5f;
        if (onset <= 0f || value >= onset)
            return colour;

        float strength = 1f - Mathf.Clamp01(value / onset);
        float wave = Mathf.Sin(Time.unscaledTime * dangerPulseSpeed * Mathf.PI * 2f) * 0.5f + 0.5f;

        return Color.Lerp(colour, Color.white, wave * strength * dangerPulseDepth);
    }

    private static float Ease(float value, float from, float to)
    {
        if (to - from <= Mathf.Epsilon)
            return value >= to ? 1f : 0f;

        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));
    }
}

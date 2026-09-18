using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Shows the correct / wrong verdict over the quiz as a pixel-font word on a band,
/// in the style of the READY-SET-BAG splash.
///
/// This replaced a pair of alpha WebM clips. Those decoded fine in the Editor but the
/// surrounding backdrop came out black on Android, and video was heavy for two words of
/// text — this costs nothing to draw and behaves the same on every device.
///
/// Everything runs on unscaled time: the quiz stays open while the game timer is paused.
/// </summary>
public class QuizFeedbackBanner : MonoBehaviour
{
    [Header("Display")]
    [SerializeField] private TextMeshProUGUI label;
    [Tooltip("Band drawn behind the word, like the READY-SET-BAG splash.")]
    [SerializeField] private GameObject band;

    [Header("Wording")]
    [SerializeField] private string correctText = "CORRECT";
    [SerializeField] private string wrongText = "NICE TRY";
    // Sampled from the original clips so the wording keeps their palette
    [SerializeField] private Color correctColor = new Color32(0x8B, 0xFF, 0x5E, 0xFF);
    [SerializeField] private Color wrongColor = new Color32(0xFE, 0xFF, 0x63, 0xFF);

    [Header("Timing")]
    [Tooltip("Scale-and-fade in.")]
    [SerializeField] private float popInDuration = 0.22f;
    [Tooltip("How long the word sits fully readable.")]
    [SerializeField] private float holdDuration = 0.95f;
    [Tooltip("Scale-and-fade out.")]
    [SerializeField] private float popOutDuration = 0.18f;
    [Tooltip("How far past full size the word overshoots before settling.")]
    [SerializeField] private float overshoot = 1.12f;
    [Tooltip("Seconds the band takes to fade in and back out around the word.")]
    [SerializeField] private float bandFadeDuration = 0.2f;

    // Fades the band without touching its Image colour, so the band's own tint stays authored
    private CanvasGroup bandGroup;

    // The band fades run unawaited alongside the word, so Hide has to be able to cancel them
    private Coroutine bandFade;

    private RectTransform labelRect;

    private void Awake()
    {
        if (band != null)
        {
            bandGroup = band.GetComponent<CanvasGroup>();
            if (bandGroup == null)
                bandGroup = band.AddComponent<CanvasGroup>();
        }

        if (label != null)
            labelRect = label.rectTransform;

        Hide();
    }

    /// <summary>
    /// Shows the verdict and yields until it has finished.
    ///
    /// The verdict is the whole of what this shows. The reason behind it is typed into the
    /// quiz's dialogue box instead — see QuizManager — since that is where the player is
    /// already reading everything else the quiz says.
    /// </summary>
    public IEnumerator Play(bool isCorrect)
    {
        if (label == null)
        {
            yield return new WaitForSecondsRealtime(popInDuration + holdDuration + popOutDuration);
            yield break;
        }

        label.text = isCorrect ? correctText : wrongText;
        label.color = isCorrect ? correctColor : wrongColor;
        label.gameObject.SetActive(true);

        float fade = Mathf.Min(bandFadeDuration, popInDuration + holdDuration + popOutDuration);

        if (band != null)
        {
            band.SetActive(true);
            StopBandFade();
            bandFade = StartCoroutine(FadeBand(0f, 1f, fade));
        }

        // Overshoot on the way in so the word lands with a bit of weight
        yield return ScaleAndFade(0.55f, overshoot, 0f, 1f, popInDuration * 0.7f);
        yield return ScaleAndFade(overshoot, 1f, 1f, 1f, popInDuration * 0.3f);

        yield return new WaitForSecondsRealtime(holdDuration);

        if (band != null)
        {
            StopBandFade();
            bandFade = StartCoroutine(FadeBand(1f, 0f, Mathf.Min(fade, popOutDuration)));
        }

        yield return ScaleAndFade(1f, 1.18f, 1f, 0f, popOutDuration);

        Hide();
    }

    /// <summary>
    /// Ramps the word's scale and alpha together over <paramref name="duration"/>.
    /// </summary>
    private IEnumerator ScaleAndFade(float fromScale, float toScale, float fromAlpha, float toAlpha, float duration)
    {
        if (labelRect == null || duration <= 0f)
        {
            if (labelRect != null)
                labelRect.localScale = Vector3.one * toScale;
            if (label != null)
                label.alpha = toAlpha;
            yield break;
        }

        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float k = t / duration;
            labelRect.localScale = Vector3.one * Mathf.Lerp(fromScale, toScale, k);
            label.alpha = Mathf.Lerp(fromAlpha, toAlpha, k);
            yield return null;
        }

        labelRect.localScale = Vector3.one * toScale;
        label.alpha = toAlpha;
    }

    private IEnumerator FadeBand(float from, float to, float duration)
    {
        if (bandGroup == null || duration <= 0f)
        {
            if (bandGroup != null)
                bandGroup.alpha = to;
            yield break;
        }

        bandGroup.alpha = from;

        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            bandGroup.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }

        bandGroup.alpha = to;
    }

    private void StopBandFade()
    {
        if (bandFade == null)
            return;

        StopCoroutine(bandFade);
        bandFade = null;
    }

    /// <summary>
    /// Puts the verdict away and resets it so the next one starts from nothing.
    /// </summary>
    public void Hide()
    {
        StopBandFade();

        if (bandGroup != null)
            bandGroup.alpha = 0f;

        if (band != null)
            band.SetActive(false);

        if (label != null)
        {
            label.alpha = 1f;
            label.gameObject.SetActive(false);
        }

        if (labelRect != null)
            labelRect.localScale = Vector3.one;
    }
}

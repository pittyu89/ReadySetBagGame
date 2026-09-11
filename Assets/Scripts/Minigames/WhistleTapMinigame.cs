using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The whistle minigame that runs after the quiz's whistle question.
///
/// The player mashes one big button until the bar under it is full — the rescuers hearing
/// you takes more than one blow on a whistle, and the bar is how long that goes on for.
///
/// Like the water minigame there is nothing to fail here. It runs whether the quiz answer
/// was right or wrong, and there is no timer and no decay on the bar: taps only ever add.
/// The point is the beat, not another test.
///
/// <see cref="Play"/> is a coroutine so QuizHandler can simply yield on it and carry on
/// with the next question once it returns.
/// </summary>
public class WhistleTapMinigame : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private CanvasGroup panelGroup;
    [Tooltip("Blurs the quiz behind the minigame, the same way the correct / wrong " +
             "overlay does. Optional.")]
    [SerializeField] private ScreenBlurBackdrop backdrop;

    [Header("Instruction")]
    [SerializeField] private CanvasGroup instructionCard;
    [SerializeField] private TextMeshProUGUI instructionLabel;
    [SerializeField, TextArea] private string instructionText = "Keep tapping till the rescuer notice you!";

    [Header("Tapping")]
    [SerializeField] private WhistleTapButton tapButton;
    [Tooltip("Taps to fill the bar from empty at full tap value, with nothing draining away. " +
             "The real number is always higher: decay takes some back, and taps are worth " +
             "less the fuller the bar gets — see progressDecayPerSecond and lateTapStrength.")]
    [SerializeField] private int requiredTaps = 12;

    [Tooltip("What a tap is worth on a full bar, as a fraction of what it is worth on an " +
             "empty one. 1 means every tap counts the same and the bar fills at a flat " +
             "rate; 0.75 means the last stretch costs a third as much again as the first, " +
             "so the bar pushes back as it approaches the end.\n\n" +
             "Careful going much below this. Taps get weaker towards the top while decay " +
             "stays constant, so past a point the bar stops being slow and starts being " +
             "impossible: at 0.55 with a decay of 0.18, a player tapping four times a " +
             "second is stuck at 72% for good, which the minigame is not supposed to " +
             "allow. These are tuned for Grade 6 players, not for adults mashing.")]
    [SerializeField, Range(0.1f, 1f)] private float lateTapStrength = 0.75f;

    [Header("Progress Bar")]
    [Tooltip("Filled left to right. Needs Image Type = Filled, Horizontal, Origin Left.")]
    [SerializeField] private Image progressFill;
    [Tooltip("How fast the green catches up to where the taps have got to. The bar chases " +
             "rather than jumping, so a burst of taps reads as one smooth surge and a " +
             "drain reads as a slide rather than a stutter.")]
    [SerializeField] private float progressCatchUpSpeed = 9f;
    [Tooltip("Fraction of the whole bar lost per second while the player is not keeping " +
             "up — 0.13 empties a full bar in about 7.5 seconds. Stopping never fails the " +
             "minigame, it only means the bar sits still; the player can always tap out " +
             "of it. Set 0 for a bar that only ever goes up.")]
    [SerializeField] private float progressDecayPerSecond = 0.13f;
    [Tooltip("Safety net for the settle after the last tap, in case the lerp is set so " +
             "slow it would never quite arrive.")]
    [SerializeField] private float progressSettleTimeout = 1.5f;

    [Header("Timing")]
    [SerializeField] private float panelFadeDuration = 0.25f;
    [SerializeField] private float instructionFadeDuration = 0.3f;
    [Tooltip("Pause once the bar is full, before the minigame closes.")]
    [SerializeField] private float finishDelay = 0.9f;

    [Header("Finish")]
    [Tooltip("Optional. Flashed once the bar is full.")]
    [SerializeField] private GameObject completedBanner;

    private bool isPlaying = false;

    // How full the bar is "really", 0-1. The Image chases this rather than being written
    // straight to, so gains and losses both arrive as a slide. Taps push it up, decay
    // pulls it down, and the minigame ends when it reaches 1.
    private float progress = 0f;

    public bool IsPlaying => isPlaying;

    private void Awake()
    {
        if (panelGroup == null && panelRoot != null)
            panelGroup = panelRoot.GetComponent<CanvasGroup>();

        if (instructionLabel != null && !string.IsNullOrEmpty(instructionText))
            instructionLabel.text = instructionText;

        // Reset the contents but leave the panel's active state alone. Awake first runs
        // during the SetActive in Open, so deactivating here would switch the panel back
        // off underneath the very coroutine that just turned it on.
        ResetVisuals();
    }

    private void OnEnable()
    {
        if (tapButton != null)
            tapButton.Tapped += OnTapped;
    }

    private void OnDisable()
    {
        if (tapButton != null)
            tapButton.Tapped -= OnTapped;
    }

    /// <summary>
    /// Runs the whole minigame and returns once it has closed.
    /// Yield on this from the quiz; it never returns early or leaves the panel up.
    /// </summary>
    public IEnumerator Play()
    {
        if (isPlaying)
            yield break;

        if (tapButton == null || progressFill == null || requiredTaps <= 0)
        {
            // Bail loudly rather than quietly: a half-wired panel would otherwise hang the
            // quiz on a bar that can never fill.
            Debug.LogWarning("[WhistleTapMinigame] Needs a tap button, a progress fill and " +
                             "at least one required tap — skipping.", this);
            yield break;
        }

        isPlaying = true;

        Open();

        if (backdrop != null)
            yield return StartCoroutine(backdrop.CaptureIncludingUIRoutine());

        yield return FadeGroup(panelGroup, 0f, 1f, panelFadeDuration);
        yield return FadeGroup(instructionCard, 0f, 1f, instructionFadeDuration);

        tapButton.SetArmed(true);

        while (progress < 1f)
        {
            AdvanceBar(true);
            yield return null;
        }

        // Taps stop counting the moment the bar is earned, but the green still has to catch
        // up to them — closing mid-chase would hide the last third of the fill. Decay is
        // off from here, or the bar would drain out from under its own finish.
        tapButton.SetArmed(false);

        for (float t = 0f; t < progressSettleTimeout && progressFill.fillAmount < 0.999f; t += Time.unscaledDeltaTime)
        {
            AdvanceBar(false);
            yield return null;
        }

        progressFill.fillAmount = 1f;

        // The banner sits where the button is, the way the water minigame's sits over its
        // glasses. Nothing is left to press by now, so the button steps aside rather than
        // showing COMPLETED stamped across a button that still says TAP.
        if (completedBanner != null)
        {
            tapButton.gameObject.SetActive(false);
            completedBanner.SetActive(true);
        }

        yield return new WaitForSecondsRealtime(finishDelay);

        yield return FadeGroup(instructionCard, 1f, 0f, instructionFadeDuration);
        yield return FadeGroup(panelGroup, 1f, 0f, panelFadeDuration);

        if (backdrop != null)
            yield return StartCoroutine(backdrop.FadeOutRoutine());

        Close();
        isPlaying = false;
    }

    private void OnTapped()
    {
        if (!isPlaying || progress >= 1f)
            return;

        // A tap is worth less the fuller the bar is. A flat gain made the whole thing feel
        // the same from the first press to the last, so there was nothing to push against;
        // tapering it means the player has to lean into the finish rather than coasting to
        // it on the same rhythm that started the bar.
        float gain = (1f / requiredTaps) * Mathf.Lerp(1f, lateTapStrength, progress);

        progress = Mathf.Clamp01(progress + gain);
    }

    /// <summary>
    /// Drains the bar for the frame, then eases the green towards wherever that leaves it.
    /// Framerate independent, so the drain and the surge both look the same on a phone
    /// that is struggling as on one that is not.
    /// </summary>
    private void AdvanceBar(bool applyDecay)
    {
        if (applyDecay && progressDecayPerSecond > 0f)
            progress = Mathf.Clamp01(progress - progressDecayPerSecond * Time.unscaledDeltaTime);

        float k = 1f - Mathf.Exp(-progressCatchUpSpeed * Time.unscaledDeltaTime);
        progressFill.fillAmount = Mathf.Lerp(progressFill.fillAmount, progress, k);
    }

    private void Open()
    {
        if (panelRoot != null)
            panelRoot.SetActive(true);

        if (panelGroup != null)
        {
            panelGroup.alpha = 0f;
            panelGroup.blocksRaycasts = true;
        }

        if (instructionCard != null)
            instructionCard.alpha = 0f;

        if (completedBanner != null)
            completedBanner.SetActive(false);

        progress = 0f;

        if (progressFill != null)
            progressFill.fillAmount = 0f;

        if (tapButton != null)
        {
            // Back from wherever the banner left it at the end of the last run
            tapButton.gameObject.SetActive(true);
            tapButton.SetArmed(false);
        }
    }

    private void Close()
    {
        ResetVisuals();

        if (backdrop != null)
            backdrop.Clear();

        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    /// <summary>
    /// Silences and blanks everything the panel owns, without touching whether the panel
    /// itself is on — see the note in Awake.
    /// </summary>
    private void ResetVisuals()
    {
        if (tapButton != null)
            tapButton.SetArmed(false);

        if (progressFill != null)
            progressFill.fillAmount = 0f;

        if (completedBanner != null)
            completedBanner.SetActive(false);

        progress = 0f;

        if (panelGroup == null)
            return;

        panelGroup.alpha = 0f;
        panelGroup.blocksRaycasts = false;
    }

    private IEnumerator FadeGroup(CanvasGroup group, float from, float to, float duration)
    {
        if (group == null)
            yield break;

        if (duration <= 0f)
        {
            group.alpha = to;
            yield break;
        }

        group.alpha = from;

        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }

        group.alpha = to;
    }
}

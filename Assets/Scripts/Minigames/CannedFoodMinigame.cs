using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The canned food minigame that runs after the quiz's canned goods question.
///
/// Two beats, in the order an easy-open tin is actually opened:
///
///   1. Peel. The tin starts sealed with its ring pull on top and an arrow pointing down it.
///      One swipe down pulls the lid back, which is the whole of the first gesture — the
///      reference sheet asks for a drag before anything else can happen.
///   2. Shake out. With the lid off, the tin is tapped to empty it. Every
///      <see cref="tapsPerFrame"/> taps shakes out one more portion, stepping the tin through
///      the authored fill stages, and each tap knocks the tin about so the food comes out
///      under the player's own hand rather than on a timer.
///
/// The last stages are not tapped for. Once the tin is full to <see cref="lastTapFrame"/> the
/// remaining frames play themselves off, which is what the sheet marks as animation only.
///
/// Like the other minigames there is nothing to fail. It runs whether the quiz answer was
/// right or wrong, there is no clock, and a swipe that goes the wrong way simply does not
/// count.
///
/// The gestures are the two the rest of the game already owns:
/// <see cref="ThermalBlanketSwipeArea"/> for the peel, which brings the nudge arrow with it,
/// and <see cref="WhistleTapButton"/> for the taps. The tap surface is a child of the swipe
/// area, so whichever of the two is armed is the one the finger lands on; the tin itself is
/// left as pure graphics, free to be shaken without either of them fighting over its
/// transform.
///
/// <see cref="Play"/> is a coroutine so QuizHandler can simply yield on it and carry on with
/// the next question once it returns.
/// </summary>
public class CannedFoodMinigame : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private CanvasGroup panelGroup;
    [Tooltip("Blurs the quiz behind the minigame, the same way the correct / wrong overlay " +
             "does. Optional.")]
    [SerializeField] private ScreenBlurBackdrop backdrop;

    [Header("Instruction")]
    [SerializeField] private CanvasGroup instructionCard;
    [SerializeField] private TextMeshProUGUI instructionLabel;
    [SerializeField, TextArea] private string peelInstruction =
        "Open the emergency can food";
    [Tooltip("Swapped in for the second half, once the lid is off.")]
    [SerializeField, TextArea] private string tapInstruction =
        "Tap the can to shake the food out";

    [Header("Can")]
    [Tooltip("The tin. Only its sprite is stepped through the stages; the rect never changes, " +
             "so the tin fills exactly as it is drawn.")]
    [SerializeField] private Image canImage;
    [Tooltip("The nine stages off CannedFoodSheet, sealed first and emptied last.")]
    [SerializeField] private Sprite[] frames;

    [Header("Gestures")]
    [SerializeField] private ThermalBlanketSwipeArea swipeArea;
    [Tooltip("Where the down arrow rests, in the swipe area's space. Just under the ring " +
             "pull, so it reads as pointing at the lid rather than at the tin.")]
    [SerializeField] private Vector2 arrowRestPosition = new Vector2(0f, -30f);
    [SerializeField] private WhistleTapButton tapButton;

    [Header("Stages")]
    [Tooltip("The stage the swipe peels the lid back to. Everything before it is the sealed " +
             "tin.")]
    [SerializeField] private int openedFrame = 1;
    [Tooltip("The last stage the player taps for. The stages past it play themselves off, " +
             "which is what the reference sheet marks as animation only.")]
    [SerializeField] private int lastTapFrame = 6;
    [Tooltip("Taps needed to shake out each portion.")]
    [SerializeField, Min(1)] private int tapsPerFrame = 2;

    [Header("Shake")]
    [Tooltip("How long the tin rocks for after a tap. Short: at two taps a portion it has to " +
             "settle before the next one lands.")]
    [SerializeField] private float shakeDuration = 0.18f;
    [Tooltip("How far the tin is knocked sideways, in canvas units.")]
    [SerializeField] private float shakeDistance = 16f;
    [Tooltip("How far the tin is rocked over, in degrees.")]
    [SerializeField] private float shakeAngle = 4f;
    [Tooltip("Rocks back and forth this many times over the shake.")]
    [SerializeField, Min(0.5f)] private float shakeCycles = 2f;

    [Header("Timing")]
    [SerializeField] private float panelFadeDuration = 0.25f;
    [SerializeField] private float instructionFadeDuration = 0.3f;
    [Tooltip("Beat between the lid coming off and the taps being taken, so the peel reads " +
             "before the tin is armed again.")]
    [SerializeField] private float peelBeat = 0.35f;
    [Tooltip("How long each of the closing stages is held for.")]
    [SerializeField] private float settleFrameDuration = 0.22f;
    [Tooltip("Pause once the tin is empty, before the minigame closes.")]
    [SerializeField] private float finishDelay = 0.9f;

    [Header("Finish")]
    [Tooltip("Optional. Flashed once the tin is empty.")]
    [SerializeField] private GameObject completedBanner;

    private bool isPlaying = false;

    // Bumped by the swipe area and the tap button; read by the two halves below.
    private bool peeled = false;
    private int tapsTaken = 0;

    // Where the tin sits when it is not being knocked about. Taken once the layout is real,
    // so a shake cut short by the panel closing cannot leave it parked off its mark.
    private Vector2 canHome;
    private bool canHomeCaptured = false;

    private Coroutine shaking;

    public bool IsPlaying { get { return isPlaying; } }

    private void Awake()
    {
        if (panelGroup == null && panelRoot != null)
            panelGroup = panelRoot.GetComponent<CanvasGroup>();

        // Reset the contents but leave the panel's active state alone. Awake first runs during
        // the SetActive in Open, so deactivating here would switch the panel back off
        // underneath the very coroutine that just turned it on.
        ResetVisuals();
    }

    /// <summary>
    /// Runs the whole minigame and returns once it has closed.
    /// Yield on this from the quiz; it never returns early or leaves the panel up.
    /// </summary>
    public IEnumerator Play()
    {
        if (isPlaying)
            yield break;

        if (canImage == null || frames == null || frames.Length < 2 || swipeArea == null
            || tapButton == null)
        {
            // Bail loudly rather than quietly: a half-wired panel would otherwise hang the
            // quiz on a tin that can never be opened.
            Debug.LogWarning("[CannedFoodMinigame] Needs the can image, at least two stages, " +
                             "the swipe area and the tap button — skipping.", this);
            yield break;
        }

        isPlaying = true;

        Open();

        if (backdrop != null)
            yield return StartCoroutine(backdrop.CaptureIncludingUIRoutine());

        yield return FadeGroup(panelGroup, 0f, 1f, panelFadeDuration);
        yield return FadeGroup(instructionCard, 0f, 1f, instructionFadeDuration);

        yield return RunPeel();

        yield return new WaitForSecondsRealtime(peelBeat);

        yield return RunTaps();
        yield return RunSettle();

        if (completedBanner != null)
            completedBanner.SetActive(true);

        yield return new WaitForSecondsRealtime(finishDelay);

        yield return FadeGroup(instructionCard, 1f, 0f, instructionFadeDuration);
        yield return FadeGroup(panelGroup, 1f, 0f, panelFadeDuration);

        if (backdrop != null)
            yield return StartCoroutine(backdrop.FadeOutRoutine());

        Close();
        isPlaying = false;
    }

    // ------------------------------------------------------------------ peel

    /// <summary>
    /// Waits for the lid to be dragged down, and returns with the tin open.
    /// </summary>
    private IEnumerator RunPeel()
    {
        SetInstruction(peelInstruction);
        ShowFrame(0);

        peeled = false;

        swipeArea.Swiped += OnSwiped;
        swipeArea.SetDirection(Vector2.down, arrowRestPosition);
        swipeArea.SetArmed(true);

        while (!peeled)
            yield return null;

        swipeArea.Swiped -= OnSwiped;
        swipeArea.SetArmed(false);

        ShowFrame(openedFrame);
    }

    private void OnSwiped()
    {
        peeled = true;
    }

    // ------------------------------------------------------------------ taps

    /// <summary>
    /// Takes taps until the tin has been shaken out as far as the player is asked to take it.
    /// </summary>
    private IEnumerator RunTaps()
    {
        SetInstruction(tapInstruction);

        tapsTaken = 0;

        tapButton.Tapped += OnTapped;
        tapButton.SetArmed(true);

        while (tapsTaken < TapsNeeded())
            yield return null;

        tapButton.Tapped -= OnTapped;
        tapButton.SetArmed(false);
    }

    private void OnTapped()
    {
        if (tapsTaken >= TapsNeeded())
            return;

        tapsTaken++;

        // Every tap rocks the tin, whether or not it shook a portion loose, so a tap always
        // lands on something.
        StartShake();

        // Integer division on purpose: the stage only moves on once the tin has been struck
        // the full number of times.
        ShowFrame(openedFrame + tapsTaken / tapsPerFrame);
    }

    /// <summary>Taps the whole of the tapped half asks for.</summary>
    private int TapsNeeded()
    {
        int stages = Mathf.Max(0, LastTapFrame() - openedFrame);
        return stages * Mathf.Max(1, tapsPerFrame);
    }

    /// <summary>
    /// The last stage taps carry the tin to, kept inside the stages actually assigned so a
    /// short sheet cannot leave the loop above waiting on a frame that does not exist.
    /// </summary>
    private int LastTapFrame()
    {
        return Mathf.Clamp(lastTapFrame, openedFrame, frames.Length - 1);
    }

    // ------------------------------------------------------------------ settle

    /// <summary>
    /// Plays off the stages past the tapped ones — the two the reference sheet marks as
    /// animation only, where the last of the food tumbles out on its own.
    /// </summary>
    private IEnumerator RunSettle()
    {
        for (int i = LastTapFrame() + 1; i < frames.Length; i++)
        {
            yield return new WaitForSecondsRealtime(settleFrameDuration);
            ShowFrame(i);
        }
    }

    // ------------------------------------------------------------------ shake

    private void StartShake()
    {
        if (!isActiveAndEnabled || canImage == null)
            return;

        // One shake at a time, so a fast mash rocks the tin harder rather than stacking two
        // tweens that fight over where it sits.
        if (shaking != null)
            StopCoroutine(shaking);

        shaking = StartCoroutine(Shake());
    }

    /// <summary>
    /// Rocks the tin and damps back to rest. Unscaled time, so it moves at the same rate
    /// whatever the game's timescale is doing behind the blur.
    /// </summary>
    private IEnumerator Shake()
    {
        RectTransform rect = (RectTransform)canImage.transform;

        for (float t = 0f; t < shakeDuration; t += Time.unscaledDeltaTime)
        {
            float k = shakeDuration <= 0f ? 1f : t / shakeDuration;

            // Fades out over the shake, so the tin settles rather than stopping dead
            float fall = 1f - k;
            float wave = Mathf.Sin(k * shakeCycles * 2f * Mathf.PI) * fall;

            rect.anchoredPosition = canHome + new Vector2(wave * shakeDistance, 0f);
            rect.localRotation = Quaternion.Euler(0f, 0f, wave * shakeAngle);
            yield return null;
        }

        rect.anchoredPosition = canHome;
        rect.localRotation = Quaternion.identity;
        shaking = null;
    }

    // ------------------------------------------------------------------ lifecycle

    /// <summary>Puts a stage on the tin, clamped so a mis-set index cannot blank it.</summary>
    private void ShowFrame(int index)
    {
        if (canImage == null || frames == null || frames.Length == 0)
            return;

        Sprite sprite = frames[Mathf.Clamp(index, 0, frames.Length - 1)];
        if (sprite != null)
            canImage.sprite = sprite;
    }

    private void SetInstruction(string text)
    {
        if (instructionLabel != null && !string.IsNullOrEmpty(text))
            instructionLabel.text = text;
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

        // Where the tin rests is only real once the layout has been built, so it is taken here
        // rather than in Awake — and only once, since a run cut short mid-shake would
        // otherwise record the knocked-about spot as home.
        if (!canHomeCaptured && canImage != null)
        {
            canHome = ((RectTransform)canImage.transform).anchoredPosition;
            canHomeCaptured = true;
        }

        ResetVisuals();
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
    /// Puts the tin back to sealed and both gestures back to inert, without touching whether
    /// the panel itself is on — see the note in Awake.
    /// </summary>
    private void ResetVisuals()
    {
        peeled = false;
        tapsTaken = 0;

        if (shaking != null)
        {
            StopCoroutine(shaking);
            shaking = null;
        }

        if (swipeArea != null)
        {
            swipeArea.Swiped -= OnSwiped;
            swipeArea.SetArmed(false);
        }

        if (tapButton != null)
        {
            tapButton.Tapped -= OnTapped;
            tapButton.SetArmed(false);
        }

        if (canImage != null)
        {
            RectTransform rect = (RectTransform)canImage.transform;

            if (canHomeCaptured)
                rect.anchoredPosition = canHome;

            rect.localRotation = Quaternion.identity;
            ShowFrame(0);
        }

        SetInstruction(peelInstruction);

        if (completedBanner != null)
            completedBanner.SetActive(false);
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

    /// <summary>
    /// Shuts the minigame down mid-play, for when the quiz's minigame timer runs out.
    /// QuizHandler stops the Play coroutine itself; this clears everything it left up.
    /// </summary>
    public void ForceClose()
    {
        StopAllCoroutines();
        Close();
        isPlaying = false;
    }
}

using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The radio minigame that runs after the quiz's radio question.
///
/// One beat: find the station. The player turns the tuning knob, the needle walks across the
/// band, and the trace on the screen answers — a red mess of static out at the edges, and a
/// steady green wave once the needle is sitting on the broadcast. Holding it there for a
/// moment locks it in and the minigame is done, which is the "turn till the signal becomes
/// green" the reference sheet asks for.
///
/// Like the other minigames there is nothing to fail. It runs whether the quiz answer was
/// right or wrong, there is no clock, and turning past the station only means turning back.
///
/// <para>
/// How the artwork lines up. The four radio sprites are all 128x128 with their pieces drawn
/// in place, so they register with each other by simply being stacked at the same size and
/// position — the base, the needle, the glass and the knob each occupy the whole rect and
/// only paint their own corner of it. That is why the needle and the knob are full-size
/// images rather than cropped ones: cropping would scale the artwork instead of moving it.
/// The needle is nudged along x to walk the band, and the knob turns about its own pivot,
/// which is authored over the knob rather than in the middle of the sprite.
/// </para>
///
/// <see cref="Play"/> is a coroutine so QuizManager can simply yield on it and carry on with
/// the next question once it returns.
/// </summary>
public class RadioMinigame : MonoBehaviour
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
    [SerializeField, TextArea] private string instructionText = "Turn till the signal becomes green";

    [Header("Radio")]
    [SerializeField] private RadioTunerDial dial;
    [Tooltip("The needle over the tuning band. Moved along x only; see the note on how the " +
             "sprites line up.")]
    [SerializeField] private RectTransform pointer;
    [Tooltip("Where the needle sits at each end of the band, as an x offset from where the " +
             "sprite draws it. Set so the needle stays inside the printed scale.")]
    [SerializeField] private float pointerMinX = -92.5f;
    [SerializeField] private float pointerMaxX = 87.5f;

    [Header("Signal Trace")]
    [Tooltip("The trace on the screen. Points are in its own rect, so that rect is what sets " +
             "how wide and how tall the wave can be.")]
    [SerializeField] private UILine signalLine;
    [Tooltip("How many points the trace is drawn from. Enough for the static to look ragged " +
             "without the mesh getting silly.")]
    [SerializeField, Range(16, 128)] private int signalResolution = 56;
    [Tooltip("Fraction of the screen's height the tuned wave swings over.")]
    [SerializeField, Range(0.1f, 1f)] private float signalAmplitude = 0.55f;
    [Tooltip("Fraction of the screen's height the static jumps over. Deliberately smaller " +
             "than the wave: a locked station should read as the loudest thing on screen.")]
    [SerializeField, Range(0.05f, 1f)] private float staticAmplitude = 0.32f;
    [Tooltip("Whole waves across the screen once the station is found.")]
    [SerializeField] private float signalCycles = 2.4f;
    [Tooltip("How fast the tuned wave travels across the screen.")]
    [SerializeField] private float signalScrollSpeed = 2.2f;
    [Tooltip("How fast the static churns.")]
    [SerializeField] private float staticChurnSpeed = 11f;

    [Header("Signal Colours")]
    [SerializeField] private Color staticColour = new Color(0.85f, 0.27f, 0.22f, 1f);
    [Tooltip("The halfway colour, so the player can see they are getting warmer before the " +
             "station itself shows up.")]
    [SerializeField] private Color nearColour = new Color(0.95f, 0.78f, 0.25f, 1f);
    [Tooltip("Matches the green already printed on the radio's own band.")]
    [SerializeField] private Color lockedColour = new Color(0.22f, 0.78f, 0.48f, 1f);

    [Header("Tuning")]
    [Tooltip("Where the station sits on the band, 0 at the left end and 1 at the right. " +
             "Picked fresh each run from this range so the answer cannot be memorised.")]
    [SerializeField] private Vector2 stationRange = new Vector2(0.22f, 0.78f);
    [Tooltip("Where the dial starts each run. Kept clear of the station by " +
             "minimumStartDistance, so a run never opens already tuned.")]
    [SerializeField] private Vector2 startRange = new Vector2(0.05f, 0.95f);
    [SerializeField] private float minimumStartDistance = 0.3f;
    [Tooltip("How far either side of the station the signal is heard at all, across the whole " +
             "band. Wider is kinder: at 0.22 roughly a fifth of the dial has something on it.")]
    [SerializeField, Range(0.05f, 0.5f)] private float bandWidth = 0.22f;
    [Tooltip("How clean the signal has to be to count as tuned, 0 to 1. The trace is well " +
             "into green by here, so what the player sees and what the game counts agree.")]
    [SerializeField, Range(0.5f, 1f)] private float lockThreshold = 0.82f;
    [Tooltip("How long the needle has to stay on the station before it locks. Long enough " +
             "that sweeping straight past does not win by accident.")]
    [SerializeField] private float holdDuration = 1.1f;

    [Header("Timing")]
    [SerializeField] private float panelFadeDuration = 0.25f;
    [SerializeField] private float instructionFadeDuration = 0.3f;
    [Tooltip("How long the trace takes to settle into a clean wave once the station locks.")]
    [SerializeField] private float lockSettleDuration = 0.5f;
    [Tooltip("Pause once the station is locked, before the minigame closes.")]
    [SerializeField] private float finishDelay = 0.9f;

    [Header("Finish")]
    [Tooltip("Optional. Flashed once the station is locked.")]
    [SerializeField] private GameObject completedBanner;

    private bool isPlaying = false;

    // Where the station is this run, 0-1 across the band
    private float station = 0.5f;

    // How clean the trace is right now, 0 static and 1 locked. Held between frames because
    // both the drawing and the hold timer read it.
    private float quality = 0f;

    // Runs while the needle is on the station and resets the moment it leaves
    private float holdTimer = 0f;

    // Fixed for the run so the static does not jump the moment the wave is redrawn
    private float staticSeed = 0f;

    private readonly List<Vector2> tracePoints = new List<Vector2>();

    public bool IsPlaying { get { return isPlaying; } }

    private void Awake()
    {
        if (panelGroup == null && panelRoot != null)
            panelGroup = panelRoot.GetComponent<CanvasGroup>();

        if (instructionLabel != null && !string.IsNullOrEmpty(instructionText))
            instructionLabel.text = instructionText;

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

        if (dial == null || signalLine == null)
        {
            // Bail loudly rather than quietly: a half-wired panel would otherwise hang the
            // quiz on a station that can never be found.
            Debug.LogWarning("[RadioMinigame] Needs the tuning dial and the signal trace — " +
                             "skipping.", this);
            yield break;
        }

        isPlaying = true;

        Open();

        if (backdrop != null)
            yield return StartCoroutine(backdrop.CaptureIncludingUIRoutine());

        yield return FadeGroup(panelGroup, 0f, 1f, panelFadeDuration);
        yield return FadeGroup(instructionCard, 0f, 1f, instructionFadeDuration);

        dial.SetArmed(true);

        yield return RunTuning();

        dial.SetArmed(false);

        yield return SettleOnStation();

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

    // ------------------------------------------------------------------ tuning

    /// <summary>
    /// Redraws the band and the trace every frame and returns once the needle has been held
    /// on the station long enough to lock.
    /// </summary>
    private IEnumerator RunTuning()
    {
        while (true)
        {
            float tuning = dial.Normalised;

            MovePointer(tuning);

            quality = QualityAt(tuning);

            if (quality >= lockThreshold)
            {
                // Unscaled throughout, like every other minigame: the quiz pauses the game
                // behind these panels and a hold timer on scaled time would never finish.
                holdTimer += Time.unscaledDeltaTime;
                if (holdTimer >= holdDuration)
                    break;
            }
            else
            {
                holdTimer = 0f;
            }

            DrawTrace(quality);

            yield return null;
        }
    }

    /// <summary>
    /// How clean the signal is at a point on the band, 0 out in the static and 1 right on the
    /// station.
    ///
    /// Eased rather than a straight ramp, so the last stretch onto the station is the part
    /// that visibly changes. A linear falloff had the trace looking half-tuned across most of
    /// the dial, which gave the player nothing to aim at.
    /// </summary>
    private float QualityAt(float tuning)
    {
        if (bandWidth <= 0f)
            return Mathf.Approximately(tuning, station) ? 1f : 0f;

        float distance = Mathf.Abs(tuning - station) / bandWidth;
        if (distance >= 1f)
            return 0f;

        float closeness = 1f - distance;
        return closeness * closeness;
    }

    private void MovePointer(float tuning)
    {
        if (pointer == null)
            return;

        Vector2 p = pointer.anchoredPosition;
        p.x = Mathf.Lerp(pointerMinX, pointerMaxX, Mathf.Clamp01(tuning));
        pointer.anchoredPosition = p;
    }

    // ------------------------------------------------------------------ the trace

    /// <summary>
    /// Draws one frame of the trace at the given signal quality.
    ///
    /// The wave and the static are both always there and simply trade places: the clean sine
    /// grows as the station comes in while the noise dies away, so there is never a moment
    /// where the screen swaps one picture for another. The colour crosses red to amber to
    /// green over the same range, which is the whole instruction the player is given.
    /// </summary>
    private void DrawTrace(float q)
    {
        if (signalLine == null)
            return;

        RectTransform rect = (RectTransform)signalLine.transform;
        float halfWidth = rect.rect.width * 0.5f;
        float height = rect.rect.height;

        int count = Mathf.Max(2, signalResolution);
        float time = Time.unscaledTime;

        tracePoints.Clear();

        for (int i = 0; i < count; i++)
        {
            float k = i / (float)(count - 1);
            float x = Mathf.Lerp(-halfWidth, halfWidth, k);

            float wave = Mathf.Sin((k * signalCycles * Mathf.PI * 2f) - time * signalScrollSpeed)
                         * (height * 0.5f * signalAmplitude) * q;

            // Perlin rather than plain random: sampled along the trace it gives static that
            // holds together as a ragged line instead of scattering into confetti.
            float noise = (Mathf.PerlinNoise(staticSeed + i * 0.85f, time * staticChurnSpeed) - 0.5f)
                          * 2f * (height * 0.5f * staticAmplitude) * (1f - q);

            tracePoints.Add(new Vector2(x, Mathf.Clamp(wave + noise, -height * 0.5f, height * 0.5f)));
        }

        signalLine.SetPoints(tracePoints);
        signalLine.color = ColourFor(q);
    }

    /// <summary>Red out in the static, amber on the way in, green on the station.</summary>
    private Color ColourFor(float q)
    {
        return q < 0.5f
            ? Color.Lerp(staticColour, nearColour, q * 2f)
            : Color.Lerp(nearColour, lockedColour, (q - 0.5f) * 2f);
    }

    /// <summary>
    /// Carries the trace the last of the way to a perfectly clean wave once the station has
    /// locked. Whatever quality the player actually stopped at, the finish is always the same
    /// full green signal — the station is found, and the screen should say so plainly.
    /// </summary>
    private IEnumerator SettleOnStation()
    {
        float from = quality;

        for (float t = 0f; t < lockSettleDuration; t += Time.unscaledDeltaTime)
        {
            quality = Mathf.Lerp(from, 1f, Mathf.Clamp01(t / lockSettleDuration));
            DrawTrace(quality);
            yield return null;
        }

        quality = 1f;
        DrawTrace(1f);
    }

    // ------------------------------------------------------------------ lifecycle

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

        ResetVisuals();

        // The dial has to be told which canvas it is on before it can turn a touch into a
        // point on the knob, and the panel is only guaranteed to be under one by now.
        if (dial != null)
            dial.Configure(GetComponentInParent<Canvas>());

        PickStation();
    }

    /// <summary>
    /// Puts the station somewhere new and starts the dial well away from it, so a run never
    /// opens already tuned and the answer cannot be carried over from the last one.
    /// </summary>
    private void PickStation()
    {
        station = Random.Range(Mathf.Min(stationRange.x, stationRange.y),
                               Mathf.Max(stationRange.x, stationRange.y));

        float low = Mathf.Min(startRange.x, startRange.y);
        float high = Mathf.Max(startRange.x, startRange.y);

        // Whichever end is further from the station has room for a start; taking the far half
        // of it keeps the opening sweep worth making without ever being the whole dial.
        float start = station - low > high - station
            ? Random.Range(low, Mathf.Max(low, station - minimumStartDistance))
            : Random.Range(Mathf.Min(high, station + minimumStartDistance), high);

        staticSeed = Random.Range(0f, 100f);

        if (dial != null)
            dial.SetNormalised(start);

        MovePointer(start);

        quality = QualityAt(start);
        DrawTrace(quality);
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
    /// Puts the radio back to how a run starts, without touching whether the panel itself is
    /// on — see the note in Awake.
    /// </summary>
    private void ResetVisuals()
    {
        holdTimer = 0f;
        quality = 0f;

        if (dial != null)
            dial.SetArmed(false);

        if (signalLine != null)
            signalLine.Clear();

        if (completedBanner != null)
            completedBanner.SetActive(false);

        if (instructionLabel != null && !string.IsNullOrEmpty(instructionText))
            instructionLabel.text = instructionText;
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
    /// QuizManager stops the Play coroutine itself; this clears everything it left up.
    /// </summary>
    public void ForceClose()
    {
        StopAllCoroutines();
        Close();
        isPlaying = false;
    }
}

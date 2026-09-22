using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The flashlight minigame that runs after the quiz's flashlight question.
///
/// The room goes black and four people are scattered around it. Clicking the torch opens
/// the beam in the middle of the screen; from there the player drags it around, and
/// holding it on someone brings them from a smudge up to fully lit. Four held, and the
/// round is over.
///
/// Like the other two there is nothing to fail here. It runs whether the quiz answer was
/// right or wrong, there is no clock, and letting go only drains the person being held
/// rather than ending anything.
///
/// <see cref="Play"/> is a coroutine so QuizManager can simply yield on it and carry on
/// with the next question once it returns.
/// </summary>
public class FlashlightMinigame : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private CanvasGroup panelGroup;
    [Tooltip("The blacked-out room. Solid rather than a blur of the quiz — the darkness " +
             "is the minigame, so there is nothing behind it to show.")]
    [SerializeField] private Image darkness;

    [Header("Instruction")]
    [SerializeField] private CanvasGroup instructionCard;
    [SerializeField] private TextMeshProUGUI instructionLabel;
    [SerializeField, TextArea] private string instructionText =
        "Keep Flashing the light till you find every person\nDRAG & HOLD the light till the circle is full";

    [Header("Torch")]
    [Tooltip("Clicked once to open the beam. Hidden for the rest of the round — by then " +
             "the light itself is the thing being handled.")]
    [SerializeField] private Button flashlightButton;
    [SerializeField] private FlashlightBeam beam;

    [Header("People")]
    [Tooltip("Scattered to fresh spots every run. Four to match the instruction card.")]
    [SerializeField] private FlashlightTarget[] targets;
    [Tooltip("Sits under the dark sheet. People start here, genuinely covered up.")]
    [SerializeField] private RectTransform hiddenLayer;
    [Tooltip("Sits above the dark sheet. A person is moved here once found, which is what " +
             "keeps them lit after the beam has moved on.")]
    [SerializeField] private RectTransform foundLayer;
    [Tooltip("Keeps people off the edges, the instruction card and the torch, in canvas " +
             "units from the middle outwards.")]
    [SerializeField] private Vector2 scatterHalfExtents = new Vector2(500f, 220f);
    [Tooltip("Smallest gap allowed between two people, so two never overlap into one blob.")]
    [SerializeField] private float minSeparation = 240f;

    [Header("Holding")]
    [Tooltip("Seconds of holding still on someone to bring them fully into the light.")]
    [SerializeField] private float holdSeconds = 1.2f;
    [Tooltip("Seconds for an abandoned hold to drain back to nothing. Leaving costs " +
             "progress but never all of it at once.")]
    [SerializeField] private float drainSeconds = 1.8f;
    [Tooltip("Forgiveness on the whole-body rule, in canvas units. 0 means every corner of " +
             "the person must be inside the beam; raise it to accept a hair over the edge.")]
    [SerializeField] private float containmentTolerance = 0f;

    [Header("Counter")]
    [SerializeField] private TextMeshProUGUI foundCounter;
    [SerializeField] private string foundCounterFormat = "{0} / {1} FOUND";

    [Header("Timing")]
    [SerializeField] private float panelFadeDuration = 0.25f;
    [SerializeField] private float instructionFadeDuration = 0.3f;
    [Tooltip("Pause once the last person is found, before the minigame closes.")]
    [SerializeField] private float finishDelay = 0.9f;

    [Header("Finish")]
    [Tooltip("Optional. Flashed once everyone has been found.")]
    [SerializeField] private GameObject completedBanner;

    [Header("Sound")]
    [Tooltip("As each person comes fully into the light.")]
    [SerializeField] private AudioClip foundSFX;

    private bool isPlaying = false;
    private bool torchOpened = false;
    private int foundCount = 0;

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
        if (flashlightButton != null)
            flashlightButton.onClick.AddListener(OpenTorch);
    }

    private void OnDisable()
    {
        if (flashlightButton != null)
            flashlightButton.onClick.RemoveListener(OpenTorch);
    }

    /// <summary>
    /// Runs the whole minigame and returns once it has closed.
    /// Yield on this from the quiz; it never returns early or leaves the panel up.
    /// </summary>
    public IEnumerator Play()
    {
        if (isPlaying)
            yield break;

        if (beam == null || targets == null || targets.Length == 0)
        {
            // Bail loudly rather than quietly: a half-wired panel would otherwise hang the
            // quiz in a dark room with nobody in it.
            Debug.LogWarning("[FlashlightMinigame] Needs a beam and at least one person to " +
                             "find — skipping.", this);
            yield break;
        }

        isPlaying = true;

        Open();

        yield return FadeGroup(panelGroup, 0f, 1f, panelFadeDuration);
        yield return FadeGroup(instructionCard, 0f, 1f, instructionFadeDuration);

        // Nothing to search until the torch is on, so the round proper starts there
        while (!torchOpened)
            yield return null;

        while (foundCount < targets.Length)
        {
            Sweep();
            yield return null;
        }

        beam.SetOn(false);

        if (completedBanner != null)
            completedBanner.SetActive(true);

        yield return new WaitForSecondsRealtime(finishDelay);

        yield return FadeGroup(instructionCard, 1f, 0f, instructionFadeDuration);
        yield return FadeGroup(panelGroup, 1f, 0f, panelFadeDuration);

        Close();
        isPlaying = false;
    }

    /// <summary>
    /// One frame of searching: works out who the beam is on, moves that person's hold
    /// along and everyone else's back, and keeps the beam's own yellow in step.
    /// </summary>
    private void Sweep()
    {
        float fillRate = holdSeconds > 0f ? 1f / holdSeconds : 1f;
        float drainRate = drainSeconds > 0f ? 1f / drainSeconds : 1f;
        float dt = Time.unscaledDeltaTime;

        // Only the nearest person inside the beam counts. Without that, two people close
        // together would both fill from one hold, which reads as a bug rather than a break.
        FlashlightTarget lit = null;
        float nearest = float.MaxValue;

        if (beam.IsHeld)
        {
            foreach (FlashlightTarget target in targets)
            {
                if (target == null || target.IsFound)
                    continue;

                // The whole person has to be inside the light, not just clipped by its edge
                // — catching somebody by the top of their head should not count as finding
                // them. The test has to match the shape the player can actually see: a
                // round check against a square pool would refuse people standing plainly
                // lit in its corners, and read as the light not working.
                Vector2 delta = target.Rect.anchoredPosition - beam.Position;
                float distance;
                bool inside;

                if (beam.IsSquare)
                {
                    // Square pool: inside on both axes, measured to the sides of the sprite.
                    Vector2 half = target.Rect.rect.size * 0.5f;
                    float reachX = beam.LightRadius - half.x + containmentTolerance;
                    float reachY = beam.LightRadius - half.y + containmentTolerance;

                    inside = Mathf.Abs(delta.x) <= reachX && Mathf.Abs(delta.y) <= reachY;
                    distance = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
                }
                else
                {
                    // Round beam: measured to the far corner of the sprite, so the light
                    // has to sit over all of them before the hold starts.
                    float halfSpan = target.Rect.rect.size.magnitude * 0.5f;
                    float reach = beam.LightRadius - halfSpan + containmentTolerance;

                    distance = delta.magnitude;
                    inside = distance <= reach;
                }

                if (inside && distance < nearest)
                {
                    nearest = distance;
                    lit = target;
                }
            }
        }

        foreach (FlashlightTarget target in targets)
        {
            if (target == null)
                continue;

            bool wasFound = target.IsFound;
            target.Tick(target == lit, fillRate, drainRate, dt);

            if (!wasFound && target.IsFound)
                OnTargetFound(target);
        }

        // The beam shows whoever it is currently on, and empties out over open floor
        beam.SetFocus(lit != null ? lit.Progress : 0f);
    }

    private void OnTargetFound(FlashlightTarget target)
    {
        foundCount++;
        UpdateCounter();
        SoundManager.Sfx(foundSFX);

        // Out from under the dark sheet, so the beam moving on no longer covers them back
        // up. World position is kept so they do not jump on the way across.
        if (foundLayer != null && target != null)
            target.Rect.SetParent(foundLayer, true);
    }

    private void OpenTorch()
    {
        if (torchOpened || !isPlaying)
            return;

        torchOpened = true;

        if (flashlightButton != null)
            flashlightButton.gameObject.SetActive(false);

        beam.SetOn(true);
    }

    /// <summary>
    /// Drops everyone somewhere new inside the allowed area, spread far enough apart that
    /// one sweep of the beam never covers two of them.
    /// </summary>
    private void Scatter()
    {
        List<Vector2> placed = new List<Vector2>();

        foreach (FlashlightTarget target in targets)
        {
            if (target == null)
                continue;

            Vector2 spot = Vector2.zero;

            // Rejection sampling, with a cap: on a crowded area the last few tries would
            // otherwise spin forever, and a slightly tight pair beats a frozen minigame.
            for (int attempt = 0; attempt < 40; attempt++)
            {
                spot = new Vector2(
                    Random.Range(-scatterHalfExtents.x, scatterHalfExtents.x),
                    Random.Range(-scatterHalfExtents.y, scatterHalfExtents.y));

                bool clear = true;
                foreach (Vector2 other in placed)
                {
                    if (Vector2.Distance(spot, other) < minSeparation)
                    {
                        clear = false;
                        break;
                    }
                }

                if (clear)
                    break;
            }

            placed.Add(spot);

            // Back under the dark sheet before being placed, or anyone found last run
            // would still be sitting on the layer above it.
            if (hiddenLayer != null && target.Rect.parent != hiddenLayer)
                target.Rect.SetParent(hiddenLayer, false);

            target.Rect.anchoredPosition = spot;
            target.ResetTarget();
        }
    }

    private void UpdateCounter()
    {
        if (foundCounter != null)
            foundCounter.text = string.Format(foundCounterFormat, foundCount, targets.Length);
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

        torchOpened = false;
        foundCount = 0;

        if (flashlightButton != null)
            flashlightButton.gameObject.SetActive(true);

        if (beam != null)
            beam.SetOn(false);

        Scatter();
        UpdateCounter();
    }

    private void Close()
    {
        ResetVisuals();

        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    /// <summary>
    /// Puts everyone back in the dark and the torch back in its holster, without touching
    /// whether the panel itself is on — see the note in Awake.
    /// </summary>
    private void ResetVisuals()
    {
        torchOpened = false;
        foundCount = 0;

        if (beam != null)
            beam.SetOn(false);

        if (flashlightButton != null)
            flashlightButton.gameObject.SetActive(true);

        if (targets != null)
        {
            foreach (FlashlightTarget target in targets)
            {
                if (target != null)
                    target.ResetTarget();
            }
        }

        if (completedBanner != null)
            completedBanner.SetActive(false);

        UpdateCounter();

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

using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The knot minigame that runs after the quiz's rope question.
///
/// A figure-of-eight guide sits over the rope with a highlight travelling round it, showing
/// the way. The player holds a finger down and traces the loop; checkpoints along the path
/// have to be reached in order, and straying too far hands the stroke back to the start.
/// Reaching the last one cinches the knot.
///
/// Like the other minigames there is nothing to fail. Wandering off only resets the stroke;
/// the round waits for the knot rather than grading it, because the question has already
/// been scored by the time this runs.
///
/// The path is not authored here. It is read off the guide art itself — see
/// <see cref="pathPointsNormalised"/> — so the checkpoints always sit on the drawn line.
///
/// <see cref="Play"/> is a coroutine so QuizManager can simply yield on it and carry on with
/// the next question once it returns.
/// </summary>
public class RopeKnotMinigame : MonoBehaviour
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
    [SerializeField, TextArea] private string instructionText = "Trace the rope to tie a knot";

    [Header("Guide")]
    [Tooltip("Shows the figure-of-eight. Its sprite is swapped through guideFrames to run " +
             "the travelling highlight.")]
    [SerializeField] private Image guideImage;
    [Tooltip("The 27 frames sliced off Guideline-Sheet, in order.")]
    [SerializeField] private Sprite[] guideFrames;
    [SerializeField] private float guideFps = 18f;

    [Header("Tracing")]
    [Tooltip("The rect the guide fills. Checkpoints and the traced stroke are placed in here.")]
    [SerializeField] private RectTransform pathArea;
    [SerializeField] private KnotTraceArea traceArea;
    [SerializeField] private UILine traceLine;
    [Tooltip("Marker shown at each checkpoint. One is cloned per checkpoint.")]
    [SerializeField] private Image checkpointTemplate;
    [Tooltip("How many checkpoints to place around the loop. The design calls for 8 to 12.")]
    [SerializeField, Range(6, 27)] private int checkpointCount = 12;
    [Tooltip("How near the pointer must come to a checkpoint to claim it, in path-rect units.")]
    [SerializeField] private float checkpointRadius = 34f;
    [Tooltip("How far off the drawn line the pointer may stray before the stroke is reset.")]
    [SerializeField] private float strayTolerance = 46f;

    [Header("Colours")]
    [SerializeField] private Color pendingColor = new Color(1f, 1f, 1f, 0.35f);
    [SerializeField] private Color nextColor = new Color(1f, 0.83f, 0.25f, 1f);
    [SerializeField] private Color reachedColor = new Color(0.42f, 0.85f, 0.35f, 1f);

    [Header("Timing")]
    [SerializeField] private float panelFadeDuration = 0.25f;
    [SerializeField] private float instructionFadeDuration = 0.3f;
    [Tooltip("Pause once the knot cinches, before the minigame closes.")]
    [SerializeField] private float finishDelay = 0.9f;

    [Header("Sound")]
    [Tooltip("Loops while the finger is tracing the knot.")]
    [SerializeField] private AudioClip traceLoopSFX;
    [Tooltip("As the knot is pulled tight.")]
    [SerializeField] private AudioClip cinchSFX;

    private SfxLoop traceLoop;

    [Header("Finish")]
    [Tooltip("Optional. Flashed once the knot is tied.")]
    [SerializeField] private GameObject completedBanner;

    private bool isPlaying = false;
    private bool knotTied = false;
    private int nextCheckpoint = 0;

    private readonly List<Vector2> pathPoints = new List<Vector2>();      // in pathArea space
    private readonly List<Vector2> checkpoints = new List<Vector2>();
    private readonly List<Image> checkpointMarkers = new List<Image>();
    private readonly List<Vector2> stroke = new List<Vector2>();

    public bool IsPlaying { get { return isPlaying; } }

    /// <summary>
    /// The figure-of-eight, as fractions of the guide sprite (0-1 across, 0-1 up), read off
    /// the travelling highlight in Guideline-Sheet frame by frame. Taken from the art rather
    /// than written by hand so the checkpoints can never drift off the drawn line.
    /// </summary>
    private static readonly Vector2[] pathPointsNormalised =
    {
        new Vector2(0.153f, 0.373f), new Vector2(0.215f, 0.286f), new Vector2(0.278f, 0.252f),
        new Vector2(0.339f, 0.252f), new Vector2(0.402f, 0.286f), new Vector2(0.464f, 0.373f),
        new Vector2(0.528f, 0.611f), new Vector2(0.590f, 0.698f), new Vector2(0.653f, 0.733f),
        new Vector2(0.714f, 0.733f), new Vector2(0.777f, 0.698f), new Vector2(0.839f, 0.611f),
        new Vector2(0.867f, 0.492f), new Vector2(0.839f, 0.373f), new Vector2(0.777f, 0.286f),
        new Vector2(0.714f, 0.252f), new Vector2(0.653f, 0.252f), new Vector2(0.590f, 0.286f),
        new Vector2(0.528f, 0.373f), new Vector2(0.496f, 0.492f), new Vector2(0.464f, 0.611f),
        new Vector2(0.402f, 0.698f), new Vector2(0.339f, 0.733f), new Vector2(0.278f, 0.733f),
        new Vector2(0.215f, 0.698f), new Vector2(0.153f, 0.611f), new Vector2(0.125f, 0.492f)
    };

    private void Awake()
    {
        if (panelGroup == null && panelRoot != null)
            panelGroup = panelRoot.GetComponent<CanvasGroup>();

        traceLoop = SfxLoop.Create(gameObject, traceLoopSFX);

        if (instructionLabel != null && !string.IsNullOrEmpty(instructionText))
            instructionLabel.text = instructionText;

        // Reset the contents but leave the panel's active state alone. Awake first runs
        // during the SetActive in Open, so deactivating here would switch the panel back off
        // underneath the very coroutine that just turned it on.
        ResetVisuals();
    }

    private void OnEnable()
    {
        if (traceArea != null)
        {
            traceArea.Pressed += OnPressed;
            traceArea.Moved += OnMoved;
            traceArea.Released += OnReleased;
        }
    }

    private void OnDisable()
    {
        if (traceArea != null)
        {
            traceArea.Pressed -= OnPressed;
            traceArea.Moved -= OnMoved;
            traceArea.Released -= OnReleased;
        }
    }

    /// <summary>
    /// Runs the whole minigame and returns once it has closed.
    /// Yield on this from the quiz; it never returns early or leaves the panel up.
    /// </summary>
    public IEnumerator Play()
    {
        if (isPlaying)
            yield break;

        if (traceArea == null || traceLine == null || pathArea == null)
        {
            // Bail loudly rather than quietly: a half-wired panel would otherwise hang the
            // quiz on a knot that can never be tied.
            Debug.LogWarning("[RopeKnotMinigame] Needs a path area, a trace area and a line — skipping.", this);
            yield break;
        }

        isPlaying = true;

        Open();

        if (backdrop != null)
            yield return StartCoroutine(backdrop.CaptureIncludingUIRoutine());

        yield return FadeGroup(panelGroup, 0f, 1f, panelFadeDuration);
        yield return FadeGroup(instructionCard, 0f, 1f, instructionFadeDuration);

        traceArea.SetArmed(true);

        Coroutine guide = StartCoroutine(RunGuideAnimation());

        while (!knotTied)
            yield return null;

        traceArea.SetArmed(false);

        if (guide != null)
            StopCoroutine(guide);

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

    // ------------------------------------------------------------------ tracing

    private void OnPressed(Vector2 p)
    {
        if (!isPlaying || knotTied)
            return;

        // The stroke may only begin on the first checkpoint, so the knot is always tied in
        // the same direction rather than from wherever the finger happens to land.
        if (checkpoints.Count > 0 && Vector2.Distance(p, checkpoints[0]) > checkpointRadius)
        {
            ResetStroke();
            return;
        }

        stroke.Clear();
        stroke.Add(p);
        traceLine.SetPoints(stroke);

        nextCheckpoint = 1;
        RefreshMarkers();
    }

    private void OnMoved(Vector2 p)
    {
        if (!isPlaying || knotTied || stroke.Count == 0)
            return;

        // Wandering off the drawn line hands the stroke back rather than failing the round
        if (DistanceToPath(p) > strayTolerance)
        {
            ResetStroke();
            return;
        }

        stroke.Add(p);
        traceLine.AddPoint(p);

        if (traceLoop != null)
            traceLoop.Hold();

        if (nextCheckpoint < checkpoints.Count &&
            Vector2.Distance(p, checkpoints[nextCheckpoint]) <= checkpointRadius)
        {
            nextCheckpoint++;
            RefreshMarkers();

            if (nextCheckpoint >= checkpoints.Count)
                Cinch();
        }
    }

    private void OnReleased()
    {
        // Letting go before the end is not a failure, just a fresh start
        if (isPlaying && !knotTied)
            ResetStroke();
    }

    private void Cinch()
    {
        knotTied = true;
        SoundManager.Sfx(cinchSFX);

        // Close the loop so the finished knot reads as one continuous line
        if (checkpoints.Count > 0)
        {
            stroke.Add(checkpoints[0]);
            traceLine.SetPoints(stroke);
        }

        traceLine.color = reachedColor;
        RefreshMarkers();
    }

    private void ResetStroke()
    {
        stroke.Clear();
        traceLine.Clear();
        nextCheckpoint = 0;
        RefreshMarkers();
    }

    /// <summary>
    /// Shortest distance from a point to the guide path, measured against the segments
    /// between path points rather than the points alone — otherwise the gaps between them
    /// would read as "off the line".
    /// </summary>
    private float DistanceToPath(Vector2 p)
    {
        float best = float.MaxValue;

        for (int i = 0; i < pathPoints.Count; i++)
        {
            Vector2 a = pathPoints[i];
            Vector2 b = pathPoints[(i + 1) % pathPoints.Count];
            best = Mathf.Min(best, DistanceToSegment(p, a, b));
        }

        return best;
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lenSq = ab.sqrMagnitude;
        if (lenSq < 0.0001f)
            return Vector2.Distance(p, a);

        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
        return Vector2.Distance(p, a + ab * t);
    }

    // ------------------------------------------------------------------ setup

    /// <summary>
    /// Places the path and the checkpoints to match however large the guide is drawn.
    /// </summary>
    private void BuildPath()
    {
        pathPoints.Clear();
        checkpoints.Clear();

        Vector2 size = pathArea.rect.size;

        foreach (Vector2 n in pathPointsNormalised)
            pathPoints.Add(new Vector2((n.x - 0.5f) * size.x, (n.y - 0.5f) * size.y));

        int count = Mathf.Clamp(checkpointCount, 3, pathPoints.Count);
        for (int i = 0; i < count; i++)
        {
            int index = Mathf.RoundToInt(i * (pathPoints.Count / (float)count));
            checkpoints.Add(pathPoints[Mathf.Min(index, pathPoints.Count - 1)]);
        }

        BuildMarkers();
    }

    private void BuildMarkers()
    {
        foreach (Image m in checkpointMarkers)
        {
            if (m == null)
                continue;

            // Editor tooling builds the path outside play mode, where Destroy is refused
            if (Application.isPlaying)
                Destroy(m.gameObject);
            else
                DestroyImmediate(m.gameObject);
        }
        checkpointMarkers.Clear();

        if (checkpointTemplate == null)
            return;

        checkpointTemplate.gameObject.SetActive(false);

        for (int i = 0; i < checkpoints.Count; i++)
        {
            Image marker = Instantiate(checkpointTemplate, checkpointTemplate.transform.parent);
            marker.gameObject.name = "Checkpoint_" + i.ToString("00");
            marker.gameObject.SetActive(true);
            ((RectTransform)marker.transform).anchoredPosition = checkpoints[i];
            checkpointMarkers.Add(marker);
        }

        RefreshMarkers();
    }

    private void RefreshMarkers()
    {
        for (int i = 0; i < checkpointMarkers.Count; i++)
        {
            if (checkpointMarkers[i] == null)
                continue;

            checkpointMarkers[i].color =
                knotTied || i < nextCheckpoint ? reachedColor :
                i == nextCheckpoint ? nextColor : pendingColor;
        }

        // Before the stroke starts, the first checkpoint is the one to press
        if (nextCheckpoint == 0 && checkpointMarkers.Count > 0 && checkpointMarkers[0] != null && !knotTied)
            checkpointMarkers[0].color = nextColor;
    }

    private IEnumerator RunGuideAnimation()
    {
        if (guideImage == null || guideFrames == null || guideFrames.Length == 0)
            yield break;

        float step = 1f / Mathf.Max(1f, guideFps);
        int frame = 0;

        while (true)
        {
            guideImage.sprite = guideFrames[frame % guideFrames.Length];
            frame++;
            yield return new WaitForSecondsRealtime(step);
        }
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

        if (completedBanner != null)
            completedBanner.SetActive(false);

        knotTied = false;
        nextCheckpoint = 0;

        // Sizes are only real once the layout has been built, so the path is laid out here
        // rather than in Awake.
        Canvas.ForceUpdateCanvases();
        BuildPath();

        traceArea.Configure(pathArea, panelRoot != null ? panelRoot.GetComponentInParent<Canvas>() : null);
        traceArea.SetArmed(false);

        traceLine.color = nextColor;
        ResetStroke();
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
        if (traceArea != null)
            traceArea.SetArmed(false);

        if (traceLine != null)
            traceLine.Clear();

        if (completedBanner != null)
            completedBanner.SetActive(false);

        stroke.Clear();
        knotTied = false;
        nextCheckpoint = 0;

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

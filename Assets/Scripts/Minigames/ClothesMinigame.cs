using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The spare clothes minigame that runs after the quiz's spare clothes question.
///
/// The shirt is laid out flat and folded the way it would be packed, one swipe per fold,
/// following the arrow each time:
///
///   1. Swipe right — the left sleeve folds in.
///   2. Swipe left  — the right sleeve folds in.
///   3. Swipe up    — the bottom half folds up.
///   4. Swipe round in a circle — the folded shirt is turned over, collar side up.
///
/// The five authored stages share one rect, so the shirt only ever swaps sprites and stays in
/// register. Like the other minigames there is nothing to fail: it runs whether the quiz
/// answer was right or wrong, there is no clock, and a swipe the wrong way does nothing.
///
/// <see cref="Play"/> is a coroutine so QuizHandler can simply yield on it and carry on with
/// the next question once it returns.
/// </summary>
public class ClothesMinigame : MonoBehaviour
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
    [SerializeField, TextArea]
    private string instruction = "Fold the clothes by swiping your finger at the right direction";

    [Header("Shirt")]
    [Tooltip("The shirt. Its sprite is stepped through the fold stages as the player swipes.")]
    [SerializeField] private Image shirtImage;
    [Tooltip("The five fold stages: laid flat first, folded and turned over last.")]
    [SerializeField] private Sprite[] foldStages;
    [SerializeField] private ClothesSwipeArea swipeArea;

    [Header("Straight folds")]
    [Tooltip("Which way each straight fold goes, in order. The fold stages left after these " +
             "are turned over with a circular swipe.")]
    [SerializeField]
    private Vector2[] swipeDirections = new Vector2[]
    {
        Vector2.right,
        Vector2.left,
        Vector2.up
    };
    [Tooltip("Where the arrow sits for each straight fold, in the swipe area's space. Matched " +
             "to swipeDirections; a short list reuses its last entry.")]
    [SerializeField]
    private Vector2[] arrowPositions = new Vector2[]
    {
        new Vector2(-52f,   75f),
        new Vector2(346f,   75f),
        new Vector2(156f, -163f)
    };

    [Header("Turn over")]
    [Tooltip("Where the rotate arrow sits for the circular swipe, in the swipe area's space.")]
    [SerializeField] private Vector2 rotateArrowPosition = new Vector2(150f, 82f);
    [Tooltip("How long the shirt takes to flip over once the circle is drawn.")]
    [SerializeField] private float flipDuration = 0.3f;

    [Header("Timing")]
    [SerializeField] private float panelFadeDuration = 0.25f;
    [SerializeField] private float instructionFadeDuration = 0.3f;
    [Tooltip("Pause once the shirt is folded, before the minigame closes.")]
    [SerializeField] private float finishDelay = 0.9f;

    [Header("Finish")]
    [Tooltip("Optional. Flashed once the shirt is folded.")]
    [SerializeField] private GameObject completedBanner;

    private bool isPlaying = false;

    // Bumped by the swipe area; read by the fold loop.
    private int foldsDone = 0;
    private bool flipping = false;

    public bool IsPlaying { get { return isPlaying; } }

    private int FoldsNeeded { get { return foldStages.Length - 1; } }

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

        if (shirtImage == null || swipeArea == null || foldStages == null || foldStages.Length < 2)
        {
            // Bail loudly rather than quietly: a half-wired panel would otherwise hang the
            // quiz on a shirt that can never be folded.
            Debug.LogWarning("[ClothesMinigame] Needs the shirt image, the swipe area and at " +
                             "least two fold stages — skipping.", this);
            yield break;
        }

        isPlaying = true;

        Open();

        if (backdrop != null)
            yield return StartCoroutine(backdrop.CaptureIncludingUIRoutine());

        yield return FadeGroup(panelGroup, 0f, 1f, panelFadeDuration);
        yield return FadeGroup(instructionCard, 0f, 1f, instructionFadeDuration);

        yield return RunFolds();

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

    // ------------------------------------------------------------------ folds

    /// <summary>
    /// Steps the shirt through its fold stages, one per gesture, and returns once it is folded
    /// and turned over.
    /// </summary>
    private IEnumerator RunFolds()
    {
        SetInstruction(instruction);

        foldsDone = 0;
        flipping = false;
        shirtImage.sprite = foldStages[0];

        swipeArea.Swiped += OnSwiped;
        ArmFold(0);

        while (foldsDone < FoldsNeeded || flipping)
            yield return null;

        swipeArea.Swiped -= OnSwiped;
        swipeArea.SetArmed(false);
    }

    private void OnSwiped()
    {
        if (foldsDone >= FoldsNeeded || flipping)
            return;

        bool wasCircular = IsCircularFold(foldsDone);
        foldsDone++;

        if (wasCircular)
        {
            // Turned over rather than folded, so it flips on the spot instead of snapping
            swipeArea.SetArmed(false);
            StartCoroutine(FlipTo(foldStages[foldsDone]));
            return;
        }

        shirtImage.sprite = foldStages[foldsDone];

        if (foldsDone < FoldsNeeded)
            ArmFold(foldsDone);
        else
            swipeArea.SetArmed(false);
    }

    /// <summary>
    /// Every fold past the authored straight swipes is the circular turn-over.
    /// </summary>
    private bool IsCircularFold(int index)
    {
        return swipeDirections == null || index >= swipeDirections.Length;
    }

    /// <summary>Points the arrow for the fold about to be made.</summary>
    private void ArmFold(int index)
    {
        if (IsCircularFold(index))
        {
            swipeArea.SetCircular(ShirtCentre(), rotateArrowPosition);
        }
        else
        {
            Vector2 pos = arrowPositions != null && arrowPositions.Length > 0
                ? arrowPositions[Mathf.Clamp(index, 0, arrowPositions.Length - 1)]
                : Vector2.zero;

            swipeArea.SetDirection(swipeDirections[index], pos);
        }

        swipeArea.SetArmed(true);
    }

    /// <summary>
    /// The folded shirt's middle, in the swipe area's space — what the circular swipe goes
    /// round. Both rects are centred in the panel, so the shirt's own position is it.
    /// </summary>
    private Vector2 ShirtCentre()
    {
        RectTransform shirtRect = (RectTransform)shirtImage.transform;
        RectTransform areaRect = (RectTransform)swipeArea.transform;
        return areaRect.InverseTransformPoint(shirtRect.position);
    }

    /// <summary>
    /// Squashes the shirt to nothing across its width, swaps to the turned-over sprite and
    /// opens it back out — reads as the shirt being flipped over in place.
    /// </summary>
    private IEnumerator FlipTo(Sprite turnedOver)
    {
        flipping = true;

        RectTransform rect = (RectTransform)shirtImage.transform;
        float half = flipDuration * 0.5f;

        for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
        {
            rect.localScale = new Vector3(Mathf.Lerp(1f, 0f, t / half), 1f, 1f);
            yield return null;
        }

        shirtImage.sprite = turnedOver;

        for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
        {
            rect.localScale = new Vector3(Mathf.Lerp(0f, 1f, t / half), 1f, 1f);
            yield return null;
        }

        rect.localScale = Vector3.one;
        flipping = false;
    }

    // ------------------------------------------------------------------ lifecycle

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
    /// Puts the shirt back to how a run starts, without touching whether the panel itself is
    /// on — see the note in Awake.
    /// </summary>
    private void ResetVisuals()
    {
        foldsDone = 0;
        flipping = false;

        if (swipeArea != null)
        {
            swipeArea.Swiped -= OnSwiped;
            swipeArea.SetArmed(false);
        }

        if (shirtImage != null)
        {
            shirtImage.transform.localScale = Vector3.one;
            if (foldStages != null && foldStages.Length > 0)
                shirtImage.sprite = foldStages[0];
        }

        SetInstruction(instruction);

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

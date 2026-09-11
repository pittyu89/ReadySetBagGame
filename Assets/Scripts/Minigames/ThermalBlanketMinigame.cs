using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The thermal blanket minigame that runs after the quiz's thermal blanket question.
///
/// Two beats, in the order the blanket is actually used:
///
///   1. Unfold. The blanket starts in its packed square and an arrow points the way it opens.
///      Each swipe along that arrow shakes out one more fold, through the four authored
///      stages, so the blanket grows under the player's own hand rather than on a timer.
///   2. Wrap. The unfolded blanket is dragged onto the character and she is wrapped in it for
///      the rest of the round, then steps to the middle of the screen to finish.
///
/// Like the other minigames there is nothing to fail. It runs whether the quiz answer was
/// right or wrong, there is no clock, and letting go of the blanket short of her only drops it
/// back where it started, ready to try again.
///
/// <see cref="Play"/> is a coroutine so QuizHandler can simply yield on it and carry on with
/// the next question once it returns.
/// </summary>
public class ThermalBlanketMinigame : MonoBehaviour
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
    [SerializeField, TextArea] private string unfoldInstruction = "Pull the blanket towards you";
    [Tooltip("Swapped in for the second half, once the blanket is open.")]
    [SerializeField, TextArea] private string wrapInstruction = "Drag the blanket to the character";

    [Header("Unfold")]
    [Tooltip("The folded blanket. Its sprite is stepped through the fold stages as the player " +
             "swipes; the rect never changes, so the blanket grows exactly as it is drawn.")]
    [SerializeField] private Image blanketImage;
    [SerializeField] private CanvasGroup blanketGroup;
    [Tooltip("The four fold stages, packed square first and fully open last.")]
    [SerializeField] private Sprite[] foldStages;
    [SerializeField] private ThermalBlanketSwipeArea swipeArea;
    [Tooltip("Which way each fold opens — one per swipe, so one fewer than the fold stages. " +
             "Alternating diagonals, matching the arrows on the reference sheet.")]
    [SerializeField]
    private Vector2[] swipeDirections = new Vector2[]
    {
        new Vector2(-1f, -1f),
        new Vector2(-1f,  1f),
        new Vector2(-1f, -1f)
    };
    [Tooltip("Where the arrow sits for each fold, in the swipe area's space. Matched to " +
             "swipeDirections; a short list reuses its last entry.")]
    [SerializeField]
    private Vector2[] arrowPositions = new Vector2[]
    {
        new Vector2(-150f, -110f),
        new Vector2(-210f,  110f),
        new Vector2(-260f, -140f)
    };

    [Header("Wrap")]
    [Tooltip("The character. Her sprite is swapped for the wrapped one once the blanket is " +
             "on, rather than a second blanket object being switched on over her — the two " +
             "authored sprites already line up, so there is nothing to keep in register.")]
    [SerializeField] private Image characterImage;
    [SerializeField] private CanvasGroup characterGroup;
    [SerializeField] private Sprite characterDefaultSprite;
    [SerializeField] private Sprite characterWithBlanketSprite;
    [Tooltip("Marks her body. The blanket goes on when dropped within snapRadius of this.")]
    [SerializeField] private RectTransform bodyTarget;
    [Tooltip("The unfolded blanket the player drags. Reuses the medkit's tool, which already " +
             "knows how to be picked up, carried and dropped back where it came from.")]
    [SerializeField] private MedkitTool blanketTool;
    [Tooltip("How close to her the blanket has to get, in canvas units. Generous on purpose: " +
             "the point of the minigame is the gesture, not the precision.")]
    [SerializeField] private float snapRadius = 220f;

    [Header("Layout")]
    [Tooltip("Where she stands while being wrapped, before stepping to the middle.")]
    [SerializeField] private Vector2 characterWrapPosition = new Vector2(-380f, -40f);
    [Tooltip("Where she finishes, centred, once she has the blanket on.")]
    [SerializeField] private Vector2 characterFinishPosition = new Vector2(0f, -40f);

    [Header("Timing")]
    [SerializeField] private float panelFadeDuration = 0.25f;
    [SerializeField] private float instructionFadeDuration = 0.3f;
    [Tooltip("How long the opened blanket takes to fade away before she walks on.")]
    [SerializeField] private float handoverFadeDuration = 0.45f;
    [Tooltip("Beat between the blanket vanishing and the character appearing.")]
    [SerializeField] private float handoverBeat = 0.2f;
    [Tooltip("How long the blanket takes to travel the last of the way onto her once it has " +
             "been dropped there.")]
    [SerializeField] private float slideDuration = 0.2f;
    [Tooltip("Beat after the blanket goes on, before she steps to the middle.")]
    [SerializeField] private float wearBeat = 0.5f;
    [Tooltip("How long she takes to fade out and back in at the middle of the screen.")]
    [SerializeField] private float walkFadeDuration = 0.3f;
    [Tooltip("Pause once she is centred, before the minigame closes.")]
    [SerializeField] private float finishDelay = 0.9f;

    [Header("Finish")]
    [Tooltip("Optional. Flashed once she is wrapped up.")]
    [SerializeField] private GameObject completedBanner;

    private bool isPlaying = false;

    // Bumped by the swipe area; read by the unfold loop.
    private int foldsDone = 0;

    // Carried between frames so the drop can be judged on where the blanket was while it was
    // still in the player's hand
    private bool wasHeld = false;
    private bool heldOverBody = false;

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

        if (blanketImage == null || swipeArea == null || foldStages == null || foldStages.Length < 2
            || characterImage == null || blanketTool == null || bodyTarget == null
            || characterWithBlanketSprite == null)
        {
            // Bail loudly rather than quietly: a half-wired panel would otherwise hang the
            // quiz on a blanket that can never be unfolded.
            Debug.LogWarning("[ThermalBlanketMinigame] Needs the blanket image, the swipe area, " +
                             "at least two fold stages, the character, the blanket tool, the " +
                             "body marker and the wrapped sprite — skipping.", this);
            yield break;
        }

        isPlaying = true;

        Open();

        if (backdrop != null)
            yield return StartCoroutine(backdrop.CaptureIncludingUIRoutine());

        yield return FadeGroup(panelGroup, 0f, 1f, panelFadeDuration);
        yield return FadeGroup(instructionCard, 0f, 1f, instructionFadeDuration);

        yield return RunUnfold();
        yield return RunHandover();
        yield return RunWrap();

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

    // ------------------------------------------------------------------ unfold

    /// <summary>
    /// Steps the blanket through its fold stages, one per swipe, and returns once it is open.
    /// </summary>
    private IEnumerator RunUnfold()
    {
        SetInstruction(unfoldInstruction);

        foldsDone = 0;
        blanketImage.sprite = foldStages[0];

        swipeArea.Swiped += OnSwiped;
        ArmFold(0);

        int foldsNeeded = foldStages.Length - 1;
        while (foldsDone < foldsNeeded)
            yield return null;

        swipeArea.Swiped -= OnSwiped;
        swipeArea.SetArmed(false);
    }

    private void OnSwiped()
    {
        int foldsNeeded = foldStages.Length - 1;
        if (foldsDone >= foldsNeeded)
            return;

        foldsDone++;
        blanketImage.sprite = foldStages[Mathf.Min(foldsDone, foldStages.Length - 1)];

        if (foldsDone < foldsNeeded)
            ArmFold(foldsDone);
    }

    /// <summary>Points the arrow for the fold about to be swiped.</summary>
    private void ArmFold(int index)
    {
        Vector2 dir = PickOrLast(swipeDirections, index, new Vector2(-1f, -1f));
        Vector2 pos = PickOrLast(arrowPositions, index, Vector2.zero);

        swipeArea.SetDirection(dir, pos);
        swipeArea.SetArmed(true);
    }

    /// <summary>
    /// Entry <paramref name="index"/>, or the last one if the list is short. Lets the
    /// directions and arrow spots be authored once and reused for any extra folds.
    /// </summary>
    private static Vector2 PickOrLast(Vector2[] list, int index, Vector2 fallback)
    {
        if (list == null || list.Length == 0)
            return fallback;

        return list[Mathf.Clamp(index, 0, list.Length - 1)];
    }

    // ------------------------------------------------------------------ handover

    /// <summary>
    /// Fades the opened blanket away and brings the character on in its place, which is the
    /// beat the reference sheet asks for between the two halves.
    /// </summary>
    private IEnumerator RunHandover()
    {
        yield return FadeGroup(blanketGroup, 1f, 0f, handoverFadeDuration);

        if (blanketImage != null)
            blanketImage.gameObject.SetActive(false);

        yield return new WaitForSecondsRealtime(handoverBeat);

        if (characterImage != null)
        {
            characterImage.gameObject.SetActive(true);
            SetCharacterPosition(characterWrapPosition);
        }

        yield return FadeGroup(characterGroup, 0f, 1f, handoverFadeDuration);
    }

    // ------------------------------------------------------------------ wrap

    /// <summary>
    /// Waits for the blanket to be dragged onto her, puts it on, then steps her to the middle.
    /// </summary>
    private IEnumerator RunWrap()
    {
        SetInstruction(wrapInstruction);

        blanketTool.gameObject.SetActive(true);
        blanketTool.ResetTool();
        blanketTool.SetAvailable(true);

        while (!WasBlanketDroppedOnBody())
            yield return null;

        yield return SlideBlanketOntoBody();

        yield return new WaitForSecondsRealtime(wearBeat);

        yield return StepToCentre();
    }

    /// <summary>
    /// True on the frame the player lets go of the blanket over her.
    ///
    /// The drop is what puts it on, not merely passing over her: dragging the blanket across
    /// her on the way somewhere else should not count, and the player should be the one
    /// deciding when it goes on.
    ///
    /// Whether it was over her is remembered from the last frame it was actually held, rather
    /// than measured after the release. The tool starts springing back to its corner the
    /// moment it is let go, so by the time the release is noticed it has already begun moving
    /// away from where it was dropped.
    /// </summary>
    private bool WasBlanketDroppedOnBody()
    {
        if (blanketTool.IsDragging)
        {
            wasHeld = true;
            heldOverBody = IsBlanketWithinReach();
            return false;
        }

        if (!wasHeld)
            return false;

        wasHeld = false;

        bool landed = heldOverBody;
        heldOverBody = false;
        return landed;
    }

    /// <summary>
    /// Whether the blanket is close enough to her to count.
    ///
    /// Measured in the panel's own space rather than in screen pixels, so the reach is the
    /// same on a phone as in the Editor whatever the canvas is scaled to.
    /// </summary>
    private bool IsBlanketWithinReach()
    {
        RectTransform panelRect = panelRoot != null
            ? panelRoot.transform as RectTransform
            : transform as RectTransform;

        if (panelRect == null)
            return false;

        Vector2 blanket = panelRect.InverseTransformPoint(blanketTool.transform.position);
        Vector2 body = panelRect.InverseTransformPoint(bodyTarget.position);

        return Vector2.Distance(blanket, body) <= snapRadius;
    }

    /// <summary>
    /// Carries the blanket the last of the way onto her, then hands over to the sprite that
    /// has it drawn on.
    ///
    /// Without this the blanket jumps back to its corner and blinks out the instant it is
    /// dropped, because the tool springs home the moment it is no longer held — the player
    /// slides it to her and watches it fly away. Sliding it the rest of the way finishes the
    /// gesture they started.
    /// </summary>
    private IEnumerator SlideBlanketOntoBody()
    {
        blanketTool.SetAvailable(false);

        // The tool's own spring pulls it home every frame it is not held, so it has to be
        // switched off for the blanket to go anywhere else.
        blanketTool.enabled = false;

        RectTransform rect = (RectTransform)blanketTool.transform;
        Vector3 from = rect.position;
        Vector3 to = bodyTarget.position;
        Vector3 fromScale = rect.localScale;

        for (float t = 0f; t < slideDuration; t += Time.unscaledDeltaTime)
        {
            // Ease out, so it settles onto her rather than arriving at full speed
            float k = Mathf.Clamp01(t / slideDuration);
            float eased = 1f - Mathf.Pow(1f - k, 3f);

            rect.position = Vector3.Lerp(from, to, eased);
            rect.localScale = Vector3.Lerp(fromScale, Vector3.one * 0.6f, eased);
            yield return null;
        }

        rect.position = to;

        // The wrapped sprite takes over on the same frame the carried one goes, so there is
        // never a gap with no blanket anywhere
        if (characterImage != null && characterWithBlanketSprite != null)
            characterImage.sprite = characterWithBlanketSprite;

        blanketTool.gameObject.SetActive(false);
        blanketTool.enabled = true;
        blanketTool.ResetTool();
    }

    /// <summary>
    /// Moves her to the middle of the screen to close the scene out. Faded across rather than
    /// slid, which is what the reference sheet asks for — she is wrapped up and settled, not
    /// still walking about.
    /// </summary>
    private IEnumerator StepToCentre()
    {
        yield return FadeGroup(characterGroup, 1f, 0f, walkFadeDuration);

        SetCharacterPosition(characterFinishPosition);

        yield return FadeGroup(characterGroup, 0f, 1f, walkFadeDuration);
    }

    private void SetCharacterPosition(Vector2 position)
    {
        RectTransform rect = characterImage != null
            ? characterImage.transform as RectTransform
            : null;

        if (rect != null)
            rect.anchoredPosition = position;
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
    /// Puts the blanket and the character back to how a run starts, without touching whether
    /// the panel itself is on — see the note in Awake.
    /// </summary>
    private void ResetVisuals()
    {
        wasHeld = false;
        heldOverBody = false;
        foldsDone = 0;

        if (swipeArea != null)
        {
            swipeArea.Swiped -= OnSwiped;
            swipeArea.SetArmed(false);
        }

        if (blanketImage != null)
        {
            blanketImage.gameObject.SetActive(true);
            if (foldStages != null && foldStages.Length > 0)
                blanketImage.sprite = foldStages[0];
        }

        if (blanketGroup != null)
            blanketGroup.alpha = 1f;

        if (characterImage != null)
        {
            if (characterDefaultSprite != null)
                characterImage.sprite = characterDefaultSprite;

            SetCharacterPosition(characterWrapPosition);
            characterImage.gameObject.SetActive(false);
        }

        if (characterGroup != null)
            characterGroup.alpha = 0f;

        // Out of sight until the wrap half starts, so it cannot be grabbed during the unfold
        if (blanketTool != null)
        {
            // Switched back on in case a run was cut short mid-slide, which would otherwise
            // leave the tool inert for the next one
            blanketTool.enabled = true;
            blanketTool.ResetTool();
            blanketTool.gameObject.SetActive(false);
        }

        SetInstruction(unfoldInstruction);

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
}

using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// The first-aid minigame that runs after the quiz's first-aid kit question.
///
/// An arm turns slowly in front of the player, bruised and cut. The kit sits just off the
/// bottom of the screen with only its handle showing; swiping it up brings the alcohol and
/// a roll of gauze into reach. Tapping the alcohol wets a cotton swab, the swab wipes the
/// bruises off the arm, and only once they are gone does the gauze come free to patch the
/// cut. Three stages, in the order a real first-aider would do them.
///
/// Like the other minigames there is nothing to fail here. It runs whether the quiz answer
/// was right or wrong, there is no clock, and wiping clean skin simply does nothing.
///
/// <see cref="Play"/> is a coroutine so QuizHandler can simply yield on it and carry on
/// with the next question once it returns.
/// </summary>
public class FirstAidMinigame : MonoBehaviour
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
    [SerializeField, TextArea] private string instructionText =
        "Inspect the hands for bruises and wound,\nthen clean the bruises and patch the wound with gauze";

    [Header("Arm")]
    [Tooltip("The window onto the arm. Turns it, and reports where a tool is touching.")]
    [SerializeField] private ArmInspector armInspector;
    [Tooltip("Rubs the bruises out of the wound mask.")]
    [SerializeField] private WoundMaskPainter painter;
    [Tooltip("The gauze wrapped round the wound, switched on once the gauze is applied. " +
             "A band round the same arm rather than a second bandaged mesh, because the " +
             "two authored arm FBXs turned out to be identical geometry.")]
    [SerializeField] private GameObject gauzeBand;
    [Tooltip("Pose the arm starts every run in, so the first look is always the same.")]
    [SerializeField] private Vector3 armStartEuler = Vector3.zero;

    [Header("Kit")]
    [SerializeField] private MedkitDrawer medkit;
    [Tooltip("Tapped once, where it sits in the kit. Wetting a swab with it is what puts " +
             "the swab in the player's hand — the bottle itself never goes near the arm.")]
    [SerializeField] private MedkitTool alcoholTool;
    [Tooltip("Not in the kit to begin with. Appears once the alcohol has been tapped, and " +
             "is the thing actually dragged over the bruises.")]
    [SerializeField] private MedkitTool swabTool;
    [Tooltip("Last, once the arm is clean. Patches the cut.")]
    [SerializeField] private MedkitTool gauzeTool;

    [Header("Cleaning")]
    [Tooltip("How much of the bruising has to be gone before the gauze comes free. Short " +
             "of 1 so a few stubborn stray texels cannot strand the player.")]
    [SerializeField, Range(0.5f, 1f)] private float cleanThreshold = 0.96f;

    [Header("Timing")]
    [SerializeField] private float panelFadeDuration = 0.25f;
    [SerializeField] private float instructionFadeDuration = 0.3f;
    [Tooltip("Beat after the bruises are gone, before the gauze is offered.")]
    [SerializeField] private float cleanedBeat = 0.5f;
    [Tooltip("Pause once the arm is bandaged, before the minigame closes.")]
    [SerializeField] private float finishDelay = 1.1f;

    [Header("Finish")]
    [Tooltip("Optional. Flashed once the arm is patched up.")]
    [SerializeField] private GameObject completedBanner;

    private bool isPlaying = false;
    private bool isBandaged = false;
    private bool swabOut = false;

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

    /// <summary>
    /// Runs the whole minigame and returns once it has closed.
    /// Yield on this from the quiz; it never returns early or leaves the panel up.
    /// </summary>
    public IEnumerator Play()
    {
        if (isPlaying)
            yield break;

        if (armInspector == null || painter == null || medkit == null ||
            alcoholTool == null || swabTool == null || gauzeTool == null)
        {
            // Bail loudly rather than quietly: a half-wired panel would otherwise hang the
            // quiz on an arm that can never be treated.
            Debug.LogWarning("[FirstAidMinigame] Needs the arm rig, the painter, the kit " +
                             "and all three tools — skipping.", this);
            yield break;
        }

        isPlaying = true;

        Open();

        if (backdrop != null)
            yield return StartCoroutine(backdrop.CaptureIncludingUIRoutine());

        yield return FadeGroup(panelGroup, 0f, 1f, panelFadeDuration);
        yield return FadeGroup(instructionCard, 0f, 1f, instructionFadeDuration);

        // Nothing to treat the arm with until the kit is open, so the round starts there.
        // Turning the arm over to look at it is free the whole time.
        while (!medkit.IsOpen)
            yield return null;

        // The alcohol is tapped where it lies; it never touches the arm itself. Tapping it
        // wets a swab and lays it out by the forearm, and tapping it again puts that swab
        // away — so the bottle stays live for the whole cleaning phase rather than being
        // spent on the first press. The white edge on the bottle is what says which of
        // those two states the player is in.
        swabOut = false;
        alcoholTool.Used += OnAlcoholUsed;
        alcoholTool.SetAvailable(true);

        while (painter.Cleanliness < cleanThreshold)
        {
            if (swabOut)
            {
                Wipe(swabTool);
            }
            else
            {
                // With the swab away the arm is free to be turned again, so the player can
                // bring the other side round before wetting a fresh one.
                armInspector.AllowRotation = true;
            }

            yield return null;
        }

        alcoholTool.Used -= OnAlcoholUsed;
        alcoholTool.SetAvailable(false);

        SetSwabOut(false);
        swabTool.SetAvailable(false);
        armInspector.AllowRotation = true;

        yield return new WaitForSecondsRealtime(cleanedBeat);

        gauzeTool.SetAvailable(true);

        while (!isBandaged)
        {
            Bandage();
            yield return null;
        }

        gauzeTool.SetAvailable(false);
        armInspector.AllowRotation = true;

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

    private void OnAlcoholUsed()
    {
        SetSwabOut(!swabOut);
    }

    /// <summary>
    /// Wets a swab and lays it beside the arm, or puts it away again.
    ///
    /// The swab's resting spot is wherever it is authored in the scene — out by the
    /// forearm, not down in the kit — so ResetTool is what places it. That also covers
    /// letting go mid-drag and tapping the bottle: the swab goes back to its spot rather
    /// than vanishing from under the finger.
    /// </summary>
    private void SetSwabOut(bool showSwab)
    {
        swabOut = showSwab;

        if (alcoholTool != null)
            alcoholTool.SetSelected(showSwab);

        if (swabTool == null)
            return;

        if (showSwab)
        {
            swabTool.gameObject.SetActive(true);
            swabTool.ResetTool();
            swabTool.SetAvailable(true);
            return;
        }

        swabTool.ResetTool();
        swabTool.gameObject.SetActive(false);

        if (armInspector != null)
            armInspector.AllowRotation = true;
    }

    /// <summary>
    /// One frame of cleaning with whichever tool is currently in hand: if its tip is on the
    /// arm, rub the mask out there. The alcohol and the swab work the same way — they only
    /// differ in when the minigame hands each one over.
    /// </summary>
    private void Wipe(MedkitTool tool)
    {
        // A tool in hand takes the drag; the arm would otherwise spin out from under it
        // the moment the player tried to hold it on a bruise.
        armInspector.AllowRotation = !tool.IsDragging;

        if (!tool.IsDragging)
            return;

        Vector2 uv;
        if (!armInspector.TryGetUV(tool.TipPosition, out uv))
            return;

        painter.PaintAt(uv, Time.unscaledDeltaTime);
    }

    /// <summary>
    /// One frame of patching: touching the arm with the gauze bandages it, once.
    /// </summary>
    private void Bandage()
    {
        armInspector.AllowRotation = !gauzeTool.IsDragging;

        if (!gauzeTool.IsDragging)
            return;

        Vector2 uv;
        if (!armInspector.TryGetUV(gauzeTool.TipPosition, out uv))
            return;

        isBandaged = true;

        // The gauze is its own band around the arm, so it just switches on — the arm
        // underneath stays exactly as the player left it.
        if (gauzeBand != null)
            gauzeBand.SetActive(true);
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

        isBandaged = false;
        swabOut = false;

        if (gauzeBand != null)
            gauzeBand.SetActive(false);

        if (painter != null)
            painter.ResetMask();

        if (armInspector != null)
        {
            armInspector.AllowRotation = true;
            armInspector.ResetPose(Quaternion.Euler(armStartEuler));
        }

        if (medkit != null)
            medkit.ResetDrawer();

        if (alcoholTool != null)
            alcoholTool.ResetTool();

        // Off the table again: the swab only exists while the alcohol is selected
        if (swabTool != null)
        {
            swabTool.ResetTool();
            swabTool.gameObject.SetActive(false);
        }

        if (gauzeTool != null)
            gauzeTool.ResetTool();
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
    /// Puts the arm, the kit and the tools back to how a run starts, without touching
    /// whether the panel itself is on — see the note in Awake.
    /// </summary>
    private void ResetVisuals()
    {
        isBandaged = false;
        swabOut = false;

        if (gauzeBand != null)
            gauzeBand.SetActive(false);

        if (alcoholTool != null)
            alcoholTool.ResetTool();

        // Off the table again: the swab only exists while the alcohol is selected
        if (swabTool != null)
        {
            swabTool.ResetTool();
            swabTool.gameObject.SetActive(false);
        }

        if (gauzeTool != null)
            gauzeTool.ResetTool();

        if (medkit != null)
            medkit.ResetDrawer();

        if (completedBanner != null)
            completedBanner.SetActive(false);

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

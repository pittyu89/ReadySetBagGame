using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The rope minigame that runs after the quiz's rope question.
///
/// A length of rope is stretched across a ruler. The player slides the knife to the target
/// measurement and presses CUT; the right-hand half drops away and the length that is left
/// is what they cut. Measuring is the whole point, so the target is called out up front and
/// a live readout follows the blade.
///
/// Like the other minigames there is nothing to fail. Cutting short of the tolerance says
/// so and hands the rope back for another go, rather than ending the round on it — the
/// point is that the player measures, not that they are graded twice for one question.
///
/// <see cref="Play"/> is a coroutine so QuizManager can simply yield on it and carry on
/// with the next question once it returns.
/// </summary>
public class PocketKnifeMinigame : MonoBehaviour
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
    [SerializeField, TextArea] private string instructionText = "Slide the knife to the target length, then cut the rope";

    [Header("Measuring")]
    [Tooltip("Marks the 0 m end of the rope. Everything is measured from here, so the " +
             "ruler graphic and the maths can never drift apart.")]
    [SerializeField] private RectTransform anchorStart;
    [Tooltip("Marks the far end of the rope, at ropeLengthMeters.")]
    [SerializeField] private RectTransform anchorEnd;
    [Tooltip("What the full span between the two anchors is worth.")]
    [SerializeField] private float ropeLengthMeters = 10f;

    [Header("Rope")]
    [Tooltip("The piece the player keeps. Pivot must be on the left; its width is driven " +
             "from the anchor to the blade.")]
    [SerializeField] private RectTransform leftRope;

    // The offcut is three nested pieces rather than one, because a tiled Image lays its
    // tiles out from its own bottom-left corner. Sizing a single rect from the blade would
    // drag the rope's whole twist pattern along with the knife. Instead the rope image is
    // pinned to the 0 m anchor and never moves, and a mask reveals only the part beyond the
    // blade — so the texture stays put on screen while the cut point slides.
    [Tooltip("Hinge for the offcut: sits at the cut and is what swings down. Pivot on the left.")]
    [SerializeField] private RectTransform offcutRoot;
    [Tooltip("The masked window, sized from the blade to the far anchor. Needs a Mask " +
             "component (stencil, not RectMask2D) so it still clips once the hinge rotates.")]
    [SerializeField] private RectTransform offcutWindow;
    [Tooltip("The tiled rope inside the window. Held at full span so its tiling origin " +
             "never moves.")]
    [SerializeField] private RectTransform offcutRope;
    [SerializeField] private CanvasGroup offcutGroup;

    [Header("Knife")]
    [SerializeField] private PocketKnifeSlider knife;
    [Tooltip("The rect the knife slides in — normally its own parent. Left empty, the " +
             "parent is used.")]
    [SerializeField] private RectTransform knifeArea;

    [Header("Controls")]
    [SerializeField] private Button cutButton;
    [SerializeField] private TextMeshProUGUI targetLabel;
    [Tooltip("Live readout of where the blade currently sits. Optional.")]
    [SerializeField] private TextMeshProUGUI readoutLabel;

    [Header("Target")]
    [Tooltip("Shortest target the round may ask for, in metres.")]
    [SerializeField] private float minTargetMeters = 3f;
    [Tooltip("Longest target the round may ask for, in metres.")]
    [SerializeField] private float maxTargetMeters = 8f;
    [Tooltip("Targets are rounded to this step so the ask is always a clean number to read " +
             "off the ruler. 1 gives whole metres, 0.5 gives halves.")]
    [SerializeField] private float targetStepMeters = 1f;
    [Tooltip("How far off the target still counts as a good cut, in metres. Generous on " +
             "purpose: this is a measuring exercise for Grade 6 players, not a precision " +
             "test, and the question has already been scored by the time it runs.")]
    [SerializeField] private float toleranceMeters = 0.3f;

    [Header("Timing")]
    [SerializeField] private float panelFadeDuration = 0.25f;
    [SerializeField] private float instructionFadeDuration = 0.3f;
    [Tooltip("How long the offcut takes to swing down and out of shot.")]
    [SerializeField] private float ropeDropDuration = 0.55f;
    [Tooltip("Pause once the cut has landed, before the minigame closes.")]
    [SerializeField] private float finishDelay = 0.9f;
    [Tooltip("How long a missed cut's message stays up before the rope is handed back.")]
    [SerializeField] private float retryMessageSeconds = 1.3f;

    [Header("Sound")]
    [Tooltip("As the rope is cut through. A miss leaves the rope whole, so it is silent.")]
    [SerializeField] private AudioClip cutSFX;

    [Header("Finish")]
    [Tooltip("Optional. Flashed once the rope has been cut to length.")]
    [SerializeField] private GameObject completedBanner;

    private bool isPlaying = false;
    private bool cutRequested = false;
    private float targetMeters;

    // Where the rope hangs before anything is cut. Remembered once, because the offcut is
    // animated away from it and has to be put back for the next run — otherwise a second
    // play opens with the right-hand half still lying where the last one dropped it.
    private float ropeHomeY;
    private bool ropeHomeCaptured;

    public bool IsPlaying => isPlaying;

    private void Awake()
    {
        if (panelGroup == null && panelRoot != null)
            panelGroup = panelRoot.GetComponent<CanvasGroup>();

        if (instructionLabel != null && !string.IsNullOrEmpty(instructionText))
            instructionLabel.text = instructionText;

        CaptureRopeHome();

        // Reset the contents but leave the panel's active state alone. Awake first runs
        // during the SetActive in Open, so deactivating here would switch the panel back
        // off underneath the very coroutine that just turned it on.
        ResetVisuals();
    }

    private void OnEnable()
    {
        if (knife != null)
            knife.Moved += OnKnifeMoved;

        if (cutButton != null)
            cutButton.onClick.AddListener(OnCutClicked);
    }

    private void OnDisable()
    {
        if (knife != null)
            knife.Moved -= OnKnifeMoved;

        if (cutButton != null)
            cutButton.onClick.RemoveListener(OnCutClicked);
    }

    /// <summary>
    /// Runs the whole minigame and returns once it has closed.
    /// Yield on this from the quiz; it never returns early or leaves the panel up.
    /// </summary>
    public IEnumerator Play()
    {
        if (isPlaying)
            yield break;

        if (knife == null || leftRope == null || offcutRoot == null || offcutWindow == null
            || offcutRope == null || anchorStart == null || anchorEnd == null || cutButton == null)
        {
            // Bail loudly rather than quietly: a half-wired panel would otherwise hang the
            // quiz on a rope that can never be cut.
            Debug.LogWarning("[PocketKnifeMinigame] Needs the anchors, both rope halves, a knife " +
                             "and a cut button — skipping.", this);
            yield break;
        }

        isPlaying = true;

        Open();

        if (backdrop != null)
            yield return StartCoroutine(backdrop.CaptureIncludingUIRoutine());

        yield return FadeGroup(panelGroup, 0f, 1f, panelFadeDuration);
        yield return FadeGroup(instructionCard, 0f, 1f, instructionFadeDuration);

        knife.SetArmed(true);
        cutButton.interactable = true;

        // Keeps going until the rope is cut to length. A miss is not a loss — it says so
        // and hands the rope straight back.
        bool done = false;
        while (!done)
        {
            cutRequested = false;
            while (!cutRequested)
                yield return null;

            knife.SetArmed(false);
            cutButton.interactable = false;

            float cutAt = CurrentMeters();
            bool onTarget = Mathf.Abs(cutAt - targetMeters) <= toleranceMeters;

            if (onTarget)
            {
                SoundManager.Sfx(cutSFX);
                yield return StartCoroutine(DropOffcut());
                done = true;
            }
            else
            {
                if (readoutLabel != null)
                    readoutLabel.text = Format(cutAt) + " — try again";

                yield return new WaitForSecondsRealtime(retryMessageSeconds);

                knife.SetArmed(true);
                cutButton.interactable = true;
                RefreshRope();
            }
        }

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

    private void OnCutClicked()
    {
        if (isPlaying)
            cutRequested = true;
    }

    private void OnKnifeMoved()
    {
        RefreshRope();
    }

    // ------------------------------------------------------------------ measuring

    /// <summary>
    /// Where the blade currently sits, in metres from <see cref="anchorStart"/>. Everything
    /// else reads off this, so the ruler graphic and the score can never disagree.
    /// </summary>
    private float CurrentMeters()
    {
        float span = anchorEnd.anchoredPosition.x - anchorStart.anchoredPosition.x;
        if (Mathf.Approximately(span, 0f))
            return 0f;

        float t = (knife.PositionX - anchorStart.anchoredPosition.x) / span;
        return Mathf.Clamp01(t) * ropeLengthMeters;
    }

    /// <summary>Stretches both halves to meet the blade.</summary>
    private void RefreshRope()
    {
        if (leftRope == null || offcutRoot == null || offcutWindow == null || offcutRope == null
            || knife == null || anchorStart == null || anchorEnd == null)
            return;

        float startX = anchorStart.anchoredPosition.x;
        float endX = anchorEnd.anchoredPosition.x;
        float bladeX = Mathf.Clamp(knife.PositionX, startX, endX);

        // The kept half is pivoted on its left edge at the 0 m anchor, so its tiling origin
        // is fixed and only its far end follows the blade.
        leftRope.anchoredPosition = new Vector2(startX, ropeHomeY);
        leftRope.sizeDelta = new Vector2(Mathf.Max(0f, bladeX - startX), leftRope.sizeDelta.y);

        // The hinge sits on the cut; the window opens from there to the far anchor...
        offcutRoot.anchoredPosition = new Vector2(bladeX, ropeHomeY);
        offcutWindow.sizeDelta = new Vector2(Mathf.Max(0f, endX - bladeX), offcutWindow.sizeDelta.y);

        // ...and the rope inside is pushed back so it still starts at the 0 m anchor,
        // which is what keeps the twist pattern from sliding as the knife moves.
        offcutRope.anchoredPosition = new Vector2(startX - bladeX, 0f);
        offcutRope.sizeDelta = new Vector2(endX - startX, offcutRope.sizeDelta.y);

        if (readoutLabel != null && isPlaying)
            readoutLabel.text = Format(CurrentMeters());
    }

    /// <summary>
    /// Swings the offcut down and out of shot. Stands in for the hinge and rigidbody the
    /// design called for: a fixed fall reads the same, cannot be knocked out of tune, and
    /// keeps the whole minigame inside the UI canvas with the rest of them.
    /// </summary>
    private IEnumerator DropOffcut()
    {
        if (offcutRoot == null)
            yield break;

        // Swings about the hinge, which sits exactly on the cut — so the offcut pivots away
        // from where the blade went in rather than sliding off sideways.
        Vector2 from = offcutRoot.anchoredPosition;
        Quaternion fromRot = offcutRoot.localRotation;
        Quaternion toRot = Quaternion.Euler(0f, 0f, -70f);
        float fall = 420f;

        for (float t = 0f; t < ropeDropDuration; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.Clamp01(t / ropeDropDuration);

            // Accelerating, so it reads as falling rather than sliding
            float gravity = k * k;

            offcutRoot.anchoredPosition = new Vector2(from.x, from.y - fall * gravity);
            offcutRoot.localRotation = Quaternion.Slerp(fromRot, toRot, gravity);

            if (offcutGroup != null)
                offcutGroup.alpha = 1f - Mathf.Clamp01((k - 0.55f) / 0.45f);

            yield return null;
        }

        if (offcutGroup != null)
            offcutGroup.alpha = 0f;

        offcutRoot.gameObject.SetActive(false);
    }

    private string Format(float meters)
    {
        return meters.ToString("0.0") + "m";
    }

    /// <summary>Remembers the rope's resting height, once, before anything moves it.</summary>
    private void CaptureRopeHome()
    {
        if (ropeHomeCaptured || offcutRoot == null)
            return;

        ropeHomeY = offcutRoot.anchoredPosition.y;
        ropeHomeCaptured = true;
    }

    /// <summary>
    /// Undoes the drop: the offcut goes back on, upright, opaque and level with the half it
    /// was cut from. Everything the fall animation touched is reset here, so a second run
    /// opens on a whole rope rather than on the wreckage of the last one.
    /// </summary>
    private void RestoreRope()
    {
        CaptureRopeHome();

        if (offcutRoot != null)
        {
            offcutRoot.gameObject.SetActive(true);
            offcutRoot.localRotation = Quaternion.identity;
            offcutRoot.anchoredPosition = new Vector2(offcutRoot.anchoredPosition.x, ropeHomeY);
        }

        if (leftRope != null)
            leftRope.anchoredPosition = new Vector2(leftRope.anchoredPosition.x, ropeHomeY);

        if (offcutGroup != null)
            offcutGroup.alpha = 1f;
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

        // A clean number somewhere in the middle of the ruler, so the answer is never the
        // very end of the rope and never a figure that cannot be read off it.
        float steps = Mathf.Max(1f, targetStepMeters);
        float raw = Random.Range(minTargetMeters, maxTargetMeters);
        targetMeters = Mathf.Clamp(Mathf.Round(raw / steps) * steps, minTargetMeters, maxTargetMeters);

        if (targetLabel != null)
            targetLabel.text = "TARGET: " + Format(targetMeters);

        // Hand the knife its rails, then park it midway along the rope so the blade starts
        // in view and the player can reach either end of the ruler with a short drag.
        RectTransform area = knifeArea != null ? knifeArea : (RectTransform)knife.transform.parent;
        knife.Configure(area, panelRoot != null ? panelRoot.GetComponentInParent<Canvas>() : null,
                        anchorStart.anchoredPosition.x, anchorEnd.anchoredPosition.x);
        knife.SetArmed(false);
        knife.SetPositionX((anchorStart.anchoredPosition.x + anchorEnd.anchoredPosition.x) * 0.5f);

        RestoreRope();

        cutRequested = false;

        if (cutButton != null)
            cutButton.interactable = false;

        RefreshRope();

        if (readoutLabel != null)
            readoutLabel.text = Format(0f);
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
        if (knife != null)
            knife.SetArmed(false);

        if (cutButton != null)
            cutButton.interactable = false;

        if (completedBanner != null)
            completedBanner.SetActive(false);

        RestoreRope();

        cutRequested = false;

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

using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// The contact card minigame that runs after the quiz's contact card question.
///
/// One beat: the card comes out of the bag covered in mud, and the player rubs it off with a
/// finger until the details underneath can be read again. A go-bag's contact card is no use
/// if nobody can make out what it says, which is the whole point being made.
///
/// Like the other minigames there is nothing to fail. It runs whether the quiz answer was
/// right or wrong, there is no clock, and no way to make the card dirtier than it started.
///
/// The mud itself, and the rubbing, live in <see cref="ContactCardSmudges"/>; this owns when
/// the card is clean enough to call done and what happens either side of that.
///
/// <see cref="Play"/> is a coroutine so QuizManager can simply yield on it and carry on with
/// the next question once it returns.
/// </summary>
public class ContactCardMinigame : MonoBehaviour
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
    [SerializeField, TextArea] private string instructionText = "Wipe the smudges off your contact card";

    [Header("Card")]
    [Tooltip("The mud over the card. Its rect sits exactly over the card art, so the two " +
             "share a pixel grid.")]
    [SerializeField] private ContactCardSmudges smudges;

    [Header("Cleaning")]
    [Tooltip("How much of the mud has to come off before the card counts as clean. Short of " +
             "1 on purpose — the last few flecks are not worth hunting for, and whatever is " +
             "left is faded off by clearFadeDuration rather than left sitting there.")]
    [SerializeField, Range(0.5f, 1f)] private float cleanThreshold = 0.9f;

    [Header("Timing")]
    [SerializeField] private float panelFadeDuration = 0.25f;
    [SerializeField] private float instructionFadeDuration = 0.3f;
    [Tooltip("How long the last of the mud takes to fade off once the card is clean enough.")]
    [SerializeField] private float clearFadeDuration = 0.4f;
    [Tooltip("Beat on the clean card, before the banner.")]
    [SerializeField] private float clearedBeat = 0.35f;
    [Tooltip("Pause once the card is clean, before the minigame closes.")]
    [SerializeField] private float finishDelay = 0.9f;

    [Header("Finish")]
    [Tooltip("Optional. Flashed once the card is clean.")]
    [SerializeField] private GameObject completedBanner;

    [Header("Sound")]
    [Tooltip("Loops while a finger is rubbing the mud.")]
    [SerializeField] private AudioClip rubLoopSFX;

    private SfxLoop rubLoop;

    private bool isPlaying = false;

    public bool IsPlaying { get { return isPlaying; } }

    private void Awake()
    {
        if (panelGroup == null && panelRoot != null)
            panelGroup = panelRoot.GetComponent<CanvasGroup>();

        rubLoop = SfxLoop.Create(gameObject, rubLoopSFX);

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

        if (smudges == null)
        {
            // Bail loudly rather than quietly: a half-wired panel would otherwise hang the
            // quiz on a card that can never be cleaned.
            Debug.LogWarning("[ContactCardMinigame] Needs the smudge layer — skipping.", this);
            yield break;
        }

        isPlaying = true;

        Open();

        if (backdrop != null)
            yield return StartCoroutine(backdrop.CaptureIncludingUIRoutine());

        yield return FadeGroup(panelGroup, 0f, 1f, panelFadeDuration);
        yield return FadeGroup(instructionCard, 0f, 1f, instructionFadeDuration);

        smudges.SetArmed(true);

        while (smudges.Cleanliness < cleanThreshold)
        {
            // Only while the finger is moving: a finger resting on the card makes no sound
            if (rubLoop != null && smudges.IsWiping && Time.unscaledTime - smudges.LastRubTime < 0.1f)
                rubLoop.Hold();

            yield return null;
        }

        smudges.SetArmed(false);

        yield return ClearRemaining();

        yield return new WaitForSecondsRealtime(clearedBeat);

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

    /// <summary>
    /// Takes off whatever mud the player did not quite get to, so the card they are left
    /// looking at is properly clean rather than clean enough.
    /// </summary>
    private IEnumerator ClearRemaining()
    {
        for (float t = 0f; t < clearFadeDuration; t += Time.unscaledDeltaTime)
        {
            smudges.SetLayerAlpha(1f - Mathf.Clamp01(t / clearFadeDuration));
            yield return null;
        }

        smudges.SetLayerAlpha(0f);
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

        if (smudges != null)
        {
            // The mud has to be told which canvas it is on before it can turn a touch into a
            // point on the card, and the panel is only guaranteed to be under one by now.
            smudges.Configure(GetComponentInParent<Canvas>());
            smudges.Rebuild();
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
    /// Puts the card back to how a run starts, without touching whether the panel itself is
    /// on — see the note in Awake. The mud is not thrown back on here: that is Open's job,
    /// so a closed panel is not sitting on a texture it will never show.
    /// </summary>
    private void ResetVisuals()
    {
        if (smudges != null)
        {
            smudges.SetArmed(false);
            smudges.SetLayerAlpha(1f);
        }

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

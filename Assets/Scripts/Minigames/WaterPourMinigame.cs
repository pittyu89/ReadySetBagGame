using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// The water-pouring minigame that runs after the quiz's water bottle question.
///
/// Three glasses are filled one at a time. The bottle for a glass only appears once the
/// previous glass is full, so there is never a choice about what to pour into — the
/// player taps and holds the bottle, water falls, and the glass fills to 500 ml.
///
/// There is nothing to fail here. It runs whether the quiz answer was right or wrong, and
/// overpouring is clamped rather than punished; the point is the beat, not another test.
///
/// <see cref="Play"/> is a coroutine so QuizHandler can simply yield on it and carry on
/// with the next question once it returns.
/// </summary>
public class WaterPourMinigame : MonoBehaviour
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
    [SerializeField, TextArea] private string instructionText = "Fill the cup by tap then hold the water Bottle";

    [Header("Glasses")]
    [Tooltip("Filled in order. Each glass is paired with the bottle at the same index.")]
    [SerializeField] private WaterCup[] cups;
    [SerializeField] private PourBottle[] bottles;
    [SerializeField] private PourStream pourStream;

    [Header("Pouring")]
    [Tooltip("Millilitres per second while the bottle is held. At 500 ml a glass, 260 " +
             "gives a glass just under two seconds.")]
    [SerializeField] private float fillRateMlPerSecond = 260f;
    [Tooltip("Beat between pressing and the water arriving, so the bottle has tipped " +
             "over before anything pours out of it.")]
    [SerializeField] private float pourStartDelay = 0.12f;

    [Header("Timing")]
    [SerializeField] private float panelFadeDuration = 0.25f;
    [SerializeField] private float instructionFadeDuration = 0.3f;
    [Tooltip("Pause after a glass fills, before its bottle leaves.")]
    [SerializeField] private float cupCompleteDelay = 0.45f;
    [Tooltip("Pause after the last glass, before the minigame closes.")]
    [SerializeField] private float finishDelay = 0.9f;

    [Header("Finish")]
    [Tooltip("Optional. Flashed once every glass is full.")]
    [SerializeField] private GameObject completedBanner;

    private bool isPlaying = false;

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

        if (cups == null || cups.Length == 0 || bottles == null || bottles.Length < cups.Length)
        {
            // Bail loudly rather than quietly: a half-wired panel would otherwise hang the
            // quiz on a glass that can never be filled.
            Debug.LogWarning("[WaterPourMinigame] Needs at least one cup and a bottle per cup — skipping.", this);
            yield break;
        }

        isPlaying = true;

        Open();

        if (backdrop != null)
            yield return StartCoroutine(backdrop.CaptureIncludingUIRoutine());

        yield return FadeGroup(panelGroup, 0f, 1f, panelFadeDuration);
        yield return FadeGroup(instructionCard, 0f, 1f, instructionFadeDuration);

        for (int i = 0; i < cups.Length; i++)
            yield return FillCup(cups[i], bottles[i]);

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
    /// Brings one bottle in and waits for the player to fill its glass.
    /// </summary>
    private IEnumerator FillCup(WaterCup cup, PourBottle bottle)
    {
        if (cup == null || bottle == null)
            yield break;

        yield return bottle.Appear();

        // Counts up while the bottle is held and resets on release, so a quick tap tips
        // the bottle without ever pouring.
        float heldFor = 0f;
        bool isStreaming = false;

        while (!cup.IsFull)
        {
            if (bottle.IsHeld)
            {
                heldFor += Time.unscaledDeltaTime;
            }
            else
            {
                heldFor = 0f;
            }

            bool shouldPour = bottle.IsHeld && heldFor >= pourStartDelay;

            if (shouldPour)
            {
                if (!isStreaming)
                {
                    if (pourStream != null)
                        pourStream.Begin();
                    cup.SetPouring(true);
                    isStreaming = true;
                }

                cup.AddMilliliters(fillRateMlPerSecond * Time.unscaledDeltaTime);

                if (pourStream != null)
                    pourStream.UpdateStream(bottle.Spout, cup);
            }
            else if (isStreaming)
            {
                StopPouring(cup);
                isStreaming = false;
            }

            yield return null;
        }

        StopPouring(cup);

        yield return new WaitForSecondsRealtime(cupCompleteDelay);

        yield return bottle.Retire();
    }

    private void StopPouring(WaterCup cup)
    {
        if (pourStream != null)
            pourStream.Stop();

        if (cup != null)
            cup.SetPouring(false);
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

        foreach (WaterCup cup in cups)
        {
            if (cup != null)
                cup.ResetCup();
        }

        foreach (PourBottle bottle in bottles)
        {
            if (bottle != null)
                bottle.Hide();
        }

        if (pourStream != null)
            pourStream.Stop();
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
        if (pourStream != null)
            pourStream.Stop();

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

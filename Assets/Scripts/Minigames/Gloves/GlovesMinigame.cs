using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The gloves minigame that runs after the quiz's gloves question.
///
/// Broken glass lies in front of the player's gloved hands. Tapping a shard puts a ring
/// on it: a white ring closes in on a blue one, and tapping as they meet picks the shard
/// up — a hand reaches out, closes on it and comes back. The hands take turns, right then
/// left, so it is never one arm doing all the work.
///
/// Tapping off the beat (or not at all) is a miss: the shard slips away to somewhere else
/// on the floor and has to be found and tried again. Nothing fails outright. Like the
/// others it runs whether the quiz answer was right or wrong, and it ends when every shard
/// has been picked up.
///
/// <see cref="Play"/> is a coroutine so QuizManager can simply yield on it and carry on
/// with the next question once it returns.
/// </summary>
public class GlovesMinigame : MonoBehaviour
{
    private enum Judgement { Great, Miss }

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
        "Tap the timing ring to clear the glass shards safely";

    [Header("Shards")]
    [Tooltip("Every shard to pick up. Each one's place in the scene is its starting spot.")]
    [SerializeField] private GlassShard[] shards = new GlassShard[0];
    [Tooltip("Where a missed shard can land. It is dropped somewhere inside this rect, " +
             "clear of the other shards.")]
    [SerializeField] private RectTransform respawnArea;
    [Tooltip("How far a relocated shard keeps from the others, in canvas units.")]
    [SerializeField] private float respawnSpacing = 90f;
    [SerializeField] private float relocateDuration = 0.35f;

    [Header("Hands")]
    [Tooltip("Reaches for the first shard, then every other one after.")]
    [SerializeField] private GlovedHand rightHand;
    [SerializeField] private GlovedHand leftHand;

    [Header("Ring")]
    [Tooltip("Moved onto the tapped shard. Holds the rings and the judgement text.")]
    [SerializeField] private RectTransform ringRoot;
    [Tooltip("The white ring that closes in.")]
    [SerializeField] private RectTransform approachRing;
    [Tooltip("The base tap it closes in on.")]
    [SerializeField] private Image targetRing;
    [Tooltip("A ring that bursts outward on a hit. Optional.")]
    [SerializeField] private Image burstRing;
    [Tooltip("GREAT JOB! / MISS, popped over the ring. Optional.")]
    [SerializeField] private TextMeshProUGUI judgementLabel;
    [Tooltip("The TAP! in the middle of the base tap. It is only up while the white ring " +
             "is on the base tap's outer ring — which is exactly when a tap counts.")]
    [SerializeField] private TextMeshProUGUI tapPrompt;
    [SerializeField] private GlovesTapArea tapArea;

    [Header("Picking")]
    [Tooltip("Catches presses that land between the shards, under the shards themselves. " +
             "A press here goes to the nearest piece of glass within pickReach. Optional, " +
             "but without it a press that misses the glass by a hair does nothing.")]
    [SerializeField] private GlovesTapArea shardPickArea;
    [Tooltip("How far from a shard a press can land and still pick it up, in canvas " +
             "units. Measured to the glass itself, not to the middle of its sprite.")]
    [SerializeField] private float pickReach = 90f;

    [Header("Timing Window")]
    [Tooltip("Scale of the white ring when it sits right on the base tap's outer ring. " +
             "With the stock sprites the white ring's middle is 213 units out and the " +
             "base tap's outer ring is 120 out at a size of 340.")]
    [SerializeField] private float hitScale = 0.56f;
    [Tooltip("Seconds from the ring appearing to it reaching the base tap's outer ring.")]
    [SerializeField] private float approachDuration = 0.85f;
    [Tooltip("How far either side of that a tap still counts, in seconds. This is also " +
             "exactly how long TAP! is up for: inside the window is GREAT JOB!, anything " +
             "else is a MISS.")]
    [SerializeField] private float hitWindow = 0.16f;
    [SerializeField] private float ringFadeInDuration = 0.1f;

    [Header("Judgement")]
    [SerializeField] private Color greatColor = new Color(150f / 255f, 176f / 255f, 0f, 1f);
    [SerializeField] private Color missColor = new Color(192f / 255f, 55f / 255f, 55f / 255f, 1f);
    [SerializeField] private float judgementDuration = 0.5f;
    [Tooltip("How far above the shard the verdict pops, in canvas units.")]
    [SerializeField] private float judgementOffset = 150f;
    [Tooltip("Keep the verdict this far below the top of the screen, clear of the " +
             "instruction card and the timer. Closer than this and it pops under the " +
             "shard instead.")]
    [SerializeField] private float judgementTopMargin = 220f;

    [Header("Timing")]
    [SerializeField] private float panelFadeDuration = 0.25f;
    [SerializeField] private float instructionFadeDuration = 0.3f;
    [Tooltip("Pause once the last shard is gone, before the minigame closes.")]
    [SerializeField] private float finishDelay = 0.9f;

    [Header("Finish")]
    [Tooltip("Optional. Flashed once every shard has been picked up.")]
    [SerializeField] private GameObject completedBanner;

    private bool isPlaying = false;

    // The shard the player pressed this frame, handed over from its event
    private GlassShard pressedShard;
    private bool tapped = false;

    // Shards a hand is already on its way to. They are still on screen until the glove
    // arrives, but are no longer up for picking.
    private readonly HashSet<GlassShard> claimed = new HashSet<GlassShard>();

    private bool rightHandNext = true;

    private CanvasGroup ringGroup;
    private Coroutine judgementRoutine;
    private Coroutine burstRoutine;

    public bool IsPlaying => isPlaying;

    private void Awake()
    {
        if (panelGroup == null && panelRoot != null)
            panelGroup = panelRoot.GetComponent<CanvasGroup>();

        if (ringRoot != null)
        {
            ringGroup = ringRoot.GetComponent<CanvasGroup>();
            if (ringGroup == null)
                ringGroup = ringRoot.gameObject.AddComponent<CanvasGroup>();

            // The ring is only ever looked at; the tap area catches the tap
            ringGroup.blocksRaycasts = false;
            ringGroup.interactable = false;
        }

        if (instructionLabel != null && !string.IsNullOrEmpty(instructionText))
            instructionLabel.text = instructionText;

        // Reset the contents but leave the panel's active state alone. Awake first runs
        // during the SetActive in Open, so deactivating here would switch the panel back
        // off underneath the very coroutine that just turned it on.
        ResetVisuals();
    }

    private void OnEnable()
    {
        foreach (GlassShard shard in shards)
            if (shard != null)
                shard.Pressed += OnShardPressed;

        if (tapArea != null)
            tapArea.Tapped += OnTapped;

        if (shardPickArea != null)
            shardPickArea.TappedAt += OnPickedNear;
    }

    private void OnDisable()
    {
        foreach (GlassShard shard in shards)
            if (shard != null)
                shard.Pressed -= OnShardPressed;

        if (tapArea != null)
            tapArea.Tapped -= OnTapped;

        if (shardPickArea != null)
            shardPickArea.TappedAt -= OnPickedNear;
    }

    /// <summary>
    /// Runs the whole minigame and returns once it has closed.
    /// Yield on this from the quiz; it never returns early or leaves the panel up.
    /// </summary>
    public IEnumerator Play()
    {
        if (isPlaying)
            yield break;

        if (shards == null || shards.Length == 0 || rightHand == null || leftHand == null ||
            ringRoot == null || approachRing == null || tapArea == null)
        {
            // Bail loudly rather than quietly: a half-wired panel would otherwise hang the
            // quiz on shards that can never be picked up.
            Debug.LogWarning("[GlovesMinigame] Needs shards, both hands, the ring and the " +
                             "tap area — skipping.", this);
            yield break;
        }

        isPlaying = true;

        Open();

        if (backdrop != null)
            yield return StartCoroutine(backdrop.CaptureIncludingUIRoutine());

        yield return FadeGroup(panelGroup, 0f, 1f, panelFadeDuration);
        yield return FadeGroup(instructionCard, 0f, 1f, instructionFadeDuration);

        while (RemainingShards() > 0)
        {
            ArmShards(true);
            pressedShard = null;

            while (pressedShard == null)
                yield return null;

            GlassShard shard = pressedShard;
            pressedShard = null;
            ArmShards(false);

            Judgement judgement = Judgement.Miss;
            yield return RunRing(shard, j => judgement = j);

            ShowJudgement(judgement);

            if (judgement == Judgement.Miss)
            {
                yield return FadeRingOut(0.12f);
                yield return shard.Relocate(PickRespawnPosition(shard), relocateDuration);
                continue;
            }

            PlayBurst(judgement);
            StartCoroutine(FadeRingOut(0.15f));

            claimed.Add(shard);

            GlovedHand hand = rightHandNext ? rightHand : leftHand;
            rightHandNext = !rightHandNext;

            // The other hand may still be coming back from the shard before last
            while (hand.IsBusy)
                yield return null;

            StartCoroutine(hand.Reach(shard));

            // The next shard can be picked while this hand is still out: the other hand
            // is free, and waiting for the return would only slow the round down
            yield return new WaitForSecondsRealtime(0.12f);
        }

        while (rightHand.IsBusy || leftHand.IsBusy)
            yield return null;

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
    /// Puts the rings on the shard, closes the white one in on the blue one, and reports
    /// how close to the meeting the player tapped. Not tapping at all is a miss once the
    /// white ring is past the last moment that could still count.
    /// </summary>
    private IEnumerator RunRing(GlassShard shard, System.Action<Judgement> report)
    {
        ringRoot.gameObject.SetActive(true);
        ringRoot.position = shard.Rect.position;

        if (targetRing != null)
        {
            targetRing.gameObject.SetActive(true);
            targetRing.rectTransform.localScale = Vector3.one;
            targetRing.color = Color.white;
        }

        approachRing.gameObject.SetActive(true);
        approachRing.localScale = Vector3.one;

        if (ringGroup != null)
            ringGroup.alpha = 0f;

        tapped = false;
        tapArea.SetArmed(true);

        if (tapPrompt != null)
            tapPrompt.gameObject.SetActive(false);

        // Linear in time, so the ring closes at a steady rate the player can read, and
        // carries on past the base tap for the late half of the window
        float lastChance = approachDuration + hitWindow;
        float shrinkPerSecond = (1f - hitScale) / approachDuration;

        Judgement result = Judgement.Miss;

        for (float t = 0f; ; t += Time.unscaledDeltaTime)
        {
            if (ringGroup != null)
                ringGroup.alpha = ringFadeInDuration > 0f ? Mathf.Clamp01(t / ringFadeInDuration) : 1f;

            float scale = Mathf.Max(0.05f, 1f - shrinkPerSecond * t);
            approachRing.localScale = new Vector3(scale, scale, 1f);

            // TAP! is the window made visible: it is up for exactly as long as a tap
            // would count, which is while the white ring is over the outer ring
            bool inWindow = Mathf.Abs(t - approachDuration) <= hitWindow;
            if (tapPrompt != null && tapPrompt.gameObject.activeSelf != inWindow)
                tapPrompt.gameObject.SetActive(inWindow);

            if (tapped)
            {
                result = inWindow ? Judgement.Great : Judgement.Miss;
                break;
            }

            if (t > lastChance)
                break;

            yield return null;
        }

        tapArea.SetArmed(false);

        if (tapPrompt != null)
            tapPrompt.gameObject.SetActive(false);

        // A hit snaps the white ring onto the blue one, so the moment reads as a lock-in
        if (result != Judgement.Miss)
            approachRing.localScale = new Vector3(hitScale, hitScale, 1f);

        report(result);
    }

    private IEnumerator FadeRingOut(float duration)
    {
        if (ringGroup == null)
        {
            HideRing();
            yield break;
        }

        float from = ringGroup.alpha;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            ringGroup.alpha = Mathf.Lerp(from, 0f, t / duration);
            yield return null;
        }

        HideRing();
    }

    private void HideRing()
    {
        if (ringGroup != null)
            ringGroup.alpha = 0f;

        if (approachRing != null)
            approachRing.gameObject.SetActive(false);

        if (targetRing != null)
            targetRing.gameObject.SetActive(false);
    }

    /// <summary>
    /// The hit rings out: a ring swells and fades away from the base tap, the way a rhythm
    /// game flashes the note that was hit. Only a good tap earns one.
    /// </summary>
    private void PlayBurst(Judgement judgement)
    {
        if (burstRing == null || judgement != Judgement.Great)
            return;

        if (burstRoutine != null)
            StopCoroutine(burstRoutine);

        burstRoutine = StartCoroutine(BurstRoutine());
    }

    private IEnumerator BurstRoutine()
    {
        RectTransform rt = burstRing.rectTransform;

        // Kept where it went off rather than riding along with the ring root, which moves
        // on to the next shard
        rt.SetParent(ringRoot.parent, true);
        rt.position = ringRoot.position;
        rt.SetAsLastSibling();
        burstRing.gameObject.SetActive(true);

        Color tint = greatColor;
        const float endScale = 1.5f;
        const float duration = 0.35f;

        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.Clamp01(t / duration);
            float eased = 1f - Mathf.Pow(1f - k, 2f);
            float s = Mathf.Lerp(1f, endScale, eased);
            rt.localScale = new Vector3(s, s, 1f);
            burstRing.color = new Color(tint.r, tint.g, tint.b, 1f - k);
            yield return null;
        }

        burstRing.gameObject.SetActive(false);
        rt.SetParent(ringRoot, false);
        rt.localPosition = Vector3.zero;
        burstRoutine = null;
    }

    private void ShowJudgement(Judgement judgement)
    {
        if (judgementLabel == null)
            return;

        if (judgementRoutine != null)
            StopCoroutine(judgementRoutine);

        judgementRoutine = StartCoroutine(JudgementRoutine(judgement));
    }

    /// <summary>
    /// Pops the verdict in above the shard, then lets it drift up and fade. Parented out
    /// of the ring for the same reason as the burst.
    /// </summary>
    private IEnumerator JudgementRoutine(Judgement judgement)
    {
        RectTransform rt = judgementLabel.rectTransform;
        rt.SetParent(ringRoot.parent, true);
        rt.SetAsLastSibling();

        // Above the shard, unless that would put it up among the instruction card and the
        // timer, in which case it goes underneath instead
        RectTransform space = ringRoot.parent as RectTransform;
        float above = judgementOffset;
        if (space != null && space.InverseTransformPoint(ringRoot.position).y + judgementOffset > space.rect.yMax - judgementTopMargin)
            above = -judgementOffset;

        Vector3 start = ringRoot.position + ringRoot.parent.TransformVector(new Vector3(0f, above, 0f));
        Vector3 rise = ringRoot.parent.TransformVector(new Vector3(0f, 40f, 0f));

        if (judgement == Judgement.Great)
        {
            judgementLabel.text = "GREAT JOB!";
            judgementLabel.color = greatColor;
        }
        else
        {
            judgementLabel.text = "MISS";
            judgementLabel.color = missColor;
        }

        judgementLabel.gameObject.SetActive(true);
        Color c = judgementLabel.color;

        for (float t = 0f; t < judgementDuration; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.Clamp01(t / judgementDuration);

            // Overshoots to 1.25 in the first fifth, settles, then fades over the last half
            float pop = k < 0.2f ? Mathf.Lerp(0.6f, 1.25f, k / 0.2f) : Mathf.Lerp(1.25f, 1f, Mathf.Clamp01((k - 0.2f) / 0.2f));
            rt.localScale = new Vector3(pop, pop, 1f);
            rt.position = start + rise * k;
            judgementLabel.color = new Color(c.r, c.g, c.b, k < 0.5f ? 1f : 1f - (k - 0.5f) / 0.5f);
            yield return null;
        }

        judgementLabel.gameObject.SetActive(false);
        rt.SetParent(ringRoot, false);
        judgementRoutine = null;
    }

    /// <summary>
    /// Somewhere in the respawn area clear of the other shards. Falls back to the best of
    /// the tries if the floor is too crowded for any of them to be fully clear.
    /// </summary>
    private Vector2 PickRespawnPosition(GlassShard moving)
    {
        RectTransform shardParent = moving.Rect.parent as RectTransform;
        if (respawnArea == null || shardParent == null)
            return moving.Rect.anchoredPosition;

        Rect area = respawnArea.rect;
        Vector2 best = moving.Rect.anchoredPosition;
        float bestClearance = -1f;

        for (int attempt = 0; attempt < 30; attempt++)
        {
            Vector3 local = new Vector3(Random.Range(area.xMin, area.xMax), Random.Range(area.yMin, area.yMax), 0f);
            Vector3 world = respawnArea.TransformPoint(local);
            Vector2 candidate = shardParent.InverseTransformPoint(world);

            // anchoredPosition is measured from the anchor, not the parent's pivot
            candidate += moving.Rect.anchoredPosition - (Vector2)moving.Rect.localPosition;

            float clearance = float.MaxValue;
            foreach (GlassShard other in shards)
            {
                if (other == null || other == moving || !other.gameObject.activeSelf)
                    continue;

                clearance = Mathf.Min(clearance, Vector2.Distance(candidate, other.Rect.anchoredPosition));
            }

            // Not back on the spot it just slipped from, either
            clearance = Mathf.Min(clearance, Vector2.Distance(candidate, moving.Rect.anchoredPosition));

            if (clearance >= respawnSpacing)
                return candidate;

            if (clearance > bestClearance)
            {
                bestClearance = clearance;
                best = candidate;
            }
        }

        return best;
    }

    private int RemainingShards()
    {
        int count = 0;
        foreach (GlassShard shard in shards)
            if (shard != null && !shard.IsCollected && !claimed.Contains(shard))
                count++;
        return count;
    }

    private void ArmShards(bool armed)
    {
        // Listens only while a shard is up for picking, so it never swallows the timing tap
        if (shardPickArea != null)
            shardPickArea.SetArmed(armed);

        foreach (GlassShard shard in shards)
        {
            if (shard == null)
                continue;

            shard.SetArmed(armed && !shard.IsCollected && !claimed.Contains(shard));
        }
    }

    private void OnShardPressed(GlassShard shard)
    {
        if (!isPlaying || pressedShard != null)
            return;

        pressedShard = shard;
    }

    /// <summary>
    /// A press that landed on no shard at all. The shards are small and the gaps between
    /// them are smaller, so rather than swallowing it, the nearest piece of glass within
    /// reach takes it.
    /// </summary>
    private void OnPickedNear(Vector2 screenPoint)
    {
        if (!isPlaying || pressedShard != null)
            return;

        RectTransform space = shards[0] != null ? shards[0].Rect.parent as RectTransform : null;
        if (space == null)
            return;

        Camera cam = null;
        Canvas canvas = shardPickArea.GetComponentInParent<Canvas>();
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(space, screenPoint, cam, out local))
            return;

        // anchoredPosition is measured from the anchor, not the parent's pivot
        if (shards[0] != null)
            local += shards[0].Rect.anchoredPosition - (Vector2)shards[0].Rect.localPosition;

        GlassShard best = null;
        float bestDistance = pickReach;

        foreach (GlassShard shard in shards)
        {
            if (shard == null || shard.IsCollected || claimed.Contains(shard) || !shard.gameObject.activeSelf)
                continue;

            float distance = Vector2.Distance(local, shard.ContentCenter);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = shard;
            }
        }

        if (best != null)
            pressedShard = best;
    }

    private void OnTapped()
    {
        if (isPlaying)
            tapped = true;
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
    /// Puts the shards, the hands and the ring back to how a run starts, without touching
    /// whether the panel itself is on — see the note in Awake.
    /// </summary>
    private void ResetVisuals()
    {
        StopAllCoroutinesExceptPlay();

        pressedShard = null;
        tapped = false;
        claimed.Clear();
        rightHandNext = true;

        foreach (GlassShard shard in shards)
            if (shard != null)
                shard.ResetShard();

        if (rightHand != null)
            rightHand.ResetHand();

        if (leftHand != null)
            leftHand.ResetHand();

        if (tapArea != null)
            tapArea.SetArmed(false);

        if (shardPickArea != null)
            shardPickArea.SetArmed(false);

        if (tapPrompt != null)
            tapPrompt.gameObject.SetActive(false);

        if (burstRing != null)
        {
            burstRing.gameObject.SetActive(false);
            if (ringRoot != null && burstRing.rectTransform.parent != ringRoot)
                burstRing.rectTransform.SetParent(ringRoot, false);
            burstRing.rectTransform.localPosition = Vector3.zero;
        }

        if (judgementLabel != null)
        {
            judgementLabel.gameObject.SetActive(false);
            if (ringRoot != null && judgementLabel.rectTransform.parent != ringRoot)
                judgementLabel.rectTransform.SetParent(ringRoot, false);
        }

        HideRing();

        if (ringRoot != null)
            ringRoot.gameObject.SetActive(false);

        if (completedBanner != null)
            completedBanner.SetActive(false);
    }

    // The burst and the judgement run alongside Play, so a reset has to stop them by hand
    // without stopping Play itself mid-run
    private void StopAllCoroutinesExceptPlay()
    {
        if (burstRoutine != null)
        {
            StopCoroutine(burstRoutine);
            burstRoutine = null;
        }

        if (judgementRoutine != null)
        {
            StopCoroutine(judgementRoutine);
            judgementRoutine = null;
        }
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

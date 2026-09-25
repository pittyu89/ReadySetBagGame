using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// The toiletries minigame that runs after the quiz's toiletries question.
///
/// The toiletries lie in a heap beside an open toiletry bag whose lining has a silhouette
/// for each one. The player drags each item onto its silhouette; dropped on the right one
/// it slides in and turns itself to fit, dropped on the wrong one it shakes its head and
/// goes back to the heap. Dropped anywhere else it just goes back.
///
/// Like the others there is nothing to fail. It runs whether the quiz answer was right or
/// wrong, and it ends once every item is in its place.
///
/// <see cref="Play"/> is a coroutine so QuizManager can simply yield on it and carry on
/// with the next question once it returns.
/// </summary>
public class ToiletriesMinigame : MonoBehaviour
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
    [SerializeField, TextArea] private string instructionText = "Put each item in its holder";

    [Header("Items")]
    [Tooltip("Every item to pack. Each one names its own silhouette in the bag.")]
    [SerializeField] private ToiletryItem[] items = new ToiletryItem[0];
    [Tooltip("How far outside a silhouette a drop still counts as on it, in canvas units. " +
             "The thin ones — the toothbrush especially — are hard to hit exactly.")]
    [SerializeField] private float dropPadding = 24f;

    [Header("Timing")]
    [SerializeField] private float snapDuration = 0.18f;
    [SerializeField] private float shakeDuration = 0.35f;
    [SerializeField] private float shakeDistance = 18f;
    [SerializeField] private float returnDuration = 0.25f;
    [SerializeField] private float panelFadeDuration = 0.25f;
    [SerializeField] private float instructionFadeDuration = 0.3f;
    [Tooltip("Pause once the last item is in, before the minigame closes.")]
    [SerializeField] private float finishDelay = 0.9f;

    [Header("Finish")]
    [Tooltip("Optional. Flashed once every item is packed.")]
    [SerializeField] private GameObject completedBanner;

    private bool isPlaying = false;
    private Canvas canvas;

    public bool IsPlaying => isPlaying;

    private void Awake()
    {
        if (panelGroup == null && panelRoot != null)
            panelGroup = panelRoot.GetComponent<CanvasGroup>();

        canvas = GetComponentInParent<Canvas>();

        if (instructionLabel != null && !string.IsNullOrEmpty(instructionText))
            instructionLabel.text = instructionText;

        // Reset the contents but leave the panel's active state alone. Awake first runs
        // during the SetActive in Open, so deactivating here would switch the panel back
        // off underneath the very coroutine that just turned it on.
        ResetVisuals();
    }

    private void OnEnable()
    {
        foreach (ToiletryItem item in items)
        {
            if (item == null)
                continue;

            item.PickedUp += OnPickedUp;
            item.Dropped += OnDropped;
        }
    }

    private void OnDisable()
    {
        foreach (ToiletryItem item in items)
        {
            if (item == null)
                continue;

            item.PickedUp -= OnPickedUp;
            item.Dropped -= OnDropped;
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

        if (items == null || items.Length == 0)
        {
            Debug.LogWarning("[ToiletriesMinigame] Needs items to pack — skipping.", this);
            yield break;
        }

        foreach (ToiletryItem item in items)
        {
            if (item == null || item.TargetSlot == null)
            {
                // Bail loudly rather than quietly: an item with nowhere to go would hang
                // the quiz on a bag that can never be finished.
                Debug.LogWarning("[ToiletriesMinigame] Every item needs a silhouette — skipping.", this);
                yield break;
            }
        }

        isPlaying = true;

        Open();

        if (backdrop != null)
            yield return StartCoroutine(backdrop.CaptureIncludingUIRoutine());

        yield return FadeGroup(panelGroup, 0f, 1f, panelFadeDuration);
        yield return FadeGroup(instructionCard, 0f, 1f, instructionFadeDuration);

        ArmLooseItems();

        while (!AllPlaced())
            yield return null;

        // Let the last one finish sliding in before the banner covers it
        yield return new WaitForSecondsRealtime(snapDuration);

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

    private void OnPickedUp(ToiletryItem item)
    {
        // Carried over everything else in the heap
        item.Rect.SetAsLastSibling();
    }

    private void OnDropped(ToiletryItem item, PointerEventData e)
    {
        if (!isPlaying)
            return;

        ToiletryItem owner = SlotUnder(e.position);

        if (owner == item)
        {
            // Placed items sink under the heap, so they never cover one still to pack
            item.Rect.SetAsFirstSibling();
            StartCoroutine(item.SnapIntoPlace(snapDuration));
            return;
        }

        if (owner != null)
            StartCoroutine(ReturnThenRearm(item, item.ShakeAndReturn(shakeDuration, shakeDistance, returnDuration)));
        else
            StartCoroutine(ReturnThenRearm(item, item.ReturnHome(returnDuration)));
    }

    private IEnumerator ReturnThenRearm(ToiletryItem item, IEnumerator move)
    {
        yield return StartCoroutine(move);

        if (isPlaying)
            item.SetArmed(true);
    }

    /// <summary>
    /// The item whose empty silhouette is under the finger, or null for none. A silhouette
    /// already filled no longer counts, so dropping onto a packed item just sends the
    /// carried one back. Where padded silhouettes overlap, the nearest centre wins.
    /// </summary>
    private ToiletryItem SlotUnder(Vector2 screenPoint)
    {
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        ToiletryItem best = null;
        float bestDistance = float.MaxValue;

        foreach (ToiletryItem candidate in items)
        {
            if (candidate == null || candidate.IsPlaced || candidate.TargetSlot == null)
                continue;

            RectTransform slot = candidate.TargetSlot;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(slot, screenPoint, cam, out local))
                continue;

            Rect area = slot.rect;
            area.xMin -= dropPadding;
            area.yMin -= dropPadding;
            area.xMax += dropPadding;
            area.yMax += dropPadding;

            if (!area.Contains(local))
                continue;

            float distance = Vector2.Distance(local, slot.rect.center);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    private bool AllPlaced()
    {
        foreach (ToiletryItem item in items)
            if (item != null && !item.IsPlaced)
                return false;
        return true;
    }

    private void ArmLooseItems()
    {
        foreach (ToiletryItem item in items)
            if (item != null)
                item.SetArmed(!item.IsPlaced);
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
    /// Puts every item back on the heap in its authored order, without touching whether the
    /// panel itself is on — see the note in Awake.
    /// </summary>
    private void ResetVisuals()
    {
        foreach (ToiletryItem item in items)
        {
            if (item == null)
                continue;

            item.ResetItem();

            // Restores the authored stacking, which placing and dragging reshuffle
            item.Rect.SetAsLastSibling();
        }

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
    /// QuizManager stops the Play coroutine itself; this clears everything it left up.
    /// </summary>
    public void ForceClose()
    {
        StopAllCoroutines();
        Close();
        isPlaying = false;
    }
}

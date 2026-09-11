using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Plays the MainScene intro animation: the contents of the left menu panel fade in
/// one at a time from top to bottom, while the player card slides in from off-screen right.
/// Attach to a persistent object in MainScene (e.g. the UIManager object).
/// </summary>
public class MainMenuIntroAnimator : MonoBehaviour
{
    [Header("Left Menu Fade")]
    [SerializeField] private Transform menuContent;          // LeftPanelBackground/MenuContent
    [SerializeField] private float menuStartDelay = 0.05f;   // Wait before the first item fades in
    [SerializeField] private float menuFadeDuration = 0.55f; // Fade time for a single item
    [SerializeField] private float menuStagger = 0.1f;       // Gap between consecutive items
    [SerializeField] private float menuSlideDistance = 28f;   // Pixels each item slides up during fade

    [Header("Player Card Slide")]
    [SerializeField] private RectTransform playerCard;
    [SerializeField] private float cardStartDelay = 0.15f;

    // Sized so the card settles on the same frame as the last menu item:
    // menuStartDelay + (items - 1) * menuStagger + menuFadeDuration == cardStartDelay + this.
    // Retune it alongside the menu timings, or the two halves stop landing together.
    [SerializeField] private float cardSlideDuration = 1f;
    [SerializeField] private float cardSlideDistance = 0f;   // 0 = auto (card width + margin)

    private readonly List<CanvasGroup> menuGroups = new List<CanvasGroup>();
    private readonly List<RectTransform> menuRects = new List<RectTransform>();
    private readonly List<Vector2> menuTargetPositions = new List<Vector2>();
    private CanvasGroup cardGroup;
    private Vector2 cardTargetPos;
    private Vector2 cardStartPos;

    private LayoutGroup menuLayout;   // Suspended while the slide runs so it does not fight us
    private int runningMenuFades;
    private bool introStarted;

    [Tooltip("When true, the intro waits for PlayIntro() to be called (e.g. by " +
             "VideoBackgroundIntro after the loading screen). When false, it auto-plays in Start.")]
    [SerializeField] private bool waitForTrigger = true;

    // Hide everything in Awake so nothing flashes on the first rendered frame
    private void Awake()
    {
        PrepareMenu();
        PrepareCard();
    }

    private void Start()
    {
        if (!waitForTrigger)
            PlayIntro();
    }

    /// <summary>
    /// Kicks off the menu fade + card slide. Safe to call multiple times — only the first
    /// call does anything.
    /// </summary>
    public void PlayIntro()
    {
        if (introStarted)
            return;

        introStarted = true;

        if (menuGroups.Count > 0)
        {
            CaptureMenuTargets();
            StartCoroutine(FadeMenuSequence());
        }

        if (playerCard != null)
            StartCoroutine(SlideCardIn());
    }

    /// <summary>
    /// Collects the menu items in sibling order (which the VerticalLayoutGroup
    /// renders top to bottom) and hides them.
    ///
    /// Only the alpha is touched here. Positions are deliberately left alone: this runs in
    /// Awake, before the layout group has laid the column out, so every item still reports the
    /// authored anchoredPosition — (0,0) in MainScene. Reading targets at this point pinned the
    /// whole menu to the panel's top-left corner once the fade coroutines started writing those
    /// bogus targets back every frame. See CaptureMenuTargets.
    /// </summary>
    private void PrepareMenu()
    {
        if (menuContent == null)
            return;

        menuLayout = menuContent.GetComponent<LayoutGroup>();

        for (int i = 0; i < menuContent.childCount; i++)
        {
            Transform child = menuContent.GetChild(i);
            if (!child.gameObject.activeSelf)
                continue;

            CanvasGroup group = child.GetComponent<CanvasGroup>();
            if (group == null)
                group = child.gameObject.AddComponent<CanvasGroup>();

            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            menuGroups.Add(group);

            menuRects.Add(child.GetComponent<RectTransform>());
        }
    }

    /// <summary>
    /// Records where each item belongs and parks it below that spot, ready to slide up.
    ///
    /// Runs at PlayIntro time rather than in Awake so the layout group has already placed the
    /// column. The rebuild is forced rather than awaited because the intro can be triggered
    /// mid-frame, and a pending rebuild would hand back stale positions.
    ///
    /// The layout group is then suspended for the duration: it rewrites its children's
    /// anchoredPosition on every rebuild, so leaving it on means it and the slide overwrite each
    /// other frame by frame. It is switched back on in FinishMenuSlide, which snaps everything
    /// back to the exact layout positions.
    /// </summary>
    private void CaptureMenuTargets()
    {
        RectTransform contentRect = menuContent as RectTransform;
        if (contentRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);

        menuTargetPositions.Clear();

        for (int i = 0; i < menuRects.Count; i++)
        {
            RectTransform rect = menuRects[i];
            Vector2 target = rect != null ? rect.anchoredPosition : Vector2.zero;
            menuTargetPositions.Add(target);
        }

        if (menuLayout != null)
            menuLayout.enabled = false;

        for (int i = 0; i < menuRects.Count; i++)
        {
            if (menuRects[i] != null)
                menuRects[i].anchoredPosition = menuTargetPositions[i] + new Vector2(0f, -menuSlideDistance);
        }
    }

    /// <summary>
    /// Hands the column back to the layout group once every item has landed.
    /// </summary>
    private void FinishMenuSlide()
    {
        runningMenuFades = 0;

        if (menuLayout != null)
            menuLayout.enabled = true;
    }

    /// <summary>
    /// Records where the card belongs, then parks it off-screen to the right.
    /// </summary>
    private void PrepareCard()
    {
        if (playerCard == null)
            return;

        cardGroup = playerCard.GetComponent<CanvasGroup>();
        if (cardGroup == null)
            cardGroup = playerCard.gameObject.AddComponent<CanvasGroup>();

        cardTargetPos = playerCard.anchoredPosition;

        // Auto distance: enough to clear the card's own width plus its margin from the edge
        float distance = cardSlideDistance > 0f
            ? cardSlideDistance
            : playerCard.rect.width + Mathf.Abs(cardTargetPos.x) + 40f;

        cardStartPos = cardTargetPos + new Vector2(distance, 0f);

        playerCard.anchoredPosition = cardStartPos;
        cardGroup.alpha = 0f;
        cardGroup.interactable = false;
        cardGroup.blocksRaycasts = false;
    }

    /// <summary>
    /// Time to advance the intro by this frame.
    ///
    /// The intro fires right after a scene load and a video start, so the first frame or two can
    /// carry a delta of several hundred milliseconds. Feeding that straight in ate most of the
    /// fade in one step and made the first items appear to pop rather than fade. Capping the step
    /// costs a few milliseconds of wall-clock accuracy during a hitch and keeps the motion even.
    /// </summary>
    private static float Step()
    {
        return Mathf.Min(Time.unscaledDeltaTime, MaxStep);
    }

    private const float MaxStep = 1f / 20f;

    /// <summary>Eases in and out — no sudden start, no abrupt stop.</summary>
    private static float SmoothStep(float t)
    {
        return t * t * (3f - 2f * t);
    }

    /// <summary>Starts fast and decelerates into the target.</summary>
    private static float EaseOutQuart(float t)
    {
        float inv = 1f - t;
        return 1f - inv * inv * inv * inv;
    }

    private IEnumerator FadeMenuSequence()
    {
        if (menuStartDelay > 0f)
            yield return new WaitForSecondsRealtime(menuStartDelay);

        for (int i = 0; i < menuGroups.Count; i++)
        {
            runningMenuFades++;
            StartCoroutine(FadeSlideIn(i, menuFadeDuration));

            if (i < menuGroups.Count - 1 && menuStagger > 0f)
                yield return new WaitForSecondsRealtime(menuStagger);
        }
    }

    private IEnumerator FadeSlideIn(int index, float duration)
    {
        CanvasGroup group = menuGroups[index];
        RectTransform rect = menuRects[index];
        Vector2 targetPos = menuTargetPositions[index];
        Vector2 startPos = targetPos + new Vector2(0f, -menuSlideDistance);

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Step();
            float progress = Mathf.Clamp01(elapsed / duration);

            // The two channels get different curves on purpose. An ease-out on the alpha spends
            // most of its time near full opacity, so the item reads as "already there" and the
            // fade looks clipped; smoothstep eases in and out, so it grows in evenly.
            if (group != null)
                group.alpha = SmoothStep(progress);

            // Position keeps the ease-out, now quart instead of cubic, so it carries a little
            // more speed up front and settles more gently at the end.
            if (rect != null)
                rect.anchoredPosition = Vector2.LerpUnclamped(startPos, targetPos, EaseOutQuart(progress));

            yield return null;
        }

        if (group != null)
        {
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;
        }

        if (rect != null)
            rect.anchoredPosition = targetPos;

        runningMenuFades--;
        if (runningMenuFades <= 0)
            FinishMenuSlide();
    }

    private IEnumerator SlideCardIn()
    {
        if (cardStartDelay > 0f)
            yield return new WaitForSecondsRealtime(cardStartDelay);

        float elapsed = 0f;

        while (elapsed < cardSlideDuration)
        {
            elapsed += Step();
            float progress = Mathf.Clamp01(elapsed / cardSlideDuration);

            if (playerCard != null)
                playerCard.anchoredPosition = Vector2.LerpUnclamped(cardStartPos, cardTargetPos, EaseOutQuart(progress));

            // The card is opaque well before it stops moving. Fading over the whole slide left it
            // visibly translucent while it was already almost in place, which read as a stutter.
            if (cardGroup != null)
                cardGroup.alpha = SmoothStep(Mathf.Clamp01(progress / 0.6f));

            yield return null;
        }

        SnapCardToTarget();
    }

    /// <summary>
    /// Immediately finishes the intro. Safe to call at any point.
    /// </summary>
    public void SkipIntro()
    {
        StopAllCoroutines();
        introStarted = true;

        for (int i = 0; i < menuGroups.Count; i++)
        {
            if (menuGroups[i] != null)
            {
                menuGroups[i].alpha = 1f;
                menuGroups[i].interactable = true;
                menuGroups[i].blocksRaycasts = true;
            }

            // Empty when the intro is skipped before it ever played — the layout group is still
            // in charge of the positions in that case, so there is nothing to restore.
            if (i < menuTargetPositions.Count && i < menuRects.Count && menuRects[i] != null)
                menuRects[i].anchoredPosition = menuTargetPositions[i];
        }

        FinishMenuSlide();
        SnapCardToTarget();
    }

    private void SnapCardToTarget()
    {
        if (playerCard != null)
            playerCard.anchoredPosition = cardTargetPos;

        if (cardGroup != null)
        {
            cardGroup.alpha = 1f;
            cardGroup.interactable = true;
            cardGroup.blocksRaycasts = true;
        }
    }
}

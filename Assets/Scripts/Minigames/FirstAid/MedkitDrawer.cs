using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The first-aid kit at the edge of the screen, which the player swipes up to open.
///
/// The kit starts pushed down off the bottom of the screen with only its handle showing,
/// as though it were sitting just out of frame. A swipe pulls it up into view, and it stays
/// up for the rest of the round — the swipe is an opening beat rather than something to
/// repeat.
///
/// There is only one piece of kit art, the open case. Nothing is swapped; the kit is simply
/// out of sight until it is pulled up.
///
/// Input goes through the EventSystem rather than polling Input, so it behaves the same on
/// a phone and in the Editor.
/// </summary>
public class MedkitDrawer : MonoBehaviour, IDragHandler, IPointerUpHandler
{
    [Header("Pieces")]
    [Tooltip("The whole kit, slid up by the swipe. Usually this object's own transform.")]
    [SerializeField] private RectTransform kitRoot;
    [Tooltip("What is packed in the kit. Fades up as the case comes into view.")]
    [SerializeField] private CanvasGroup contents;
    [Tooltip("Optional. A nudge to swipe, hidden once the kit has been opened.")]
    [SerializeField] private GameObject swipeHint;

    [Header("Peek")]
    [Tooltip("How far below its resting spot the kit sits before the swipe, in canvas " +
             "units. Enough that only the handle clears the bottom of the screen.")]
    [SerializeField] private float peekDrop = 300f;
    [SerializeField] private float openDuration = 0.32f;

    [Header("Swipe")]
    [Tooltip("How far up the finger has to travel before the kit counts as opened.")]
    [SerializeField] private float swipeThreshold = 60f;

    [Header("Hint")]
    [Tooltip("How far the nudge arrow rides up and down, in canvas units. 0 holds it still.")]
    [SerializeField] private float hintBobHeight = 10f;
    [SerializeField] private float hintBobSpeed = 3.4f;

    /// <summary>Raised once, the moment the kit finishes coming up.</summary>
    public event Action Opened;

    public bool IsOpen { get; private set; }

    // Where the kit rests once it is up. Everything else is measured down from here.
    private Vector2 openPosition;
    private bool hasOpenPosition = false;

    private float hintRestY = 0f;
    private bool hasHintRest = false;

    private float accumulatedSwipe = 0f;
    private float openTimer = 0f;
    private bool isOpening = false;

    private void Awake()
    {
        EnsureInit();
        ResetDrawer();
    }

    /// <summary>
    /// Caches the authored resting places the first time anything needs them. Not left to
    /// Awake alone, because the panel resets its kit on the way through its own Awake,
    /// which can land before this one.
    /// </summary>
    private void EnsureInit()
    {
        if (kitRoot == null)
            kitRoot = transform as RectTransform;

        if (!hasOpenPosition && kitRoot != null)
        {
            openPosition = kitRoot.anchoredPosition;
            hasOpenPosition = true;
        }

        if (!hasHintRest && swipeHint != null)
        {
            RectTransform hint = swipeHint.transform as RectTransform;
            if (hint != null)
            {
                hintRestY = hint.anchoredPosition.y;
                hasHintRest = true;
            }
        }
    }

    private void Update()
    {
        BobHint();

        if (!isOpening || kitRoot == null)
            return;

        openTimer += Time.unscaledDeltaTime;
        float k = openDuration <= 0f ? 1f : Mathf.Clamp01(openTimer / openDuration);

        // Ease out, so the kit settles into place rather than arriving at full speed
        float eased = 1f - (1f - k) * (1f - k);

        kitRoot.anchoredPosition = Vector2.Lerp(openPosition - new Vector2(0f, peekDrop), openPosition, eased);

        if (contents != null)
            contents.alpha = eased;

        if (k < 1f)
            return;

        isOpening = false;
        IsOpen = true;
        kitRoot.anchoredPosition = openPosition;

        if (contents != null)
        {
            contents.alpha = 1f;
            contents.blocksRaycasts = true;
        }

        Opened?.Invoke();
    }

    /// <summary>
    /// Rides the nudge arrow up and down while the kit is still down, so a swipe reads as
    /// the thing to do rather than something to guess at.
    /// </summary>
    private void BobHint()
    {
        if (swipeHint == null || IsOpen || hintBobHeight <= 0f)
            return;

        RectTransform rect = swipeHint.transform as RectTransform;
        if (rect == null)
            return;

        float bob = Mathf.Sin(Time.unscaledTime * hintBobSpeed) * hintBobHeight;
        rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, hintRestY + bob);
    }

    /// <summary>
    /// Drops the kit back out of sight with nothing reachable in it. Used to set up before
    /// a run.
    /// </summary>
    public void ResetDrawer()
    {
        EnsureInit();

        IsOpen = false;
        isOpening = false;
        openTimer = 0f;
        accumulatedSwipe = 0f;

        if (kitRoot != null)
            kitRoot.anchoredPosition = openPosition - new Vector2(0f, peekDrop);

        if (contents != null)
        {
            contents.alpha = 0f;
            contents.blocksRaycasts = false;
        }

        if (swipeHint != null)
            swipeHint.SetActive(true);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (IsOpen || isOpening)
            return;

        // Upward travel only, and it accumulates: a short flick opens the kit as surely as
        // one long drag, which is how a swipe is expected to feel on a phone.
        if (eventData.delta.y > 0f)
            accumulatedSwipe += eventData.delta.y;

        if (accumulatedSwipe >= swipeThreshold)
            BeginOpen();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        // A swipe that fell short is not held against the next attempt
        if (!IsOpen && !isOpening)
            accumulatedSwipe = 0f;
    }

    private void BeginOpen()
    {
        isOpening = true;
        openTimer = 0f;

        if (swipeHint != null)
            swipeHint.SetActive(false);
    }
}

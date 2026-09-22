using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// The OBJECTIVE card every minigame shows in its top corner.
///
/// It owns the marking off and the card's entrance: the line of text is set by the minigame
/// itself, the way it always was. The diamond stays as authored; a green check lands on it the
/// moment the minigame is finished — which is to say the moment its COMPLETED banner goes up —
/// and a red X if the quiz reports the minigame ran out of time instead.
///
/// Watching the banner rather than being told keeps this out of all twenty minigames: they
/// already raise that banner on their own, and none of them had to learn about this card.
/// The entrance works the same way: every minigame fades this card's CanvasGroup up from
/// nothing when it starts, and the card slides in from the left whenever it sees that happen.
/// </summary>
public class MinigameObjective : MonoBehaviour
{
    [Header("Diamond")]
    [SerializeField] private Image diamond;

    [Header("Result Mark")]
    [Tooltip("Drawn over the diamond once the minigame is finished. Loaded from " +
             "Resources/MinigameHUD/ObjectiveCheck when left empty.")]
    [SerializeField] private Sprite checkSprite;
    [Tooltip("Drawn over the diamond when the minigame runs out of time. Loaded from " +
             "Resources/MinigameHUD/ObjectiveCross when left empty.")]
    [SerializeField] private Sprite crossSprite;
    [Tooltip("Size of the mark relative to the diamond. 1 = the diamond's own size.")]
    [SerializeField] private float markSize = 1f;

    [Header("Completion")]
    [Tooltip("The minigame's COMPLETED banner. The diamond is checked while this is on, " +
             "so it follows whatever the minigame already counts as finishing.")]
    [SerializeField] private GameObject completedBanner;

    [Header("Mark Pop")]
    [Tooltip("How far the mark swells when it lands, before settling back.")]
    [SerializeField] private float popScale = 1.25f;
    [SerializeField] private float popDuration = 0.35f;

    [Header("Entrance")]
    [Tooltip("How far left of its place the card starts, as a fraction of its width. Kept " +
             "short: the card sits at the screen's left edge, so a long slide keeps it off " +
             "screen for most of its fade and the fade can't be seen.")]
    [SerializeField] private float slideDistance = 0.2f;
    [Tooltip("Seconds the card takes to slide and fade in. Both finish together, so the card " +
             "is fully visible the moment it settles. The card owns this fade: the minigame's " +
             "own quick fade-in is overridden while it runs.")]
    [FormerlySerializedAs("slideDuration")]
    [SerializeField] private float entranceDuration = 0.5f;

    private enum Mark { None, Check, Cross }

    private Mark mark = Mark.None;
    private Image markImage;
    private Coroutine popRoutine;

    private RectTransform rectTransform;
    private CanvasGroup group;
    private Vector2 restPosition;
    private float lastGroupAlpha;
    private float entranceTime = -1f;

    /// <summary>
    /// True once the minigame has been seen through — the same moment the check lands, which
    /// is the moment its COMPLETED banner goes up.
    ///
    /// The quiz reads this to know the player is finished, which is not the same as the
    /// minigame being over: a minigame keeps running for a few seconds after the last piece
    /// is placed, to show the banner and fade its panel away. Those seconds should not be
    /// counted against the clock, so the countdown stops here rather than when Play returns.
    /// </summary>
    public bool IsComplete
    {
        get { return mark == Mark.Check; }
    }

    /// <summary>
    /// The minigame's own COMPLETED banner. It no longer draws anything — the shared
    /// <see cref="MinigameResultBanner"/> is shown in its place — but it still marks the
    /// moment of finishing, and its panel is what the shared banner fades out with.
    /// </summary>
    public GameObject CompletedBanner
    {
        get { return completedBanner; }
    }

    private void Awake()
    {
        rectTransform = (RectTransform)transform;
        group = GetComponent<CanvasGroup>();
        restPosition = rectTransform.anchoredPosition;

        if (checkSprite == null)
            checkSprite = Resources.Load<Sprite>("MinigameHUD/ObjectiveCheck");
        if (crossSprite == null)
            crossSprite = Resources.Load<Sprite>("MinigameHUD/ObjectiveCross");
    }

    private void OnEnable()
    {
        SetMark(Mark.None);
        entranceTime = -1f;
        rectTransform.anchoredPosition = restPosition;
        lastGroupAlpha = group != null ? group.alpha : 1f;
    }

    private void Update()
    {
        if (completedBanner == null || mark != Mark.None)
            return;

        if (completedBanner.activeInHierarchy)
            SetMark(Mark.Check);
    }

    private void LateUpdate()
    {
        if (group == null)
            return;

        // The minigame fading the card up from nothing is the start of a new run
        float alpha = group.alpha;
        if (lastGroupAlpha <= 0f && alpha > 0f)
        {
            entranceTime = 0f;
            SetMark(Mark.None);
        }
        lastGroupAlpha = alpha;

        if (entranceTime < 0f)
            return;

        // Capped so a hitch as the minigame opens can't jump most of the entrance in one frame
        entranceTime += Mathf.Min(Time.unscaledDeltaTime, 0.05f);

        float k = entranceDuration > 0f ? Mathf.Clamp01(entranceTime / entranceDuration) : 1f;

        // One motion: the slide and the fade share a clock and both ease out, so the card
        // glides to a stop in its place just as it becomes fully solid. The slide eases a
        // little harder than the fade, which keeps the fade visible through the whole move.
        float slideEased = 1f - Mathf.Pow(1f - k, 3f);
        float fadeEased = 1f - (1f - k) * (1f - k);

        float width = rectTransform.rect.width;
        rectTransform.anchoredPosition = restPosition + Vector2.left * (width * slideDistance * (1f - slideEased));

        // The fade is the card's own, not the dimmer of it and the minigame's: every minigame
        // fades the card up in 0.3s, straight after its panel fades in, which read as the card
        // simply appearing
        group.alpha = fadeEased;

        if (k >= 1f)
        {
            rectTransform.anchoredPosition = restPosition;
            group.alpha = 1f;
            entranceTime = -1f;
        }
    }

    /// <summary>
    /// Ticks or clears the objective. Public so a minigame can call it directly if it ever
    /// wants to, rather than waiting on its banner.
    /// </summary>
    public void SetChecked(bool value)
    {
        SetMark(value ? Mark.Check : Mark.None);
    }

    /// <summary>Marks the objective with a red X: the minigame ran out of time.</summary>
    public void SetFailed()
    {
        SetMark(Mark.Cross);
    }

    private void SetMark(Mark value)
    {
        mark = value;

        if (popRoutine != null)
        {
            StopCoroutine(popRoutine);
            popRoutine = null;
        }

        if (value == Mark.None)
        {
            if (markImage != null)
                markImage.enabled = false;
            return;
        }

        Image image = EnsureMarkImage();
        if (image == null)
            return;

        image.sprite = value == Mark.Check ? checkSprite : crossSprite;
        image.enabled = image.sprite != null;
        image.rectTransform.localScale = Vector3.one;

        if (isActiveAndEnabled && popDuration > 0f)
            popRoutine = StartCoroutine(Pop(image.rectTransform));
    }

    /// <summary>The mark sits centred on the diamond, drawn just above it.</summary>
    private Image EnsureMarkImage()
    {
        if (markImage != null || diamond == null)
            return markImage;

        GameObject go = new GameObject("ResultMark", typeof(RectTransform));
        go.layer = diamond.gameObject.layer;

        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(diamond.transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = diamond.rectTransform.rect.size * markSize;

        markImage = go.AddComponent<Image>();
        markImage.preserveAspect = true;
        markImage.raycastTarget = false;
        markImage.enabled = false;
        return markImage;
    }

    private IEnumerator Pop(RectTransform rt)
    {
        for (float t = 0f; t < popDuration; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.Clamp01(t / popDuration);

            // Out fast, back slowly, so the mark lands rather than wobbles
            float s = k < 0.35f
                ? Mathf.Lerp(0.4f, popScale, k / 0.35f)
                : Mathf.Lerp(popScale, 1f, (k - 0.35f) / 0.65f);

            rt.localScale = new Vector3(s, s, 1f);
            yield return null;
        }

        rt.localScale = Vector3.one;
        popRoutine = null;
    }
}

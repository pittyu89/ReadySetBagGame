using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DifficultyPanel : MonoBehaviour
{
    [Header("Difficulty")]
    [Tooltip("Beginner, Intermediate, Advanced - in that order. Each button's Target Graphic is its bar.")]
    [SerializeField] private Button[] difficultyButtons;
    [Tooltip("The small stripe left of each bar, tinted together with it.")]
    [SerializeField] private Graphic[] difficultyStripes;
    [SerializeField] private Color selectedColor = new Color(0.824f, 0.478f, 0.271f, 1f);
    [SerializeField] private Color unselectedColor = new Color(0.471f, 0.443f, 0.435f, 1f);
    [SerializeField] private TextMeshProUGUI difficultyTitle;
    [SerializeField] private TextMeshProUGUI difficultyDescription;
    [SerializeField] private RenderTexture difficultyRT;
    [Tooltip("Shows DifficultyRT. Its UV rect is cropped so the video fills the slanted frame without stretching.")]
    [SerializeField] private RawImage previewImage;
    [Tooltip("How far the selected difficulty's bar slides out to the right.")]
    [SerializeField] private float selectedShift = 22f;
    [SerializeField] private float selectDuration = 0.28f;
    [Tooltip("Fade out, then back in, as the title and description change.")]
    [SerializeField] private float textFadeDuration = 0.14f;
    [Tooltip("How far the new title and description rise as they fade in.")]
    [SerializeField] private float textRise = 14f;

    [Header("Locked Difficulties")]
    [Tooltip("Disabled, and relabelled, while a locked difficulty is selected.")]
    [SerializeField] private Button startButton;
    [SerializeField] private string lockedStartText = "LOCKED";
    [Tooltip("Opacity of a locked difficulty's bar label.")]
    [SerializeField, Range(0f, 1f)] private float lockedLabelAlpha = 0.4f;

    [Header("Go-Bag")]
    [SerializeField] private Button bagLeftButton;
    [SerializeField] private Button bagRightButton;
    [SerializeField] private Image bagImage;
    [SerializeField] private TextMeshProUGUI bagNameText;
    [SerializeField] private Sprite[] bagSprites = new Sprite[] { };
    [SerializeField] private string[] bagNames = new string[] { };
    [Tooltip("How long one bag takes to slide out while the next slides in.")]
    [SerializeField] private float bagSwapDuration = 0.45f;
    [SerializeField] private float bagSlideDistance = 110f;
    [Tooltip("Size a bag shrinks to as it leaves (and grows from as it arrives).")]
    [SerializeField] private float bagMinScale = 0.55f;

    [Header("Intro / Outro")]
    [SerializeField] private RectTransform leftBase;
    [SerializeField] private RectTransform rightBase;
    [Tooltip("Shown after the bases meet (header, BACK / START) and hidden before they part.")]
    [SerializeField] private CanvasGroup[] fadeGroups = new CanvasGroup[] { };
    [SerializeField] private float slideDistance = 1600f;
    [SerializeField] private float slideDuration = 0.7f;
    [SerializeField] private float fadeDuration = 0.3f;

    public const string SELECTED_GO_BAG_KEY = "SelectedGoBag";

    /// <summary>The bag the teacher picked for the current session. Kept apart from the
    /// player's own offline choice so a session doesn't overwrite it.</summary>
    public const string SESSION_GO_BAG_KEY = "SessionGoBag";

    /// <summary>
    /// The bag this run uses (0 = Standard, 1 = Small, 2 = Medium): the teacher's pick in a
    /// teacher session, otherwise whatever the player chose on this panel.
    /// </summary>
    public static int GetActiveGoBag()
    {
        bool inTeacherSession = !string.IsNullOrEmpty(PlayerPrefs.GetString("SessionCode", ""));
        if (inTeacherSession && PlayerPrefs.HasKey(SESSION_GO_BAG_KEY))
            return PlayerPrefs.GetInt(SESSION_GO_BAG_KEY, 0);

        return PlayerPrefs.GetInt(SELECTED_GO_BAG_KEY, 0);
    }

    private int currentDifficultyIndex = 0;
    private int currentBagIndex = 0;
    private CanvasGroup panelGroup;
    private Coroutine transition;

    // A second copy of the bag image, used for the bag sliding in during a swap
    private Image incomingBagImage;
    private Vector2 bagHomePosition;
    private Coroutine bagSwap;

    // Resting positions, captured before any selection offsets are applied
    private Vector2[] difficultyButtonHomes;
    private Vector2 titleHome;
    private Vector2 descriptionHome;
    private Coroutine selectAnimation;
    private Coroutine textAnimation;

    private TextMeshProUGUI[] difficultyLabels;
    private TextMeshProUGUI startLabel;
    private string startText;

    private readonly string[] difficultyNames = { "Beginner", "Intermediate", "Advanced" };
    private readonly string[] difficultyKeys = { "beginner", "intermediate", "advanced" };
    private readonly string[] difficultyDescriptions =
    {
        "A relaxed run with a 10-minute timer and a 5kg weight limit, perfect for taking your time to learn item locations and master the basics.",
        "Unlocks new areas to explore, featuring an 8-minute timer and a 5kg weight limit, with supplies shuffled to new spots every run.",
        "Unlocks all areas with a fast 6-minute countdown and a 5kg weight limit, demanding precise routing and expert packing choices."
    };

    private readonly string[] videoKeys =
    {
        VideoManager.BEGINNER_PREVIEW,
        VideoManager.INTERMEDIATE_PREVIEW,
        VideoManager.ADVANCED_PREVIEW
    };

    void Awake()
    {
        panelGroup = GetComponent<CanvasGroup>();
        if (panelGroup == null)
            panelGroup = gameObject.AddComponent<CanvasGroup>();

        difficultyButtonHomes = new Vector2[difficultyButtons.Length];
        difficultyLabels = new TextMeshProUGUI[difficultyButtons.Length];
        for (int i = 0; i < difficultyButtons.Length; i++)
        {
            int index = i;
            if (difficultyButtons[i] != null)
            {
                difficultyButtonHomes[i] = ((RectTransform)difficultyButtons[i].transform).anchoredPosition;
                difficultyLabels[i] = difficultyButtons[i].GetComponentInChildren<TextMeshProUGUI>(true);
                difficultyButtons[i].onClick.AddListener(() => SelectDifficulty(index));
            }
        }

        if (startButton != null)
        {
            startLabel = startButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (startLabel != null)
                startText = startLabel.text;
        }

        if (difficultyTitle != null)
            titleHome = difficultyTitle.rectTransform.anchoredPosition;

        if (difficultyDescription != null)
            descriptionHome = difficultyDescription.rectTransform.anchoredPosition;

        if (bagLeftButton != null)
            bagLeftButton.onClick.AddListener(PreviousBag);

        if (bagRightButton != null)
            bagRightButton.onClick.AddListener(NextBag);

        if (bagImage != null)
        {
            bagHomePosition = bagImage.rectTransform.anchoredPosition;

            incomingBagImage = Instantiate(bagImage, bagImage.transform.parent);
            incomingBagImage.name = "BagImageIncoming";
            incomingBagImage.transform.SetSiblingIndex(bagImage.transform.GetSiblingIndex() + 1);
            incomingBagImage.gameObject.SetActive(false);
        }
    }

    void OnEnable()
    {
        currentDifficultyIndex = 0;
        currentBagIndex = bagSprites.Length > 0
            ? Mathf.Clamp(PlayerPrefs.GetInt(SELECTED_GO_BAG_KEY, 0), 0, bagSprites.Length - 1)
            : 0;

        FitPreviewToFrame();
        UpdateDifficultyDisplay();
        SnapDifficultyVisuals();
        UpdateBagDisplay();

        DifficultyProgress.Changed += OnLocksChanged;

        transition = StartCoroutine(PlayIntro());
    }

    void OnDisable()
    {
        DifficultyProgress.Changed -= OnLocksChanged;

        transition = null;

        // Coroutines die with the panel - don't leave a half-slid bar or faded text behind
        selectAnimation = null;
        textAnimation = null;
        SnapDifficultyVisuals();

        // Coroutines die with the panel - don't leave a half-slid bag behind
        if (bagSwap != null)
        {
            bagSwap = null;
            ResetBagVisuals();
        }

        // Stop whichever preview is playing when the panel closes
        if (VideoManager.Instance != null)
            VideoManager.Instance.StopVideo(videoKeys[currentDifficultyIndex]);
    }

    /// <summary>
    /// Slides the bases back out, then runs <paramref name="onComplete"/> (start the game,
    /// go back). Ignored while the panel is already animating.
    /// </summary>
    public void PlayOutro(Action onComplete)
    {
        if (transition != null || !isActiveAndEnabled)
            return;

        transition = StartCoroutine(PlayOutroRoutine(onComplete));
    }

    private void SelectDifficulty(int index)
    {
        if (index == currentDifficultyIndex)
            return;

        currentDifficultyIndex = index;
        UpdateDifficultyDisplay(animateText: isActiveAndEnabled);

        if (!isActiveAndEnabled)
        {
            SnapDifficultyVisuals();
            return;
        }

        // Starts from wherever the bars are, so a quick second tap redirects smoothly
        if (selectAnimation != null)
            StopCoroutine(selectAnimation);
        selectAnimation = StartCoroutine(AnimateDifficultyBars());
    }

    /// <summary>
    /// The chosen bar slides out to the right with a small overshoot and warms to the
    /// selected colour, while the others slide home and cool back down.
    /// </summary>
    private IEnumerator AnimateDifficultyBars()
    {
        int count = difficultyButtons.Length;
        var startPositions = new Vector2[count];
        var startColors = new Color[count];

        for (int i = 0; i < count; i++)
        {
            if (difficultyButtons[i] == null) continue;
            startPositions[i] = ((RectTransform)difficultyButtons[i].transform).anchoredPosition;
            startColors[i] = BarGraphic(i) != null ? BarGraphic(i).color : unselectedColor;
        }

        yield return Animate(selectDuration, t =>
        {
            for (int i = 0; i < count; i++)
            {
                if (difficultyButtons[i] == null) continue;

                bool selected = i == currentDifficultyIndex;
                var rt = (RectTransform)difficultyButtons[i].transform;
                Vector2 target = DifficultyButtonTarget(i);

                // Only the arriving bar overshoots; the others just ease home
                float move = selected ? EaseOutBack(t) : EaseOutCubic(t);
                rt.anchoredPosition = Vector2.LerpUnclamped(startPositions[i], target, move);

                TintDifficulty(i, Color.Lerp(startColors[i], selected ? selectedColor : unselectedColor, EaseOutCubic(t)));
            }
        });

        selectAnimation = null;
        SnapDifficultyBars();
    }

    /// <summary>Fades the title and description out, swaps the words, then fades them in rising.</summary>
    private IEnumerator AnimateDifficultyText(string title, string description)
    {
        float startAlpha = difficultyTitle != null ? difficultyTitle.alpha
            : difficultyDescription != null ? difficultyDescription.alpha : 1f;

        yield return Animate(textFadeDuration * startAlpha, t => SetDifficultyTextAlpha(Mathf.Lerp(startAlpha, 0f, t), 0f));

        if (difficultyTitle != null) difficultyTitle.text = title;
        if (difficultyDescription != null) difficultyDescription.text = description;

        yield return Animate(textFadeDuration, t =>
        {
            float ease = EaseOutCubic(t);
            SetDifficultyTextAlpha(ease, textRise * (1f - ease));
        });

        textAnimation = null;
    }

    private void SetDifficultyTextAlpha(float alpha, float drop)
    {
        if (difficultyTitle != null)
        {
            difficultyTitle.alpha = alpha;
            difficultyTitle.rectTransform.anchoredPosition = titleHome + Vector2.down * drop;
        }

        if (difficultyDescription != null)
        {
            difficultyDescription.alpha = alpha;
            difficultyDescription.rectTransform.anchoredPosition = descriptionHome + Vector2.down * drop;
        }
    }

    /// <summary>Puts every bar, tint and text straight into its resting state for the current selection.</summary>
    private void SnapDifficultyVisuals()
    {
        SnapDifficultyBars();

        if (difficultyTitle != null) difficultyTitle.text = TitleFor(currentDifficultyIndex);
        if (difficultyDescription != null) difficultyDescription.text = DescriptionFor(currentDifficultyIndex);
        SetDifficultyTextAlpha(1f, 0f);
    }

    // ---- Locks ----

    private bool IsLocked(int index) => !DifficultyProgress.IsUnlocked(difficultyKeys[index]);

    private string TitleFor(int index) =>
        IsLocked(index) ? difficultyNames[index] + " (Locked)" : difficultyNames[index];

    // A locked difficulty says what opens it instead of what it's like
    private string DescriptionFor(int index) =>
        IsLocked(index) ? DifficultyProgress.Requirement(difficultyKeys[index]) : difficultyDescriptions[index];

    /// <summary>
    /// Dims the labels of locked difficulties and holds START while one is selected. A locked
    /// bar can still be picked, so the player can watch its preview and read what opens it.
    /// </summary>
    private void ApplyLocks()
    {
        for (int i = 0; i < difficultyLabels.Length; i++)
        {
            if (difficultyLabels[i] != null)
                difficultyLabels[i].alpha = IsLocked(i) ? lockedLabelAlpha : 1f;
        }

        bool locked = IsLocked(currentDifficultyIndex);
        if (startButton != null)
            startButton.interactable = !locked;
        if (startLabel != null)
            startLabel.text = locked ? lockedStartText : startText;
    }

    /// <summary>The debug picker can lock or unlock a difficulty while this panel is open.</summary>
    private void OnLocksChanged()
    {
        ApplyLocks();
        if (textAnimation == null)
            SnapDifficultyVisuals();
    }

    private void SnapDifficultyBars()
    {
        if (difficultyButtonHomes == null)
            return;

        for (int i = 0; i < difficultyButtons.Length; i++)
        {
            if (difficultyButtons[i] == null) continue;
            ((RectTransform)difficultyButtons[i].transform).anchoredPosition = DifficultyButtonTarget(i);
            TintDifficulty(i, i == currentDifficultyIndex ? selectedColor : unselectedColor);
        }
    }

    private Vector2 DifficultyButtonTarget(int index)
    {
        Vector2 home = difficultyButtonHomes[index];
        return index == currentDifficultyIndex ? home + Vector2.right * selectedShift : home;
    }

    private Graphic BarGraphic(int index)
    {
        return difficultyButtons[index] != null ? difficultyButtons[index].targetGraphic : null;
    }

    private void TintDifficulty(int index, Color tint)
    {
        if (BarGraphic(index) != null)
            BarGraphic(index).color = tint;

        if (index < difficultyStripes.Length && difficultyStripes[index] != null)
            difficultyStripes[index].color = tint;
    }

    private void NextBag()
    {
        ChangeBag(1);
    }

    private void PreviousBag()
    {
        ChangeBag(-1);
    }

    /// <param name="direction">1 = next (new bag enters from the right), -1 = previous.</param>
    private void ChangeBag(int direction)
    {
        if (bagSprites.Length == 0)
            return;

        // A quick second tap skips the running swap straight to its end
        if (bagSwap != null)
        {
            StopCoroutine(bagSwap);
            bagSwap = null;
            ResetBagVisuals();
        }

        Sprite from = bagSprites[currentBagIndex];
        currentBagIndex = (currentBagIndex + direction + bagSprites.Length) % bagSprites.Length;
        UpdateBagDisplay();

        if (bagImage != null && incomingBagImage != null && isActiveAndEnabled)
            bagSwap = StartCoroutine(SwapBag(from, bagSprites[currentBagIndex], direction));
    }

    /// <summary>
    /// Carousel swap: the current bag slides toward the far side while shrinking and
    /// fading, as the next one slides in from the arrow's side, grows and pops into place.
    /// </summary>
    private IEnumerator SwapBag(Sprite from, Sprite to, int direction)
    {
        RectTransform outgoing = bagImage.rectTransform;
        RectTransform incoming = incomingBagImage.rectTransform;

        bagImage.sprite = from;
        incomingBagImage.sprite = to;
        incoming.anchoredPosition = bagHomePosition;
        incomingBagImage.gameObject.SetActive(true);

        yield return Animate(bagSwapDuration, t =>
        {
            float slide = EaseOutCubic(t);

            outgoing.anchoredPosition = bagHomePosition + new Vector2(-direction * bagSlideDistance * slide, 0f);
            outgoing.localScale = Vector3.one * Mathf.Lerp(1f, bagMinScale, slide);
            SetAlpha(bagImage, 1f - slide);

            incoming.anchoredPosition = bagHomePosition + new Vector2(direction * bagSlideDistance * (1f - slide), 0f);
            incoming.localScale = Vector3.one * Mathf.LerpUnclamped(bagMinScale, 1f, EaseOutBack(t));
            SetAlpha(incomingBagImage, slide);
        });

        bagSwap = null;
        ResetBagVisuals();
    }

    private void ResetBagVisuals()
    {
        if (bagImage == null)
            return;

        if (bagSprites.Length > 0)
            bagImage.sprite = bagSprites[currentBagIndex];

        bagImage.rectTransform.anchoredPosition = bagHomePosition;
        bagImage.rectTransform.localScale = Vector3.one;
        SetAlpha(bagImage, 1f);

        if (incomingBagImage != null)
            incomingBagImage.gameObject.SetActive(false);
    }

    private static void SetAlpha(Graphic graphic, float alpha)
    {
        Color c = graphic.color;
        c.a = alpha;
        graphic.color = c;
    }

    /// <summary>
    /// Switches the preview video and saves the choice. Bar tints and positions are handled
    /// by SnapDifficultyVisuals / AnimateDifficultyBars; the text here, unless animated.
    /// </summary>
    private void UpdateDifficultyDisplay(bool animateText = false)
    {
        if (animateText)
        {
            if (textAnimation != null)
                StopCoroutine(textAnimation);
            textAnimation = StartCoroutine(AnimateDifficultyText(
                TitleFor(currentDifficultyIndex), DescriptionFor(currentDifficultyIndex)));
        }
        else
        {
            if (difficultyTitle != null)
                difficultyTitle.text = TitleFor(currentDifficultyIndex);

            if (difficultyDescription != null)
                difficultyDescription.text = DescriptionFor(currentDifficultyIndex);
        }

        ApplyLocks();

        // Play the preview video through VideoManager
        if (VideoManager.Instance != null)
        {
            // Stop all previews first
            for (int i = 0; i < videoKeys.Length; i++)
                VideoManager.Instance.StopVideo(videoKeys[i]);

            // Point the selected video at this panel's RenderTexture and play
            var vp = VideoManager.Instance.GetPlayer(videoKeys[currentDifficultyIndex]);
            if (vp != null)
            {
                if (difficultyRT != null)
                    vp.targetTexture = difficultyRT;
                vp.Play();
            }
        }

        PlayerPrefs.SetString("SessionDifficulty", difficultyKeys[currentDifficultyIndex]);
        PlayerPrefs.Save();
    }

    private void UpdateBagDisplay()
    {
        if (bagSprites.Length == 0)
            return;

        if (bagImage != null)
            bagImage.sprite = bagSprites[currentBagIndex];

        if (bagNameText != null)
            bagNameText.text = currentBagIndex < bagNames.Length ? bagNames[currentBagIndex] : string.Empty;

        // Read in the game scene by InventoryPanel and GoBagPickup
        PlayerPrefs.SetInt(SELECTED_GO_BAG_KEY, currentBagIndex);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// The preview frame is much wider than the video, so show a centered horizontal
    /// strip of it instead of squashing the whole frame in.
    /// </summary>
    private void FitPreviewToFrame()
    {
        if (previewImage == null || difficultyRT == null)
            return;

        Rect frame = previewImage.rectTransform.rect;
        if (frame.width <= 0f || frame.height <= 0f)
            return;

        float frameAspect = frame.width / frame.height;
        float videoAspect = (float)difficultyRT.width / difficultyRT.height;

        if (frameAspect >= videoAspect)
        {
            float h = videoAspect / frameAspect;
            previewImage.uvRect = new Rect(0f, (1f - h) * 0.5f, 1f, h);
        }
        else
        {
            float w = frameAspect / videoAspect;
            previewImage.uvRect = new Rect((1f - w) * 0.5f, 0f, w, 1f);
        }
    }

    private IEnumerator PlayIntro()
    {
        // Taps are blocked rather than the buttons made non-interactable - non-interactable
        // buttons draw in their faded "disabled" tint, so BACK and START would dim mid-transition
        panelGroup.blocksRaycasts = false;
        SetFade(0f);
        SetBaseOffset(1f);

        yield return Animate(slideDuration, t => SetBaseOffset(1f - EaseOutCubic(t)));
        yield return Animate(fadeDuration, SetFade);

        panelGroup.blocksRaycasts = true;
        transition = null;
    }

    private IEnumerator PlayOutroRoutine(Action onComplete)
    {
        panelGroup.blocksRaycasts = false;

        yield return Animate(fadeDuration, t => SetFade(1f - t));
        yield return Animate(slideDuration, t => SetBaseOffset(EaseInCubic(t)));

        transition = null;
        onComplete?.Invoke();

        // Reset for the next time the panel is shown, in case it stays open
        if (isActiveAndEnabled)
        {
            SetBaseOffset(0f);
            SetFade(1f);
            panelGroup.blocksRaycasts = true;
        }
    }

    private static IEnumerator Animate(float duration, Action<float> step)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            step(elapsed / duration);
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }
        step(1f);
    }

    /// <summary>0 = bases meet in the middle, 1 = fully off-screen on their own sides.</summary>
    private void SetBaseOffset(float amount)
    {
        if (leftBase != null)
            leftBase.anchoredPosition = new Vector2(-slideDistance * amount, leftBase.anchoredPosition.y);

        if (rightBase != null)
            rightBase.anchoredPosition = new Vector2(slideDistance * amount, rightBase.anchoredPosition.y);
    }

    private void SetFade(float alpha)
    {
        foreach (CanvasGroup group in fadeGroups)
        {
            if (group != null)
                group.alpha = alpha;
        }
    }

    private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
    private static float EaseInCubic(float t) => t * t * t;

    // Overshoots past 1 before settling - the little "pop" as a bag arrives
    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }
}

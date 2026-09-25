using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The coach marks drawn over the game during the practice run: a card that says what to do,
/// a dimmed screen with a window cut around whatever is being taught, a pulsing frame on it,
/// and a bouncing arrow that points at a HUD element or at something out in the house.
///
/// Built entirely in code on its own canvas, drawn above the game's, so GameScene needs no
/// extra UI set up and the game's own panels are never rearranged. <see cref="OnboardingManager"/>
/// decides what is shown; this only draws it and keeps it lined up with its targets as they move.
/// </summary>
public class OnboardingOverlay : MonoBehaviour
{
    /// <summary>How much of the screen the current step lets the player touch.</summary>
    public enum Block
    {
        /// <summary>Nothing dimmed, nothing blocked: steps played out in the house.</summary>
        None,
        /// <summary>Everything outside the targets is dimmed and blocked; the targets can be used.</summary>
        OutsideTargets,
        /// <summary>Everything is blocked. The targets stay undimmed so they can be read.</summary>
        Everything
    }

    public enum CardPlace { Auto, Top, Bottom, Center }

    // House palette: the finish prompt's black panel, its green button, the tutorial's orange.
    // The panel is solid: the Outline draws copies of it in orange underneath, which a
    // see-through panel lets bleed through as a brown tint.
    private static readonly Color PanelColor = new Color(0.04f, 0.04f, 0.04f, 1f);
    private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.6f);
    private static readonly Color AccentColor = new Color(1f, 0.545f, 0.263f, 1f);
    private static readonly Color ButtonColor = new Color(0.541f, 0.631f, 0.012f, 1f);
    private static readonly Color StepColor = new Color(1f, 1f, 1f, 0.55f);

    // Narrow enough to sit beside a target in the middle of the screen
    private const float CardWidth = 470f;
    private const float FramePadding = 8f;
    private const float FrameThickness = 4f;

    // The marker sprite's triangle fills the middle half of its square, so the square is
    // drawn larger than the arrow should look. The tip sits this far up the sprite.
    private const float ArrowSize = 72f;
    private const float ArrowTipY = 0.16f;

    // Kept clear of the screen edges and of the timer and pause button along the top
    private const float EdgeMargin = 16f;
    private const float TopHudHeight = 80f;

    private Canvas canvas;
    private RectTransform root;
    private Image catcher;
    private readonly Image[] dims = new Image[4];
    private readonly List<Image[]> frames = new List<Image[]>();

    private RectTransform card;
    private TextMeshProUGUI stepText;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI bodyText;
    private Button button;
    private TextMeshProUGUI buttonLabel;
    private Action onButton;
    private CardPlace place = CardPlace.Auto;

    private RectTransform arrow;
    private TextMeshProUGUI badge;

    private RectTransform skipButton;
    private GameObject confirm;
    private TextMeshProUGUI confirmTitle;
    private TextMeshProUGUI confirmBody;
    private Action onConfirmYes;
    private Action onConfirmNo;

    /// <summary>Raised when the player taps SKIP. The manager decides what that means.</summary>
    public event Action SkipRequested;

    private readonly List<RectTransform> targets = new List<RectTransform>();
    private readonly List<Func<RectTransform>> keepClearOf = new List<Func<RectTransform>>();
    private Func<Vector3> worldTarget;
    private Block block = Block.None;
    private bool hidden;

    /// <summary>Makes the overlay on its own canvas above everything else in the scene.</summary>
    public static OnboardingOverlay Create(TMP_FontAsset font, Sprite buttonSprite)
    {
        GameObject go = new GameObject("OnboardingOverlay", typeof(RectTransform));
        OnboardingOverlay overlay = go.AddComponent<OnboardingOverlay>();
        overlay.Build(font, buttonSprite);
        return overlay;
    }

    private void Build(TMP_FontAsset font, Sprite buttonSprite)
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        // Same scaling as the game's canvas, so sizes here read the same as the HUD's
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();
        root = (RectTransform)transform;

        // Swallows every tap while a card needs reading. Invisible: the dims do the darkening.
        catcher = MakeImage("Catcher", root, new Color(0f, 0f, 0f, 0f));
        Stretch(catcher.rectTransform);

        for (int i = 0; i < dims.Length; i++)
        {
            dims[i] = MakeImage("Dim" + i, root, DimColor);
            SetCornerAnchored(dims[i].rectTransform);
        }

        // The same rounded arrow that marks searchable furniture, in the coach's orange
        Image arrowImage = MakeImage("Arrow", root, AccentColor);
        arrowImage.sprite = ClickableIndicator.ArrowSprite;
        arrowImage.raycastTarget = false;
        arrow = arrowImage.rectTransform;
        arrow.sizeDelta = new Vector2(ArrowSize, ArrowSize);
        SetCornerAnchored(arrow);
        // Pivot on the arrow's rounded tip, so placing the pivot is placing the point
        arrow.pivot = new Vector2(0.5f, ArrowTipY);

        BuildCard(font, buttonSprite);

        badge = MakeText("Badge", root, font, 20f, StepColor);
        badge.text = "PRACTICE RUN - NOT TIMED OR SCORED";
        badge.alignment = TextAlignmentOptions.Center;
        RectTransform badgeRect = badge.rectTransform;
        badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(0.5f, 0f);
        badgeRect.pivot = new Vector2(0.5f, 0f);
        badgeRect.sizeDelta = new Vector2(600f, 28f);
        badgeRect.anchoredPosition = new Vector2(0f, 10f);

        // Bottom-right is the one corner the HUD leaves free: joystick bottom-left, Journal
        // top-left, timer and pause top-centre, bag button top-right
        Button skip = MakeButton("Skip", root, font, buttonSprite, "SKIP PRACTICE", 24f,
                                 new Color(0.18f, 0.18f, 0.18f, 0.92f), new Vector2(200f, 44f));
        skipButton = (RectTransform)skip.transform;
        skipButton.anchorMin = skipButton.anchorMax = new Vector2(1f, 0f);
        skipButton.pivot = new Vector2(1f, 0f);
        skipButton.anchoredPosition = new Vector2(-EdgeMargin, EdgeMargin);
        skip.onClick.AddListener(() => SkipRequested?.Invoke());

        BuildConfirm(font, buttonSprite);

        HideCard();
        SetTargets();
        SetWorldTarget(null);
        SetBlock(Block.None);
    }

    private void BuildCard(TMP_FontAsset font, Sprite buttonSprite)
    {
        Image background = MakeImage("Card", root, PanelColor);
        card = background.rectTransform;
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(CardWidth, 0f);

        Outline outline = background.gameObject.AddComponent<Outline>();
        outline.effectColor = AccentColor;
        outline.effectDistance = new Vector2(3f, -3f);

        VerticalLayoutGroup layout = background.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 18, 22);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = background.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        stepText = MakeText("Step", card, font, 20f, StepColor);
        stepText.alignment = TextAlignmentOptions.Center;

        titleText = MakeText("Title", card, font, 40f, AccentColor);
        titleText.alignment = TextAlignmentOptions.Center;

        bodyText = MakeText("Body", card, font, 27f, Color.white);
        bodyText.alignment = TextAlignmentOptions.Center;
        bodyText.lineSpacing = -8f;

        // The button sits in a row of its own so it keeps its size inside the stretched column
        GameObject row = new GameObject("ButtonRow", typeof(RectTransform));
        row.transform.SetParent(card, false);
        HorizontalLayoutGroup rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlWidth = false;
        rowLayout.childControlHeight = false;
        LayoutElement rowElement = row.AddComponent<LayoutElement>();
        rowElement.preferredHeight = 62f;

        Image buttonImage = MakeImage("Button", row.transform, ButtonColor);
        if (buttonSprite != null)
        {
            buttonImage.sprite = buttonSprite;
            buttonImage.type = Image.Type.Sliced;
        }
        buttonImage.rectTransform.sizeDelta = new Vector2(240f, 56f);
        button = buttonImage.gameObject.AddComponent<Button>();
        button.onClick.AddListener(() =>
        {
            Action pending = onButton;
            onButton = null;
            pending?.Invoke();
        });

        buttonLabel = MakeText("Label", buttonImage.transform, font, 34f, Color.white);
        buttonLabel.alignment = TextAlignmentOptions.Center;
        Stretch(buttonLabel.rectTransform);
    }

    /// <summary>
    /// A yes / no question over a full-screen dim, above everything else the overlay draws.
    /// Used to make sure a Skip was meant.
    /// </summary>
    private void BuildConfirm(TMP_FontAsset font, Sprite buttonSprite)
    {
        Image dim = MakeImage("Confirm", root, DimColor);
        Stretch(dim.rectTransform);
        confirm = dim.gameObject;

        Image panel = MakeImage("Panel", confirm.transform, PanelColor);
        RectTransform panelRect = panel.rectTransform;
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(CardWidth + 60f, 0f);

        Outline outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = AccentColor;
        outline.effectDistance = new Vector2(3f, -3f);

        VerticalLayoutGroup layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 22, 24);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        confirmTitle = MakeText("Title", panelRect, font, 40f, AccentColor);
        confirmTitle.alignment = TextAlignmentOptions.Center;
        confirmBody = MakeText("Body", panelRect, font, 27f, Color.white);
        confirmBody.alignment = TextAlignmentOptions.Center;
        confirmBody.lineSpacing = -8f;

        GameObject row = new GameObject("Buttons", typeof(RectTransform));
        row.transform.SetParent(panelRect, false);
        HorizontalLayoutGroup rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.spacing = 20f;
        rowLayout.childControlWidth = false;
        rowLayout.childControlHeight = false;
        row.AddComponent<LayoutElement>().preferredHeight = 62f;

        // The safe choice is the green one: carrying on with the practice
        Button no = MakeButton("No", row.transform, font, buttonSprite, "KEEP PRACTICING", 30f,
                               ButtonColor, new Vector2(250f, 56f));
        Button yes = MakeButton("Yes", row.transform, font, buttonSprite, "SKIP", 30f,
                                new Color(0.35f, 0.35f, 0.35f, 1f), new Vector2(160f, 56f));
        no.onClick.AddListener(() => CloseConfirm(onConfirmNo));
        yes.onClick.AddListener(() => CloseConfirm(onConfirmYes));

        confirm.SetActive(false);
    }

    private void CloseConfirm(Action then)
    {
        confirm.SetActive(false);
        onConfirmYes = null;
        onConfirmNo = null;
        then?.Invoke();
    }

    private Button MakeButton(string name, Transform parent, TMP_FontAsset font, Sprite sprite,
                              string label, float fontSize, Color color, Vector2 size)
    {
        Image image = MakeImage(name, parent, color);
        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
        }
        image.rectTransform.sizeDelta = size;

        TextMeshProUGUI text = MakeText("Label", image.transform, font, fontSize, Color.white);
        text.text = label;
        text.alignment = TextAlignmentOptions.Center;
        Stretch(text.rectTransform);

        return image.gameObject.AddComponent<Button>();
    }

    // ----------------------------------------------------------------- public API

    /// <summary>Asks a yes / no question over everything, and reports the answer.</summary>
    public void ShowConfirm(string title, string body, Action onYes, Action onNo)
    {
        confirmTitle.text = title;
        confirmBody.text = body;
        onConfirmYes = onYes;
        onConfirmNo = onNo;

        confirm.transform.SetAsLastSibling();
        confirm.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)confirm.transform.GetChild(0));
    }

    public bool IsConfirmOpen => confirm.activeSelf;

    /// <summary>
    /// Puts up the card. With a <paramref name="buttonText"/> it waits for a tap on that button;
    /// without one it is an instruction, and the step ends when the player does the thing.
    /// </summary>
    public void ShowCard(string step, string title, string body, string buttonText = null,
                         Action onClick = null, CardPlace cardPlace = CardPlace.Auto)
    {
        stepText.text = step;
        titleText.text = title;
        bodyText.text = body;
        place = cardPlace;

        bool hasButton = !string.IsNullOrEmpty(buttonText);
        button.transform.parent.gameObject.SetActive(hasButton);
        buttonLabel.text = hasButton ? buttonText : "";
        onButton = onClick;

        // An instruction card must not eat the drags and taps the step is asking for
        card.GetComponent<Image>().raycastTarget = hasButton;

        card.gameObject.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(card);
    }

    /// <summary>Swaps the card's text while a step runs, for hints that follow what the player does.</summary>
    public void SetBody(string body)
    {
        if (bodyText.text == body)
            return;

        bodyText.text = body;
        LayoutRebuilder.ForceRebuildLayoutImmediate(card);
    }

    public void HideCard()
    {
        card.gameObject.SetActive(false);
        onButton = null;
    }

    /// <summary>HUD elements to frame and point at. The first one gets the arrow.</summary>
    public void SetTargets(params RectTransform[] rects)
    {
        targets.Clear();
        if (rects != null)
        {
            foreach (RectTransform rect in rects)
                if (rect != null)
                    targets.Add(rect);
        }

        while (frames.Count < targets.Count)
            frames.Add(MakeFrame());

        for (int i = 0; i < frames.Count; i++)
            SetFrameActive(frames[i], i < targets.Count);
    }

    /// <summary>
    /// Something the card should always stay off while it is showing, whatever the step.
    /// Looked up every frame; return null while it is not up.
    /// </summary>
    public void KeepClearOf(Func<RectTransform> rect)
    {
        if (rect != null)
            keepClearOf.Add(rect);
    }

    /// <summary>A point in the house to point the arrow at, re-read every frame. Null clears it.</summary>
    public void SetWorldTarget(Func<Vector3> target)
    {
        worldTarget = target;
    }

    public void SetBlock(Block value)
    {
        block = value;
    }

    /// <summary>Hides the whole overlay, for the pause menu, without losing the step.</summary>
    public void SetHidden(bool value)
    {
        hidden = value;
        canvas.enabled = !value;
    }

    public void Clear()
    {
        HideCard();
        SetTargets();
        SetWorldTarget(null);
        SetBlock(Block.None);
    }

    // ----------------------------------------------------------------- per frame

    private void LateUpdate()
    {
        if (hidden)
            return;

        Rect hole;
        bool hasHole = TryGetTargetsRect(out hole);

        catcher.enabled = block == Block.Everything;
        UpdateDims(hasHole ? hole : (Rect?)null);
        UpdateFrames();
        UpdateArrow(hasHole);
        PlaceCard(hasHole ? hole : (Rect?)null);

        // The catcher sits under the card so the card's button still gets its tap
        catcher.transform.SetSiblingIndex(0);
    }

    private void UpdateDims(Rect? hole)
    {
        bool dimmed = block != Block.None;
        Vector2 size = root.rect.size;

        if (!dimmed)
        {
            foreach (Image dim in dims)
                dim.enabled = false;
            return;
        }

        // No target: one sheet over everything
        if (hole == null)
        {
            SetDim(0, 0f, 0f, size.x, size.y);
            for (int i = 1; i < dims.Length; i++)
                dims[i].enabled = false;
            return;
        }

        Rect h = hole.Value;
        float xMin = Mathf.Clamp(h.xMin, 0f, size.x);
        float xMax = Mathf.Clamp(h.xMax, 0f, size.x);
        float yMin = Mathf.Clamp(h.yMin, 0f, size.y);
        float yMax = Mathf.Clamp(h.yMax, 0f, size.y);

        SetDim(0, 0f, yMax, size.x, size.y - yMax);        // above
        SetDim(1, 0f, 0f, size.x, yMin);                   // below
        SetDim(2, 0f, yMin, xMin, yMax - yMin);            // left
        SetDim(3, xMax, yMin, size.x - xMax, yMax - yMin); // right
    }

    private void SetDim(int index, float x, float y, float width, float height)
    {
        Image dim = dims[index];
        dim.enabled = width > 0.5f && height > 0.5f;
        dim.rectTransform.anchoredPosition = new Vector2(x, y);
        dim.rectTransform.sizeDelta = new Vector2(width, height);
    }

    private void UpdateFrames()
    {
        // A slow pulse, so the frame reads as "look here" rather than as part of the UI
        float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f));
        Color color = new Color(AccentColor.r, AccentColor.g, AccentColor.b, pulse);

        for (int i = 0; i < targets.Count; i++)
        {
            Rect rect;
            bool visible = TryGetLocalRect(targets[i], out rect);
            SetFrameActive(frames[i], visible);
            if (!visible)
                continue;

            rect.xMin -= FramePadding;
            rect.yMin -= FramePadding;
            rect.xMax += FramePadding;
            rect.yMax += FramePadding;

            Image[] edges = frames[i];
            PlaceEdge(edges[0], rect.xMin, rect.yMax - FrameThickness, rect.width, FrameThickness, color);
            PlaceEdge(edges[1], rect.xMin, rect.yMin, rect.width, FrameThickness, color);
            PlaceEdge(edges[2], rect.xMin, rect.yMin, FrameThickness, rect.height, color);
            PlaceEdge(edges[3], rect.xMax - FrameThickness, rect.yMin, FrameThickness, rect.height, color);
        }
    }

    private static void PlaceEdge(Image edge, float x, float y, float width, float height, Color color)
    {
        edge.rectTransform.anchoredPosition = new Vector2(x, y);
        edge.rectTransform.sizeDelta = new Vector2(width, height);
        edge.color = color;
    }

    private void UpdateArrow(bool hasHole)
    {
        float bob = Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f)) * 12f;
        Vector2 size = root.rect.size;

        // A HUD target takes precedence: the arrow hangs over the first one
        Rect first;
        if (targets.Count > 0 && TryGetLocalRect(targets[0], out first))
        {
            arrow.gameObject.SetActive(true);

            // Pointing down from above, unless the target is against the top of the screen
            bool fromBelow = first.yMax + ArrowSize + 30f > size.y;
            if (fromBelow)
            {
                arrow.anchoredPosition = new Vector2(first.center.x, first.yMin - FramePadding - 6f - bob);
                arrow.localEulerAngles = new Vector3(0f, 0f, 180f);
            }
            else
            {
                arrow.anchoredPosition = new Vector2(first.center.x, first.yMax + FramePadding + 6f + bob);
                arrow.localEulerAngles = Vector3.zero;
            }
            return;
        }

        if (worldTarget == null || Camera.main == null)
        {
            arrow.gameObject.SetActive(false);
            return;
        }

        arrow.gameObject.SetActive(true);
        Vector3 screen = Camera.main.WorldToScreenPoint(worldTarget());
        bool behind = screen.z < 0f;
        if (behind)
            screen = -screen;

        Vector2 local = ScreenToLocal(screen);
        float margin = ArrowSize + 20f;
        bool onScreen = !behind && local.x > margin && local.x < size.x - margin
                        && local.y > margin && local.y < size.y - margin;

        if (onScreen)
        {
            arrow.anchoredPosition = new Vector2(local.x, local.y + bob);
            arrow.localEulerAngles = Vector3.zero;
            return;
        }

        // Off screen: ride the edge nearest the target and point out toward it
        Vector2 centre = size * 0.5f;
        Vector2 direction = (local - centre);
        if (direction.sqrMagnitude < 0.01f)
            direction = Vector2.down;
        direction.Normalize();

        float scale = Mathf.Min(
            (centre.x - margin) / Mathf.Max(Mathf.Abs(direction.x), 0.0001f),
            (centre.y - margin) / Mathf.Max(Mathf.Abs(direction.y), 0.0001f));
        Vector2 edge = centre + direction * scale;

        // The sprite points down (-Y); turn it to face the target
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + 90f;
        arrow.localEulerAngles = new Vector3(0f, 0f, angle);
        arrow.anchoredPosition = edge - direction * bob;
    }

    /// <summary>
    /// Puts the card where it hides nothing the step is about. The preferred spot is tried
    /// first; if it would cover a target or the arrow, every other spot on the screen is tried
    /// and the one covering the least wins. The card only ever sits where the instructions can
    /// be read next to the thing they describe, never on top of it.
    /// </summary>
    private void PlaceCard(Rect? hole)
    {
        if (!card.gameObject.activeSelf)
            return;

        Vector2 size = root.rect.size;
        Vector2 cardSize = card.rect.size;

        List<Rect> avoid = new List<Rect>();
        foreach (RectTransform target in targets)
        {
            Rect rect;
            if (TryGetLocalRect(target, out rect))
                avoid.Add(Grow(rect, FramePadding + 6f));
        }
        if (arrow.gameObject.activeSelf)
            avoid.Add(Grow(ArrowRect(), 6f));

        Rect skipRect;
        if (TryGetLocalRect(skipButton, out skipRect))
            avoid.Add(Grow(skipRect, 6f));

        // Things the game can open over the screen at any step, like an item's description,
        // are there to be read: the card never covers them
        foreach (Func<RectTransform> keepClear in keepClearOf)
        {
            Rect rect;
            if (TryGetLocalRect(keepClear(), out rect))
                avoid.Add(Grow(rect, 6f));
        }

        Rect best = default;
        float bestOverlap = float.MaxValue;

        foreach (Vector2 centre in CandidateCentres(size, cardSize))
        {
            Rect rect = new Rect(centre - cardSize * 0.5f, cardSize);

            float overlap = 0f;
            foreach (Rect other in avoid)
                overlap += OverlapArea(rect, other);

            // Candidates come in order of preference, so a tie keeps the earlier one
            if (overlap < bestOverlap - 0.5f)
            {
                best = rect;
                bestOverlap = overlap;
                if (overlap <= 0f)
                    break;
            }
        }

        card.anchorMin = card.anchorMax = Vector2.zero;
        card.anchoredPosition = best.center;
    }

    /// <summary>
    /// Where the card may go, most preferred first: the step's own choice, then across the
    /// top and bottom, then down the sides, then dead centre as a last resort.
    /// </summary>
    private IEnumerable<Vector2> CandidateCentres(Vector2 size, Vector2 cardSize)
    {
        float halfW = cardSize.x * 0.5f;
        float halfH = cardSize.y * 0.5f;

        float top = size.y - TopHudHeight - halfH;
        float bottom = EdgeMargin + 28f + halfH;   // above the PRACTICE RUN badge
        float middle = size.y * 0.5f;
        float left = EdgeMargin + halfW;
        float right = size.x - EdgeMargin - halfW;
        float centre = size.x * 0.5f;

        switch (place)
        {
            case CardPlace.Top: yield return new Vector2(centre, top); break;
            case CardPlace.Bottom: yield return new Vector2(centre, bottom); break;
            case CardPlace.Center: yield return new Vector2(centre, middle); break;
        }

        yield return new Vector2(centre, top);
        yield return new Vector2(centre, bottom);
        yield return new Vector2(left, top);
        yield return new Vector2(right, top);
        yield return new Vector2(left, bottom);
        yield return new Vector2(right, bottom);
        yield return new Vector2(left, middle);
        yield return new Vector2(right, middle);
        yield return new Vector2(centre, middle);
    }

    private Rect ArrowRect()
    {
        // The arrow is drawn around its tip, rotated to face its target
        Vector3[] corners = new Vector3[4];
        arrow.GetLocalCorners(corners);
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector3 corner in corners)
        {
            Vector2 p = (Vector2)(arrow.localRotation * corner) + arrow.anchoredPosition;
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static Rect Grow(Rect rect, float amount)
    {
        return Rect.MinMaxRect(rect.xMin - amount, rect.yMin - amount, rect.xMax + amount, rect.yMax + amount);
    }

    private static float OverlapArea(Rect a, Rect b)
    {
        float width = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
        float height = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
        return width > 0f && height > 0f ? width * height : 0f;
    }

    // ----------------------------------------------------------------- geometry

    private bool TryGetTargetsRect(out Rect union)
    {
        union = default;
        bool any = false;

        foreach (RectTransform target in targets)
        {
            Rect rect;
            if (!TryGetLocalRect(target, out rect))
                continue;

            rect.xMin -= FramePadding;
            rect.yMin -= FramePadding;
            rect.xMax += FramePadding;
            rect.yMax += FramePadding;

            union = any ? Rect.MinMaxRect(Mathf.Min(union.xMin, rect.xMin), Mathf.Min(union.yMin, rect.yMin),
                                          Mathf.Max(union.xMax, rect.xMax), Mathf.Max(union.yMax, rect.yMax))
                        : rect;
            any = true;
        }

        return any;
    }

    /// <summary>A HUD element's rectangle in this overlay's units, measured from the bottom-left.</summary>
    private bool TryGetLocalRect(RectTransform target, out Rect rect)
    {
        rect = default;
        if (target == null || !target.gameObject.activeInHierarchy)
            return false;

        Canvas targetCanvas = target.GetComponentInParent<Canvas>();
        Camera cam = targetCanvas != null && targetCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? targetCanvas.rootCanvas.worldCamera
            : null;

        Vector3[] corners = new Vector3[4];
        target.GetWorldCorners(corners);
        Vector2 min = ScreenToLocal(RectTransformUtility.WorldToScreenPoint(cam, corners[0]));
        Vector2 max = ScreenToLocal(RectTransformUtility.WorldToScreenPoint(cam, corners[2]));

        rect = Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y),
                               Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
        return rect.width > 1f && rect.height > 1f;
    }

    private Vector2 ScreenToLocal(Vector2 screen)
    {
        // The root covers the screen from its bottom-left corner, one overlay unit per
        // scaleFactor pixels. Read off the canvas rather than Screen, which can disagree
        // with the size the canvas was actually laid out for.
        float scale = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        return screen / scale;
    }

    // ----------------------------------------------------------------- building blocks

    private Image[] MakeFrame()
    {
        Image[] edges = new Image[4];
        for (int i = 0; i < 4; i++)
        {
            edges[i] = MakeImage("FrameEdge", root, AccentColor);
            edges[i].raycastTarget = false;
            SetCornerAnchored(edges[i].rectTransform);
        }

        // Frames draw over the dims but under the arrow and the card
        foreach (Image edge in edges)
            edge.transform.SetSiblingIndex(arrow.GetSiblingIndex());
        return edges;
    }

    private static void SetFrameActive(Image[] edges, bool active)
    {
        foreach (Image edge in edges)
            edge.enabled = active;
    }

    private static Image MakeImage(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static TextMeshProUGUI MakeText(string name, Transform parent, TMP_FontAsset font, float fontSize, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        if (font != null)
            text.font = font;
        text.fontSize = fontSize;
        text.color = color;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>Anchored at the bottom-left corner, positioned and sized in overlay units.</summary>
    private static void SetCornerAnchored(RectTransform rect)
    {
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
    }
}

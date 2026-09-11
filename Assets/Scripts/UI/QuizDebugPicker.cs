// Test-only tool. The whole file compiles out of a release build, so nothing here can
// reach players: QuizHandler's matching override is behind the same guard.
#if UNITY_EDITOR || DEVELOPMENT_BUILD

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// A floating button, with a dropdown, for choosing which questions a quiz round asks and
/// in what order.
///
/// A round is as long as the session's difficulty asks for (see QuizHandler), so the choice
/// is which ones — and the order you tick them in is the order they are asked. Tick the
/// flashlight first and it is question one. Each row shows its position once picked.
///
/// That is what lets a test run be aimed at the questions you care about, since each one
/// hands over to its own minigame once answered.
///
/// Anything short of a full round is not a usable choice, so the quiz stays on its normal
/// random draw until the last box is ticked. With a pool of exactly six the choice of
/// *which* six is forced, but the ordering is useful whatever the pool size.
///
/// Nothing needs wiring up. The widget builds its own canvas and spawns itself after each
/// scene load, showing only while a <see cref="QuizHandler"/> is present. The choice is
/// remembered in PlayerPrefs, so it survives entering and leaving play mode.
/// </summary>
public class QuizDebugPicker : MonoBehaviour
{
    private const string PREF_KEY = "Debug_QuestionSubset";

    /// <summary>
    /// Tallest the scrolling row list is allowed to get. Sized so the menu still fits under
    /// the button on a 720-high canvas with room to spare on shorter aspect ratios.
    /// </summary>
    private const float MAX_LIST_HEIGHT = 420f;

    private static QuizDebugPicker instance;

    private Canvas canvas;
    private RectTransform widget;      // button + menu, dragged together
    private RectTransform menu;
    private RectTransform content;     // the scrolling rows
    private ScrollRect scroll;
    private LayoutElement scrollElement;
    private TextMeshProUGUI header;
    private TextMeshProUGUI footer;
    private GameObject blocker;        // closes the menu on a click anywhere else
    private TextMeshProUGUI buttonLabel;

    private readonly List<Image> optionBackgrounds = new List<Image>();
    private readonly List<Image> optionBadges = new List<Image>();
    private readonly List<TextMeshProUGUI> optionBadgeLabels = new List<TextMeshProUGUI>();
    private readonly List<int> optionIndices = new List<int>();

    /// <summary>
    /// Chosen questions, in the order they were clicked — which is the order the round asks
    /// them. A list rather than a set precisely because the order carries meaning.
    /// </summary>
    private readonly List<int> picked = new List<int>();

    private QuizHandler quiz;
    private int questionCount;
    private bool isMenuOpen;
    private bool wasRefused;   // last tick was rejected for being over the round length

    private static readonly Color PanelColor    = new Color(0.10f, 0.10f, 0.13f, 0.96f);
    private static readonly Color AccentColor   = new Color(0.85f, 0.20f, 0.60f, 1f);
    private static readonly Color OptionColor   = new Color(1f, 1f, 1f, 0.06f);
    private static readonly Color SelectedColor = new Color(0.85f, 0.20f, 0.60f, 0.55f);
    private static readonly Color BadgeOffColor = new Color(1f, 1f, 1f, 0.10f);
    private static readonly Color BadgeOnColor  = new Color(1f, 0.78f, 0.25f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
            return;

        GameObject host = new GameObject("~QuizDebugPicker");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<QuizDebugPicker>();
    }

    private void Awake()
    {
        BuildUI();
        SceneManager.sceneLoaded += OnSceneLoaded;
        Rebind();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (instance == this)
            instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Rebind();
    }

    /// <summary>
    /// Finds the quiz in whatever scene just loaded and hides the widget when there is
    /// none, so the button only shows up where it is any use.
    /// </summary>
    private void Rebind()
    {
        // Inactive included on purpose: QuizPanel stays switched off until the finish door
        // opens the quiz, so an active-only search would never find it and the button
        // would never appear.
        quiz = FindFirstObjectByType<QuizHandler>(FindObjectsInactive.Include);

        bool hasQuiz = quiz != null;
        canvas.gameObject.SetActive(hasQuiz);

        if (!hasQuiz)
            return;

        questionCount = quiz.GetAllQuestionsForDebug().Length;

        CloseMenu();
        LoadSelection();
        BuildOptions();
        ApplySelection();
    }

    // ------------------------------------------------------------------ selection state

    /// <summary>
    /// How many questions the choice must name. A round is a fixed length, so anything
    /// short of this is not a usable selection — clamped to the pool in case it holds
    /// fewer questions than a round wants.
    /// </summary>
    private int RequiredCount
    {
        get { return Mathf.Min(QuizHandler.CurrentQuestionsPerRound, questionCount); }
    }

    private bool IsComplete
    {
        get { return picked.Count == RequiredCount; }
    }

    /// <summary>
    /// Restores the choice, in order, dropping any index that no longer exists or repeats.
    /// Anything that does not restore to a full round starts from the first six instead.
    /// </summary>
    private void LoadSelection()
    {
        picked.Clear();

        string raw = PlayerPrefs.GetString(PREF_KEY, "");
        if (!string.IsNullOrEmpty(raw))
        {
            foreach (string part in raw.Split(','))
            {
                int index;
                if (int.TryParse(part, out index)
                    && index >= 0 && index < questionCount
                    && !picked.Contains(index))
                {
                    picked.Add(index);
                }
            }
        }

        if (!IsComplete)
            SelectFirst();
    }

    private void SelectFirst()
    {
        picked.Clear();
        for (int i = 0; i < RequiredCount; i++)
            picked.Add(i);
    }

    private void SelectRandom()
    {
        List<int> pool = new List<int>();
        for (int i = 0; i < questionCount; i++)
            pool.Add(i);

        picked.Clear();
        for (int i = 0; i < RequiredCount && pool.Count > 0; i++)
        {
            int at = Random.Range(0, pool.Count);
            picked.Add(pool[at]);
            pool.RemoveAt(at);
        }
    }

    /// <summary>
    /// Pushes the choice to the quiz and saves it, order intact. Only a full round is
    /// handed over — a half-made choice leaves the quiz on its normal random draw, so the
    /// game is never in a state the picker is not actually describing.
    /// </summary>
    private void ApplySelection()
    {
        // Copied, not handed over directly: the quiz must not see later edits to this list
        // part-way through a round.
        QuizHandler.DebugQuestionSubset = IsComplete ? new List<int>(picked) : null;

        PlayerPrefs.SetString(PREF_KEY, string.Join(",", picked.ConvertAll(i => i.ToString()).ToArray()));
        PlayerPrefs.Save();

        RefreshRows();
    }

    /// <summary>
    /// Clicking a row adds it to the end of the running order, or takes it out — at which
    /// point everything after it moves up a place.
    /// </summary>
    private void ToggleQuestion(int index)
    {
        if (picked.Contains(index))
        {
            picked.Remove(index);
            wasRefused = false;
        }
        else if (picked.Count >= RequiredCount)
        {
            // Refuse rather than silently dropping someone else's pick, and say why
            wasRefused = true;
            RefreshRows();
            return;
        }
        else
        {
            picked.Add(index);
            wasRefused = false;
        }

        ApplySelection();
    }

    /// <summary>
    /// Numbers the chosen rows by their place in the running order and puts the count on
    /// the button, so the current setting is readable without opening the menu.
    /// </summary>
    private void RefreshRows()
    {
        for (int i = 0; i < optionIndices.Count; i++)
        {
            int place = picked.IndexOf(optionIndices[i]);
            bool on = place >= 0;

            optionBackgrounds[i].color = on ? SelectedColor : OptionColor;
            optionBadges[i].color = on ? BadgeOnColor : BadgeOffColor;
            optionBadgeLabels[i].text = on ? (place + 1).ToString() : "";
            optionBadgeLabels[i].color = Color.black;
        }

        if (buttonLabel != null)
            buttonLabel.text = picked.Count + "/" + RequiredCount;

        if (footer == null)
            return;

        if (wasRefused)
        {
            footer.text = "A round is " + RequiredCount + " — untick one first";
        }
        else if (IsComplete)
        {
            footer.text = "Asked in this order";
        }
        else
        {
            int missing = RequiredCount - picked.Count;
            footer.text = "Pick " + missing + " more · random " + RequiredCount + " until then";
        }
    }

    // ------------------------------------------------------------------ UI construction

    private void BuildUI()
    {
        GameObject canvasGO = new GameObject("DebugCanvas");
        canvasGO.transform.SetParent(transform, false);
        canvasGO.layer = LayerMask.NameToLayer("UI");

        canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;   // above every gameplay canvas, banners included

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        EnsureEventSystem();

        blocker = new GameObject("Blocker", typeof(RectTransform));
        blocker.transform.SetParent(canvasGO.transform, false);
        Stretch((RectTransform)blocker.transform);
        Image blockerImg = blocker.AddComponent<Image>();
        blockerImg.color = new Color(0f, 0f, 0f, 0f);
        Button blockerBtn = blocker.AddComponent<Button>();
        blockerBtn.transition = Selectable.Transition.None;
        blockerBtn.onClick.AddListener(CloseMenu);
        blocker.SetActive(false);

        widget = NewRect("Widget", canvasGO.transform);
        widget.anchorMin = new Vector2(1f, 1f);
        widget.anchorMax = new Vector2(1f, 1f);
        widget.pivot = new Vector2(1f, 1f);
        widget.anchoredPosition = new Vector2(-16f, -16f);
        widget.sizeDelta = new Vector2(54f, 54f);

        // ---- floating button ----
        RectTransform buttonRT = NewRect("Button", widget);
        Stretch(buttonRT);
        Image buttonImg = buttonRT.gameObject.AddComponent<Image>();
        buttonImg.color = AccentColor;
        buttonImg.sprite = Resources.Load<Sprite>("Sprites/UI/Circle");

        Button button = buttonRT.gameObject.AddComponent<Button>();
        button.targetGraphic = buttonImg;
        button.onClick.AddListener(ToggleMenu);

        DragHandle drag = buttonRT.gameObject.AddComponent<DragHandle>();
        drag.target = widget;
        drag.canvas = canvas;

        RectTransform labelRT = NewRect("Label", buttonRT);
        Stretch(labelRT);
        buttonLabel = MakeText(labelRT, "", 17f, TextAlignmentOptions.Center);

        // ---- dropdown ----
        // Header and footer are pinned; only the question rows scroll, so the count and the
        // RAND / CLEAR actions stay reachable however long the list gets.
        menu = NewRect("Menu", widget);
        menu.anchorMin = new Vector2(1f, 0f);
        menu.anchorMax = new Vector2(1f, 0f);
        menu.pivot = new Vector2(1f, 1f);
        menu.anchoredPosition = new Vector2(0f, -8f);
        menu.sizeDelta = new Vector2(272f, 0f);

        Image menuBg = menu.gameObject.AddComponent<Image>();
        menuBg.color = PanelColor;

        VerticalLayoutGroup layout = menu.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = 3f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = menu.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildHeader();

        RectTransform scrollRT = NewRect("List", menu);
        scrollElement = scrollRT.gameObject.AddComponent<LayoutElement>();

        scroll = scrollRT.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 20f;

        RectTransform viewport = NewRect("Viewport", scrollRT);
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();

        content = NewRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        VerticalLayoutGroup listLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        listLayout.spacing = 3f;
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = true;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = false;

        ContentSizeFitter listFitter = content.gameObject.AddComponent<ContentSizeFitter>();
        listFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewport;
        scroll.content = content;

        RectTransform footerRT = NewRect("Footer", menu);
        footer = MakeText(footerRT, "", 12f, TextAlignmentOptions.Left);
        footer.color = new Color(1f, 1f, 1f, 0.45f);
        footerRT.gameObject.AddComponent<LayoutElement>().minHeight = 20f;

        menu.gameObject.SetActive(false);
    }

    /// <summary>
    /// The pinned title line, with the RAND / CLEAR shortcuts sitting on its right.
    /// </summary>
    private void BuildHeader()
    {
        RectTransform row = NewRect("Header", menu);
        row.gameObject.AddComponent<LayoutElement>().minHeight = 22f;

        RectTransform titleRT = NewRect("Title", row);
        Stretch(titleRT);
        titleRT.offsetMax = new Vector2(-104f, 0f);
        header = MakeText(titleRT, "PICK IN ORDER", 12f, TextAlignmentOptions.Left);
        header.color = new Color(1f, 1f, 1f, 0.45f);

        // No "all" / "none" under a fixed round length — neither is a valid choice. These
        // are the two that are: a fresh random draw, or start over and pick by hand.
        MakeMiniButton(row, "RAND", -52f, delegate
        {
            SelectRandom();
            wasRefused = false;
            ApplySelection();
        });

        MakeMiniButton(row, "CLEAR", 0f, delegate
        {
            picked.Clear();
            wasRefused = false;
            ApplySelection();
        });
    }

    private void MakeMiniButton(Transform parent, string text, float x, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rt = NewRect(text, parent);
        rt.anchorMin = new Vector2(1f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(x, 0f);
        rt.sizeDelta = new Vector2(48f, 18f);

        Image bg = rt.gameObject.AddComponent<Image>();
        bg.color = OptionColor;

        Button btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(onClick);

        RectTransform labelRT = NewRect("Label", rt);
        Stretch(labelRT);
        MakeText(labelRT, text, 11f, TextAlignmentOptions.Center);
    }

    /// <summary>
    /// Builds one row per question. Rebuilt on every scene load so the labels always come
    /// from the quiz actually in the scene.
    /// </summary>
    private void BuildOptions()
    {
        // DestroyImmediate, not Destroy: the layout is measured on open, and a deferred
        // Destroy would leave the old rows still counted.
        for (int i = content.childCount - 1; i >= 0; i--)
            DestroyImmediate(content.GetChild(i).gameObject);

        optionBackgrounds.Clear();
        optionBadges.Clear();
        optionBadgeLabels.Clear();
        optionIndices.Clear();

        QuestionData[] questions = quiz.GetAllQuestionsForDebug();
        for (int i = 0; i < questions.Length; i++)
        {
            // Labelled by the item that answers it — far easier to pick out than the
            // opening words of the question, and it names the minigame that follows.
            string[] answers = questions[i].correctAnswerItemNames;
            string answer = (answers != null && answers.Length > 0) ? answers[0] : "?";
            AddOption((i + 1) + " · " + answer, i);
        }
    }

    private void AddOption(string text, int questionIndex)
    {
        RectTransform rt = NewRect("Option", content);

        Image bg = rt.gameObject.AddComponent<Image>();
        bg.color = OptionColor;

        Button btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        int captured = questionIndex;
        btn.onClick.AddListener(delegate { ToggleQuestion(captured); });

        rt.gameObject.AddComponent<LayoutElement>().minHeight = 30f;

        // Place in the running order, blank until the row is picked
        RectTransform badgeRT = NewRect("Badge", rt);
        badgeRT.anchorMin = new Vector2(0f, 0.5f);
        badgeRT.anchorMax = new Vector2(0f, 0.5f);
        badgeRT.pivot = new Vector2(0f, 0.5f);
        badgeRT.anchoredPosition = new Vector2(8f, 0f);
        badgeRT.sizeDelta = new Vector2(18f, 18f);
        Image badge = badgeRT.gameObject.AddComponent<Image>();
        badge.color = BadgeOffColor;
        badge.raycastTarget = false;

        RectTransform badgeLabelRT = NewRect("Label", badgeRT);
        Stretch(badgeLabelRT);
        TextMeshProUGUI badgeLabel = MakeText(badgeLabelRT, "", 12f, TextAlignmentOptions.Center);

        RectTransform labelRT = NewRect("Label", rt);
        Stretch(labelRT);
        labelRT.offsetMin = new Vector2(33f, 0f);
        labelRT.offsetMax = new Vector2(-10f, 0f);
        MakeText(labelRT, text, 15f, TextAlignmentOptions.Left);

        optionBackgrounds.Add(bg);
        optionBadges.Add(badge);
        optionBadgeLabels.Add(badgeLabel);
        optionIndices.Add(questionIndex);
    }

    /// <summary>
    /// Sizes the scrolling area to the rows, up to <see cref="MAX_LIST_HEIGHT"/>. Below that
    /// the menu is exactly as tall as its contents and nothing scrolls; above it the list
    /// stops growing and the rows scroll inside it, so a long question list can never run
    /// off the bottom of the screen.
    /// </summary>
    private void ResizeList()
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);

        float needed = LayoutUtility.GetPreferredHeight(content);
        scrollElement.preferredHeight = Mathf.Min(needed, MAX_LIST_HEIGHT);
        scrollElement.minHeight = scrollElement.preferredHeight;

        LayoutRebuilder.ForceRebuildLayoutImmediate(menu);
        scroll.verticalNormalizedPosition = 1f;   // always open at the top
    }

    // ------------------------------------------------------------------ behaviour

    private void ToggleMenu()
    {
        if (isMenuOpen)
            CloseMenu();
        else
            OpenMenu();
    }

    private void OpenMenu()
    {
        isMenuOpen = true;
        menu.gameObject.SetActive(true);
        blocker.SetActive(true);
        // Behind the widget, so the widget still takes its own clicks
        blocker.transform.SetSiblingIndex(0);

        // Measured here rather than in BuildOptions: the rows are built while the menu is
        // still switched off, and a layout rebuild on an inactive hierarchy reports zero,
        // which collapsed the list to nothing.
        ResizeList();
        RefreshRows();
    }

    private void CloseMenu()
    {
        isMenuOpen = false;
        if (menu != null)
            menu.gameObject.SetActive(false);
        if (blocker != null)
            blocker.SetActive(false);
    }

    // ------------------------------------------------------------------ helpers

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) != null)
            return;

        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
        DontDestroyOnLoad(es);
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.localScale = Vector3.one;
        return rt;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static TextMeshProUGUI MakeText(RectTransform rt, string text, float size,
                                            TextAlignmentOptions align)
    {
        TextMeshProUGUI label = rt.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.alignment = align;
        label.color = Color.white;
        label.raycastTarget = false;

        TMP_FontAsset font = Resources.Load<TMP_FontAsset>("Fonts/Jersey25-Regular SDF");
        if (font != null)
            label.font = font;

        return label;
    }

    /// <summary>
    /// Lets the widget be dragged out of the way when it covers something being tested.
    /// </summary>
    private class DragHandle : MonoBehaviour, IDragHandler
    {
        public RectTransform target;
        public Canvas canvas;

        public void OnDrag(PointerEventData eventData)
        {
            if (target == null)
                return;

            float scale = canvas != null ? canvas.scaleFactor : 1f;
            target.anchoredPosition += eventData.delta / scale;
        }
    }
}

#endif

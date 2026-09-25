using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The important-documents minigame that runs after the quiz's important-documents question.
///
/// The papers a family would have to prove who they are come out of the bag in a heap — the
/// PSA birth certificate, the Form 137-A, the certificate of enrolment, the medical
/// certificate, the certificate of recognition. One folder sits under the pile with a filename
/// on its tab, and the paper named on that tab is the only one it will take. Sorting them is
/// the whole point being made: documents are no use in an evacuation if nobody can find the
/// right one.
///
/// Only one folder is open at a time, and the arrows either side of it flip between all five,
/// so the player can work in whatever order they like and go back to a folder they have
/// already filled to check or change what is in it. A paper that has been filed peeks out
/// above the folder's lip rather than disappearing behind it, so an open folder always shows
/// what is in it and the paper can be pulled back out.
///
/// Like the other minigames there is nothing to fail. It runs whether the quiz answer was
/// right or wrong, there is no clock, and a paper let go over the wrong folder — or over
/// nothing at all — simply shakes its head and travels back to the heap.
///
/// The papers themselves live in <see cref="DocumentCard"/>; this owns which folder is open,
/// what each one will take and when every paper is where it belongs.
///
/// <see cref="Play"/> is a coroutine so QuizManager can simply yield on it and carry on with
/// the next question once it returns.
/// </summary>
public class ImportantDocumentsMinigame : MonoBehaviour
{
    /// <summary>
    /// One of the five folders. The sprite carries the filename on its tab, so
    /// <see cref="displayName"/> is only there for the optional label drawn over it and for
    /// readable warnings when a panel is wired up wrong.
    /// </summary>
    [Serializable]
    public class FolderSlot
    {
        [Tooltip("The filename on the tab, e.g. \"PSA Birth Certificate\". Shown on the " +
                 "optional label; the sprite already has it drawn on.")]
        public string displayName = "";

        [Tooltip("Folder1..Folder5 — the tabbed lip with this filename written on it.")]
        public Sprite folderSprite;

        [Tooltip("The documentId of the one paper this folder will take. Has to match the " +
                 "documentId on a DocumentCard in the pile exactly.")]
        public string documentId = "";
    }

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private CanvasGroup panelGroup;
    [Tooltip("Blurs the quiz behind the minigame, the same way the correct / wrong overlay " +
             "does. Optional.")]
    [SerializeField] private ScreenBlurBackdrop backdrop;

    [Header("Instruction")]
    [SerializeField] private CanvasGroup instructionCard;
    [SerializeField] private TextMeshProUGUI instructionLabel;
    [SerializeField, TextArea] private string instructionText =
        "Put each document in the folder with its name on the tab";

    [Header("Board")]
    [Tooltip("Everything is positioned inside this rect, and papers are dragged in its space.")]
    [SerializeField] private RectTransform board;
    [Tooltip("The heap the papers start in. Drawn in front of the folder, so one being " +
             "carried passes over it.")]
    [SerializeField] private RectTransform documentLayer;

    [Header("Folder")]
    [Tooltip("The tabbed lip with the filename on it. Its sprite is swapped as the arrows " +
             "flip between folders.")]
    [SerializeField] private Image folderLip;
    [Tooltip("Where a filed paper is parented. Has to sit behind the FolderBase pocket in " +
             "the hierarchy, so a filed paper reads as being inside the folder rather than " +
             "resting in front of it.")]
    [SerializeField] private RectTransform filedLayer;
    [Tooltip("The mouth of the pocket — the window the folder art leaves open, which on the " +
             "FolderBase sprite is the clear rows between the two side walls. A filed paper " +
             "is made as wide as this and hung from its top edge, so the band across the " +
             "opening is what shows and the rest sits behind the front of the pocket. Make it " +
             "any deeper and the paper stands proud of the folder instead of going into it.")]
    [SerializeField] private RectTransform filedSlot;
    [Tooltip("A paper has to be let go over this to be filed. Usually the whole folder.")]
    [SerializeField] private RectTransform dropZone;
    [Tooltip("The filename written on the folder's tab. The art carries the tab but not the " +
             "name, so this draws it — and moves to sit on whichever tab is open, since the " +
             "five sprites stagger their tabs the way a real set of folders does.")]
    [SerializeField] private TextMeshProUGUI folderLabel;

    [Header("Navigation")]
    [Tooltip("Flips to the folder before this one. Wraps around at the ends.")]
    [SerializeField] private Button previousFolderButton;
    [Tooltip("Flips to the folder after this one. Wraps around at the ends.")]
    [SerializeField] private Button nextFolderButton;
    [Tooltip("Optional. Shows how many papers are away, e.g. \"2 / 5 filed\".")]
    [SerializeField] private TextMeshProUGUI progressLabel;
    [Tooltip("Optional. Switched on while the open folder is holding the paper it asked for.")]
    [SerializeField] private GameObject folderFilledMark;

    [Header("Folders")]
    [Tooltip("The five folders, in the order the arrows walk through them.")]
    [SerializeField] private FolderSlot[] folders = new FolderSlot[0];
    [Tooltip("Which folder is open when the round starts.")]
    [SerializeField] private int startingFolder = 0;

    [Header("Feel")]
    [Tooltip("How far a paper is turned as it goes into the folder. The folder is far wider " +
             "than it is deep, so a quarter turn is what lets a portrait certificate sit down " +
             "inside it instead of standing proud of it. A paper picked back up is always " +
             "straightened again, whatever this is set to.")]
    [SerializeField] private float filedRotation = 90f;
    [Tooltip("How much of its slot a filed paper fills once it has been turned. Under 1 so it " +
             "does not touch the folder's edges.")]
    [SerializeField, Range(0.4f, 1f)] private float filedFill = 0.94f;
    [Tooltip("How far the top of a filed certificate stands above the mouth of the pocket.\n\n" +
             "Zero sits it flush, which is what reads as filed: nothing stands above the " +
             "folder and the certificate shows through the opening. The blank border around " +
             "the art is already accounted for by each paper's own artMargins, so this is only " +
             "for deliberately standing one proud.")]
    [SerializeField] private float filedPeek = 0f;
    [Tooltip("How long a paper takes to slide into the folder, or to travel back to the heap.")]
    [SerializeField] private float settleDuration = 0.22f;
    [Tooltip("How far a paper let go over the wrong folder wobbles on its way back, in " +
             "degrees. Zero for no wobble.")]
    [SerializeField] private float rejectShake = 9f;
    [Tooltip("How long that wobble lasts.")]
    [SerializeField] private float rejectShakeDuration = 0.25f;

    [Header("Timing")]
    [SerializeField] private float panelFadeDuration = 0.25f;
    [SerializeField] private float instructionFadeDuration = 0.3f;
    [Tooltip("Pause once the last paper is away, before the minigame closes.")]
    [SerializeField] private float finishDelay = 0.9f;

    [Header("Finish")]
    [Tooltip("Optional. Flashed once every paper is in the right folder.")]
    [SerializeField] private GameObject completedBanner;

    // Everything below is read off the 128x128 folder sprites, so the filename lands on the
    // tab that is actually drawn however the folder is scaled.
    private const float SHEET = 128f;

    /// <summary>Centre of the first folder's tab, in columns from the left of the sprite.</summary>
    private const float TAB_FIRST_CENTRE_COL = 31.5f;

    /// <summary>
    /// How far right each folder's tab sits from the one before it. The five sprites stagger
    /// their tabs by this much so a row of them reads like a real set of folders.
    /// </summary>
    private const float TAB_STEP_COLS = 9f;

    /// <summary>Middle of the tab, in rows down from the top of the sprite.</summary>
    private const float TAB_CENTRE_ROW = 84.5f;

    /// <summary>
    /// How wide the tab is, in columns. The same on all five sprites — only where it sits
    /// changes. The filename is held to this, so a long one wraps onto a second line inside
    /// the tab rather than running off the ends of it.
    /// </summary>
    private const float TAB_WIDTH_COLS = 24f;

    /// <summary>How deep the drawn tab is, in rows.</summary>
    private const float TAB_HEIGHT_ROWS = 7f;

    /// <summary>
    /// How far in from each end of the tab the filename starts. Without it a name that only
    /// just fits runs right to the tab's edges and reads as though it is overflowing even when
    /// it is not; it is also what decides where a long name breaks, since the two-line names
    /// are the ones too wide for the tab once this is taken off both ends.
    /// </summary>
    private const float TAB_TEXT_INSET_COLS = 1.8f;

    private bool isPlaying = false;

    private readonly List<DocumentCard> cards = new List<DocumentCard>();

    /// <summary>Which paper is in each folder, or null for an empty one.</summary>
    private DocumentCard[] filed;

    private int activeFolder = 0;

    private bool homesCaptured = false;

    public bool IsPlaying { get { return isPlaying; } }

    private void Awake()
    {
        if (panelGroup == null && panelRoot != null)
            panelGroup = panelRoot.GetComponent<CanvasGroup>();

        if (instructionLabel != null && !string.IsNullOrEmpty(instructionText))
            instructionLabel.text = instructionText;

        if (previousFolderButton != null)
        {
            // Cleared first: Awake can run more than once across a domain reload, and
            // subscribing twice would skip a folder on every press.
            previousFolderButton.onClick.RemoveListener(ShowPreviousFolder);
            previousFolderButton.onClick.AddListener(ShowPreviousFolder);
        }

        if (nextFolderButton != null)
        {
            nextFolderButton.onClick.RemoveListener(ShowNextFolder);
            nextFolderButton.onClick.AddListener(ShowNextFolder);
        }

        CollectCards();

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

        // The panel lives switched off between rounds, so its Awake has not run yet the first
        // time through — the quiz starts this coroutine on itself, and the panel is only
        // turned on further down in Open. Finding the papers here rather than leaning on Awake
        // is what stops that first run failing the check below and skipping the minigame.
        CollectCards();

        if (board == null || documentLayer == null || filedLayer == null || filedSlot == null
            || dropZone == null || folders.Length == 0 || cards.Count == 0)
        {
            // Bail loudly rather than quietly: a half-wired panel would otherwise hang the
            // quiz on a folder that can never be filled.
            Debug.LogWarning("[ImportantDocumentsMinigame] Needs the board, both layers, the " +
                             "filed slot, the drop zone, at least one folder and at least one " +
                             "document — skipping.", this);
            yield break;
        }

        if (!EveryFolderHasAPaper())
            Debug.LogWarning("[ImportantDocumentsMinigame] A folder is asking for a documentId " +
                             "no paper in the pile carries; that folder can never be filled.",
                             this);

        isPlaying = true;

        Open();

        if (backdrop != null)
            yield return StartCoroutine(backdrop.CaptureIncludingUIRoutine());

        yield return FadeGroup(panelGroup, 0f, 1f, panelFadeDuration);
        yield return FadeGroup(instructionCard, 0f, 1f, instructionFadeDuration);

        foreach (DocumentCard card in cards)
            card.SetArmed(true);

        SetNavigationInteractable(true);

        // Waits on the folders themselves rather than a running tally, so pulling a paper back
        // out un-counts it exactly the way filing it counted it.
        while (FiledCount() < folders.Length)
            yield return null;

        foreach (DocumentCard card in cards)
            card.SetArmed(false);

        SetNavigationInteractable(false);

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

    // ------------------------------------------------------------------ folders

    public void ShowPreviousFolder()
    {
        ShowFolder(activeFolder - 1);
    }

    public void ShowNextFolder()
    {
        ShowFolder(activeFolder + 1);
    }

    /// <summary>
    /// Opens a folder. Wraps at both ends, so the arrows walk round the five rather than
    /// greying out — there is no first or last folder to get stuck on.
    /// </summary>
    public void ShowFolder(int index)
    {
        if (folders.Length == 0)
            return;

        activeFolder = ((index % folders.Length) + folders.Length) % folders.Length;

        FolderSlot slot = folders[activeFolder];

        if (folderLip != null && slot != null && slot.folderSprite != null)
            folderLip.sprite = slot.folderSprite;

        if (folderLabel != null && slot != null)
            folderLabel.text = slot.displayName;

        PlaceLabelOnTab();
        RefreshFiledVisibility();
        RefreshProgress();
    }

    /// <summary>
    /// Fits the filename onto the open folder's tab. The tab is drawn in a different place on
    /// each of the five sprites, so a label left sitting still would only line up with one of
    /// them; working both where it goes and how much room it has out of the sprite's own
    /// geometry keeps the name on the tab, and inside it, whatever size the folder is drawn at.
    ///
    /// The box is the tab itself, so a name too long to fit across it wraps onto a second line
    /// and, failing that, is shrunk by the label's own auto-sizing — either way it stays within
    /// the drawn tab instead of spilling over the folder either side of it.
    /// </summary>
    private void PlaceLabelOnTab()
    {
        if (folderLabel == null || folderLip == null)
            return;

        Rect lip = folderLip.rectTransform.rect;
        float col = TAB_FIRST_CENTRE_COL + TAB_STEP_COLS * activeFolder;

        RectTransform label = folderLabel.rectTransform;

        // Rows are counted down from the top of the sprite, the opposite way to the rect's y.
        label.anchoredPosition = new Vector2(
            (col / SHEET - 0.5f) * lip.width,
            (0.5f - TAB_CENTRE_ROW / SHEET) * lip.height);

        label.sizeDelta = new Vector2(
            TAB_WIDTH_COLS / SHEET * lip.width,
            TAB_HEIGHT_ROWS / SHEET * lip.height);

        // Held off the tab's ends rather than filling it, so the name sits on the tab with a
        // margin either side however wide the folder is drawn.
        float inset = TAB_TEXT_INSET_COLS / SHEET * lip.width;
        folderLabel.margin = new Vector4(inset, 0f, inset, 0f);
    }

    /// <summary>
    /// Only the open folder's paper is drawn. The other four are filed into the same layer and
    /// switched off, which keeps a filed paper's parent stable whichever folder is open.
    /// </summary>
    private void RefreshFiledVisibility()
    {
        if (filed == null || filed.Length == 0)
            return;

        for (int i = 0; i < filed.Length; i++)
        {
            if (filed[i] == null)
                continue;

            filed[i].gameObject.SetActive(i == activeFolder);
        }

        if (folderFilledMark != null)
            folderFilledMark.SetActive(filed[activeFolder] != null);
    }

    private void RefreshProgress()
    {
        if (progressLabel == null)
            return;

        progressLabel.text = FiledCount() + " / " + folders.Length + " filed";
    }

    // ------------------------------------------------------------------ filing

    private void OnPickedUp(DocumentCard card)
    {
        // Any paper can be picked up again, including one already filed, so a change of mind
        // is possible. Lifting it empties its folder and brings it back to the heap layer at
        // its authored size.
        if (card.FolderIndex >= 0 && filed[card.FolderIndex] == card)
            filed[card.FolderIndex] = null;

        card.IsFiled = false;
        card.FolderIndex = -1;

        StopSettling(card);

        card.Rect.SetParent(documentLayer, false);
        card.Rect.sizeDelta = card.HomeSize;
        card.Rect.localRotation = Quaternion.identity;

        // Whatever is being carried draws above the rest of the heap
        card.Rect.SetAsLastSibling();

        RefreshFiledVisibility();
        RefreshProgress();
    }

    private void OnDropped(DocumentCard card)
    {
        // A paper let go anywhere but over the open folder travels back to the heap — nothing
        // is left stranded in the middle of the desk or lost off the edge.
        if (!OverDropZone(card))
        {
            StartSettling(card, SendHome(card, false));
            return;
        }

        FolderSlot slot = folders[activeFolder];

        // The open folder takes the one paper named on its tab and nothing else. A folder that
        // is already holding its paper is full: the two would otherwise sit on top of each
        // other in the same slot.
        bool belongs = slot != null && card.DocumentId == slot.documentId;
        if (!belongs || filed[activeFolder] != null)
        {
            StartSettling(card, SendHome(card, true));
            return;
        }

        filed[activeFolder] = card;
        card.FolderIndex = activeFolder;

        card.Rect.SetParent(filedLayer, false);
        card.Rect.SetAsLastSibling();
        StartSettling(card, FileInto(card));
    }

    /// <summary>How many folders are holding the paper they asked for.</summary>
    private int FiledCount()
    {
        if (filed == null)
            return 0;

        int n = 0;
        for (int i = 0; i < filed.Length; i++)
            if (filed[i] != null && filed[i].IsFiled)
                n++;

        return n;
    }

    /// <summary>
    /// Whether the paper's centre is over the folder. Worked out through world space rather
    /// than anchored positions, so the drop zone can be parented wherever it reads best
    /// without the test quietly moving with it.
    /// </summary>
    private bool OverDropZone(DocumentCard card)
    {
        if (dropZone == null)
            return false;

        Vector3 world = card.Rect.TransformPoint(card.Rect.rect.center);
        return dropZone.rect.Contains(dropZone.InverseTransformPoint(world));
    }

    private IEnumerator FileInto(DocumentCard card)
    {
        Vector2 size = FiledSize(card);
        yield return Settle(card, FiledPosition(card, size), size,
                            Quaternion.Euler(0f, 0f, filedRotation));

        card.IsFiled = true;

        RefreshFiledVisibility();
        RefreshProgress();
    }

    private IEnumerator SendHome(DocumentCard card, bool rejected)
    {
        if (rejected && rejectShake > 0f && rejectShakeDuration > 0f)
            yield return Shake(card);

        yield return Settle(card, card.HomePosition, card.HomeSize, card.HomeRotation);

        card.Rect.SetSiblingIndex(card.HomeSiblingIndex);
    }

    /// <summary>
    /// Where a filed paper comes to rest, as an anchoredPosition.
    ///
    /// The paper is hung from the top of the slot rather than centred in it, because the slot
    /// is the mouth of the pocket — the shallow window the folder art leaves open — and not a
    /// box the whole paper is meant to sit inside. Lining its top edge up with the top of that
    /// window is what makes it read as posted into the folder: the band across the opening is
    /// all that shows, and the rest of it is behind the front of the pocket.
    ///
    /// Going via world space and reading the value back keeps this right whatever anchors the
    /// paper and the slot were authored with.
    /// </summary>
    private Vector2 FiledPosition(DocumentCard card, Vector2 filedSize)
    {
        RectTransform rect = card.Rect;

        Vector2 saved = rect.anchoredPosition;
        rect.position = filedSlot.TransformPoint(
            new Vector3(filedSlot.rect.center.x, filedSlot.rect.yMax, 0f));
        Vector2 atTop = rect.anchoredPosition;
        rect.anchoredPosition = saved;

        // That put the paper's middle on the opening's top edge. Drop it by half its turned
        // depth so its own top edge sits there, then lift it by its blank margin so it is the
        // top of the certificate, not the empty border around it, that lines up with the
        // opening. Flush like that, nothing stands above the folder and what shows through the
        // mouth is the certificate itself.
        float lift = FiledTopMargin(card, filedSize) + filedPeek;
        return atTop - new Vector2(0f, FiledVisualSize(filedSize).y * 0.5f - lift);
    }

    /// <summary>
    /// Whether <see cref="filedRotation"/> lays the paper on its side. A half turn leaves it
    /// upright, so only something nearer a quarter turn than not counts.
    /// </summary>
    private bool FiledTurned
    {
        get
        {
            float turn = Mathf.Abs(Mathf.DeltaAngle(filedRotation, 0f));
            return turn > 45f && turn <= 135f;
        }
    }

    /// <summary>How much room a paper of this size takes up on screen once it has been turned.</summary>
    private Vector2 FiledVisualSize(Vector2 size)
    {
        return FiledTurned ? new Vector2(size.y, size.x) : size;
    }

    /// <summary>
    /// The blank strip between the top of a turned paper's rect and the top of the certificate
    /// actually drawn on it.
    ///
    /// The turn decides which edge of the sheet ends up facing upwards — a quarter turn puts
    /// the sheet's right edge up, three quarters its left — so it also decides which of the
    /// four margins is the one in the way.
    /// </summary>
    private float FiledTopMargin(DocumentCard card, Vector2 size)
    {
        Vector4 m = card.ArtMargins;
        float angle = Mathf.Repeat(filedRotation, 360f);

        if (angle >= 45f && angle < 135f)
            return m.y * size.x;    // the sheet's right edge is uppermost

        if (angle >= 135f && angle < 225f)
            return m.w * size.y;    // upside down, so its bottom edge

        if (angle >= 225f && angle < 315f)
            return m.x * size.x;    // its left edge

        return m.z * size.y;        // still upright
    }

    /// <summary>
    /// Size a filed paper is resized to. Unlike the heap this scales up as well as down: the
    /// point of filing is that the paper ends up sitting in the folder rather than merely near
    /// it, so it is sized to the folder instead of being left at whatever size it was picked
    /// up at.
    ///
    /// Only how wide it is matters. A paper goes into the folder top edge first and the rest of
    /// it carries on down behind the front of the pocket, so its depth never has to fit —
    /// sizing it to the opening's height as well would shrink a certificate to a postage stamp.
    /// </summary>
    private Vector2 FiledSize(DocumentCard card)
    {
        Vector2 home = card.HomeSize;
        if (home.x <= 0f || home.y <= 0f)
            return filedSlot.rect.size;

        // The paper's own side that ends up lying across the folder.
        float across = FiledTurned ? home.y : home.x;
        if (across <= 0f)
            return home;

        return home * (filedSlot.rect.width * filedFill / across);
    }

    /// <summary>
    /// The little "no" wobble a paper gives before heading back to the heap. It shakes where
    /// it was let go rather than on the way, so the refusal reads as coming from the folder.
    /// </summary>
    private IEnumerator Shake(DocumentCard card)
    {
        RectTransform rect = card.Rect;
        Quaternion from = rect.localRotation;

        for (float t = 0f; t < rejectShakeDuration; t += Time.unscaledDeltaTime)
        {
            float k = t / rejectShakeDuration;

            // Three swings, damped, so it settles rather than stopping dead mid-swing
            float angle = Mathf.Sin(k * Mathf.PI * 6f) * rejectShake * (1f - k);
            rect.localRotation = from * Quaternion.Euler(0f, 0f, angle);
            yield return null;
        }

        rect.localRotation = from;
    }

    /// <summary>
    /// Slides a paper to where it is going. Unscaled time, so it moves at the same rate
    /// whatever the game's timescale is doing behind the blur.
    /// </summary>
    private IEnumerator Settle(DocumentCard card, Vector2 target, Vector2 size, Quaternion toRotation)
    {
        RectTransform rect = card.Rect;
        Vector2 fromPosition = rect.anchoredPosition;
        Vector2 fromSize = rect.sizeDelta;
        Quaternion fromRotation = rect.localRotation;

        for (float t = 0f; t < settleDuration; t += Time.unscaledDeltaTime)
        {
            float k = settleDuration <= 0f ? 1f : t / settleDuration;

            // Ease out, so it arrives settling rather than at full speed
            float eased = 1f - (1f - k) * (1f - k);

            rect.anchoredPosition = Vector2.Lerp(fromPosition, target, eased);
            rect.sizeDelta = Vector2.Lerp(fromSize, size, eased);
            rect.localRotation = Quaternion.Slerp(fromRotation, toRotation, eased);
            yield return null;
        }

        rect.anchoredPosition = target;
        rect.sizeDelta = size;
        rect.localRotation = toRotation;
    }

    // One settle per paper, so grabbing one mid-flight does not leave an old tween still
    // driving it into the folder it was headed for.
    private readonly Dictionary<DocumentCard, Coroutine> settling =
        new Dictionary<DocumentCard, Coroutine>();

    private void StartSettling(DocumentCard card, IEnumerator routine)
    {
        StopSettling(card);

        if (isActiveAndEnabled)
            settling[card] = StartCoroutine(routine);
    }

    private void StopSettling(DocumentCard card)
    {
        Coroutine running;
        if (!settling.TryGetValue(card, out running))
            return;

        if (running != null)
            StopCoroutine(running);

        settling.Remove(card);
    }

    // ------------------------------------------------------------------ setup

    /// <summary>
    /// Finds the papers authored under the document layer and wires them up. Their authored
    /// spots are recorded once, the first time through, so every run starts from the same heap.
    /// </summary>
    private void CollectCards()
    {
        if (documentLayer == null)
            return;

        cards.Clear();
        documentLayer.GetComponentsInChildren(true, cards);

        Canvas owning = panelRoot != null ? panelRoot.GetComponentInParent<Canvas>() : null;

        foreach (DocumentCard card in cards)
        {
            card.Configure(board, owning);

            if (!homesCaptured)
                card.CaptureHome();

            // Cleared first: Awake can run more than once across a domain reload, and
            // subscribing twice would file a paper on one drop and immediately unfile it.
            card.PickedUp -= OnPickedUp;
            card.Dropped -= OnDropped;
            card.PickedUp += OnPickedUp;
            card.Dropped += OnDropped;

            card.SetArmed(false);
        }

        homesCaptured = true;

        if (filed == null || filed.Length != folders.Length)
            filed = new DocumentCard[folders.Length];
    }

    /// <summary>
    /// Whether every folder is asking for a paper that is actually in the heap. A folder that
    /// is not would leave the round waiting on a paper that can never arrive.
    /// </summary>
    private bool EveryFolderHasAPaper()
    {
        foreach (FolderSlot slot in folders)
        {
            if (slot == null)
                return false;

            bool found = false;
            foreach (DocumentCard card in cards)
            {
                if (card != null && card.DocumentId == slot.documentId)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                return false;
        }

        return true;
    }

    private void SetNavigationInteractable(bool value)
    {
        if (previousFolderButton != null)
            previousFolderButton.interactable = value;

        if (nextFolderButton != null)
            nextFolderButton.interactable = value;
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

        if (completedBanner != null)
            completedBanner.SetActive(false);

        // Sizes are only real once the layout has been built, so anything read off a rect —
        // the drop test and the filed slot — is left until here rather than done in Awake.
        Canvas.ForceUpdateCanvases();

        ResetBoard();
    }

    /// <summary>Empties every folder, puts every paper back in the heap and reopens the first.</summary>
    private void ResetBoard()
    {
        if (filed != null)
            for (int i = 0; i < filed.Length; i++)
                filed[i] = null;

        foreach (DocumentCard card in cards)
        {
            if (card == null)
                continue;

            StopSettling(card);
            card.Rect.SetParent(documentLayer, false);
            card.GoHome();
            card.SetArmed(false);
            card.gameObject.SetActive(true);
        }

        SetNavigationInteractable(false);

        ShowFolder(startingFolder);
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
    /// Blanks everything the panel owns, without touching whether the panel itself is on —
    /// see the note in Awake.
    /// </summary>
    private void ResetVisuals()
    {
        if (filed != null)
            ResetBoard();

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

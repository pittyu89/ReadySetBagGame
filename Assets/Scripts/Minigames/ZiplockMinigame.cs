using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The ziplock minigame that runs after the quiz's ziplock question.
///
/// The bag starts sealed with its zipper in the top-left corner. Nothing can be put in until
/// the player has dragged that zipper the whole way across, so the round opens on the gesture
/// the bag is named for; the seal fills in behind the zipper as it travels, so a half-done
/// pull reads as half-open rather than staying shut until the last pixel.
///
/// Once it is open the six things laid out around it — the pad and pen, the ID, the money,
/// the powerbank, the phone and the batteries — are dragged in. All six belong in the bag;
/// like the other minigames there is nothing to fail here, because the question has already
/// been scored by the time this runs. An item let go anywhere but over the open bag simply
/// goes back to where it was lying, so nothing is ever lost off the edge of the table.
///
/// An item dropped in is taken behind the bag graphic rather than left on top of it. The
/// sprite's interior is translucent white, so it reads as being inside the plastic instead of
/// resting in front of it, and it is shrunk into one of six slots so a full bag looks packed
/// rather than piled. It stays draggable afterwards and can be pulled back out.
///
/// None of the geometry is authored by hand. Where the zipper sits shut, how far it travels
/// and the interior the items are packed into are all read off the 64x64 ZiplockBase sprite,
/// so they line up with what is drawn however the sprite is scaled.
///
/// <see cref="Play"/> is a coroutine so QuizHandler can simply yield on it and carry on with
/// the next question once it returns.
/// </summary>
public class ZiplockMinigame : MonoBehaviour
{
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
        "Put only the important things in the zip lock";

    [Header("Board")]
    [Tooltip("Everything is positioned inside this rect, and items are dragged in its space.")]
    [SerializeField] private RectTransform board;
    [Tooltip("The bag itself. All the geometry is read off this rect and the sprite drawn in it.")]
    [SerializeField] private RectTransform bag;
    [Tooltip("The zipper, given the bag's own rect so the art lands on the seal by itself.")]
    [SerializeField] private ZiplockZipperHandle zipper;
    [Tooltip("Where the loose items sit. Drawn in front of the bag, so one being carried " +
             "passes over it.")]
    [SerializeField] private RectTransform itemLayer;
    [Tooltip("Where packed items are moved to. Drawn behind the bag, so they show through " +
             "the translucent plastic instead of sitting on top of it.")]
    [SerializeField] private RectTransform insideLayer;

    [Header("Packing")]
    [Tooltip("Columns and rows the inside of the bag is divided into. Three by two holds the " +
             "six things the round asks for.")]
    [SerializeField] private int packColumns = 3;
    [SerializeField] private int packRows = 2;
    [Tooltip("How much of its slot a packed item fills. Under 1 so neighbours do not touch.")]
    [SerializeField, Range(0.4f, 1f)] private float packFill = 0.82f;
    [Tooltip("How long an item takes to settle into its slot, or to travel back to the table.")]
    [SerializeField] private float settleDuration = 0.22f;
    [Tooltip("How far a packed item is tilted, in degrees, so the bag looks packed by hand.")]
    [SerializeField] private float packTilt = 7f;

    [Header("Timing")]
    [SerializeField] private float panelFadeDuration = 0.25f;
    [SerializeField] private float instructionFadeDuration = 0.3f;
    [Tooltip("Pause once the last thing is in, before the minigame closes.")]
    [SerializeField] private float finishDelay = 0.9f;

    [Header("Finish")]
    [Tooltip("Optional. Flashed once everything is in the bag.")]
    [SerializeField] private GameObject completedBanner;

    // Everything below is read off the 64x64 ZiplockBase sprite, so the zipper's travel and
    // the interior line up with what is actually drawn however the sprite is scaled.
    private const float SHEET = 64f;

    /// <summary>Columns the bag itself occupies. Outside these the sprite is empty.</summary>
    private const float BAG_LEFT_COL = 10f;
    private const float BAG_RIGHT_COL = 54f;

    /// <summary>Bottom of the bag, counted from the bottom of the sprite.</summary>
    private const float BAG_BOTTOM_ROW = 9f;

    /// <summary>The yellow seal, rows 44-46. The zipper runs along it and items go in under it.</summary>
    private const float SEAL_BOTTOM_ROW = 44f;

    /// <summary>Columns the zipper art occupies when the bag is shut.</summary>
    private const float ZIPPER_LEFT_COL = 11f;
    private const float ZIPPER_WIDTH_COLS = 7f;

    private bool isPlaying = false;

    private readonly List<ZiplockItem> items = new List<ZiplockItem>();

    /// <summary>Which item is in each slot inside the bag, or null for a free one.</summary>
    private ZiplockItem[] slots;

    /// <summary>The inside of the bag, in board space — what an item has to be dropped over.</summary>
    private Rect interior;

    private bool homesCaptured = false;

    public bool IsPlaying { get { return isPlaying; } }

    private void Awake()
    {
        if (panelGroup == null && panelRoot != null)
            panelGroup = panelRoot.GetComponent<CanvasGroup>();

        if (instructionLabel != null && !string.IsNullOrEmpty(instructionText))
            instructionLabel.text = instructionText;

        CollectItems();

        // Reset the contents but leave the panel's active state alone. Awake first runs
        // during the SetActive in Open, so deactivating here would switch the panel back off
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
        // turned on further down in Open. Finding the items here rather than leaning on Awake
        // is what stops that first run failing the check below and skipping the minigame.
        CollectItems();

        if (board == null || bag == null || zipper == null || itemLayer == null
            || insideLayer == null || items.Count == 0)
        {
            // Bail loudly rather than quietly: a half-wired panel would otherwise hang the
            // quiz on a bag that can never be filled.
            Debug.LogWarning("[ZiplockMinigame] Needs the board, bag, zipper, both item layers " +
                             "and at least one item — skipping.", this);
            yield break;
        }

        isPlaying = true;

        Open();

        if (backdrop != null)
            yield return StartCoroutine(backdrop.CaptureIncludingUIRoutine());

        yield return FadeGroup(panelGroup, 0f, 1f, panelFadeDuration);
        yield return FadeGroup(instructionCard, 0f, 1f, instructionFadeDuration);

        // The zipper is the only thing live to begin with. Nothing can be put in a shut bag,
        // so the items stay inert until it has been pulled across.
        zipper.SetArmed(true);

        while (!zipper.IsOpen)
            yield return null;

        foreach (ZiplockItem item in items)
            item.SetArmed(true);

        // Waits on the bag itself rather than a running tally, so pulling something back out
        // un-counts it exactly the way putting it in counted it.
        while (PackedCount() < items.Count)
            yield return null;

        foreach (ZiplockItem item in items)
            item.SetArmed(false);

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

    // ------------------------------------------------------------------ packing

    private void OnPickedUp(ZiplockItem item)
    {
        // Anything can be picked up again, including something already in the bag, so a
        // change of mind is possible. Lifting it frees its slot and brings it back to the
        // front layer at its authored size.
        if (item.Slot >= 0 && slots[item.Slot] == item)
            slots[item.Slot] = null;

        item.IsPacked = false;
        item.Slot = -1;

        StopSettling(item);

        item.Rect.SetParent(itemLayer, false);
        item.Rect.sizeDelta = item.HomeSize;
        item.Rect.localRotation = Quaternion.identity;

        // Whatever is being carried draws above the rest
        item.Rect.SetAsLastSibling();
    }

    private void OnDropped(ZiplockItem item)
    {
        // Only an open bag takes anything, and only a drop over its inside counts. Let go
        // anywhere else and the item travels back to where it was lying — nothing is left
        // stranded in the middle of the table or lost off the edge.
        if (!zipper.IsOpen || !interior.Contains(item.Rect.anchoredPosition))
        {
            StartSettling(item, SendHome(item));
            return;
        }

        int slot = NearestFreeSlot(item.Rect.anchoredPosition);
        if (slot < 0)
        {
            // Every slot taken and this one is not in any of them. Nothing sensible to do but
            // put it back; it cannot happen while there are as many slots as items.
            StartSettling(item, SendHome(item));
            return;
        }

        slots[slot] = item;
        item.Slot = slot;

        item.Rect.SetParent(insideLayer, false);
        StartSettling(item, PackInto(item, slot));
    }

    /// <summary>How many things are lying inside the bag.</summary>
    private int PackedCount()
    {
        int n = 0;
        for (int i = 0; i < items.Count; i++)
            if (items[i] != null && items[i].IsPacked)
                n++;

        return n;
    }

    /// <summary>
    /// The free slot nearest where the item was let go, so a drop lands roughly where it was
    /// aimed rather than always filling the bag left to right.
    /// </summary>
    private int NearestFreeSlot(Vector2 dropPoint)
    {
        int best = -1;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null)
                continue;

            float d = (SlotCentre(i) - dropPoint).sqrMagnitude;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }

        return best;
    }

    /// <summary>Centre of a packing slot, in board space.</summary>
    private Vector2 SlotCentre(int slot)
    {
        int column = slot % packColumns;
        int row = slot / packColumns;

        float cellWidth = interior.width / packColumns;
        float cellHeight = interior.height / packRows;

        // Row 0 is the top one, which is the order things read in
        return new Vector2(
            interior.xMin + cellWidth * (column + 0.5f),
            interior.yMax - cellHeight * (row + 0.5f));
    }

    /// <summary>
    /// Size a packed item is shrunk to: as large as fits its slot without distorting the art,
    /// so the phone stays tall and the money stays wide.
    /// </summary>
    private Vector2 PackedSize(ZiplockItem item)
    {
        float cellWidth = interior.width / packColumns * packFill;
        float cellHeight = interior.height / packRows * packFill;

        Vector2 home = item.HomeSize;
        if (home.x <= 0f || home.y <= 0f)
            return new Vector2(cellWidth, cellHeight);

        float scale = Mathf.Min(cellWidth / home.x, cellHeight / home.y);

        // Only ever shrink. Something already smaller than its slot is left at its own size
        // rather than blown up to fill it.
        scale = Mathf.Min(scale, 1f);

        return home * scale;
    }

    private IEnumerator PackInto(ZiplockItem item, int slot)
    {
        Vector2 target = SlotCentre(slot);
        Vector2 size = PackedSize(item);

        // A little tilt, steady per slot rather than random, so a bag packed the same way
        // twice looks the same both times.
        float tilt = packTilt * ((slot % 2 == 0) ? 1f : -1f);

        yield return Settle(item, target, size, tilt);

        item.IsPacked = true;
    }

    private IEnumerator SendHome(ZiplockItem item)
    {
        yield return Settle(item, item.HomePosition, item.HomeSize, 0f);
    }

    /// <summary>
    /// Slides an item to where it is going. Unscaled time, so it moves at the same rate
    /// whatever the game's timescale is doing behind the blur.
    /// </summary>
    private IEnumerator Settle(ZiplockItem item, Vector2 target, Vector2 size, float tilt)
    {
        RectTransform rect = item.Rect;
        Vector2 fromPosition = rect.anchoredPosition;
        Vector2 fromSize = rect.sizeDelta;
        Quaternion fromRotation = rect.localRotation;
        Quaternion toRotation = Quaternion.Euler(0f, 0f, tilt);

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

    // One settle per item, so grabbing something mid-flight does not leave an old tween still
    // driving it into the slot it was headed for.
    private readonly Dictionary<ZiplockItem, Coroutine> settling =
        new Dictionary<ZiplockItem, Coroutine>();

    private void StartSettling(ZiplockItem item, IEnumerator routine)
    {
        StopSettling(item);

        if (isActiveAndEnabled)
            settling[item] = StartCoroutine(routine);
    }

    private void StopSettling(ZiplockItem item)
    {
        Coroutine running;
        if (!settling.TryGetValue(item, out running))
            return;

        if (running != null)
            StopCoroutine(running);

        settling.Remove(item);
    }

    // ------------------------------------------------------------------ setup

    /// <summary>
    /// Works out the zipper's travel and the inside of the bag, in board space, from the
    /// sprite's own geometry.
    /// </summary>
    private void BuildGeometry()
    {
        float w = bag.rect.width;
        float h = bag.rect.height;
        float left = bag.anchoredPosition.x - w * bag.pivot.x;
        float bottom = bag.anchoredPosition.y - h * bag.pivot.y;

        // The zipper graphic is given the bag's whole rect, so it starts in the right place
        // with no offset of its own; only the distance it travels has to be worked out. It
        // stops when its right edge reaches the right edge of the bag.
        float travelCols = (BAG_RIGHT_COL - ZIPPER_WIDTH_COLS) - ZIPPER_LEFT_COL;

        Canvas owning = panelRoot != null ? panelRoot.GetComponentInParent<Canvas>() : null;
        zipper.Configure(travelCols / SHEET * w, board, owning);

        // Under the seal and inside the bag's own edges — where a dropped item has to land,
        // and the space the six slots are laid out in.
        float x0 = left + BAG_LEFT_COL / SHEET * w;
        float x1 = left + BAG_RIGHT_COL / SHEET * w;
        float y0 = bottom + BAG_BOTTOM_ROW / SHEET * h;
        float y1 = bottom + SEAL_BOTTOM_ROW / SHEET * h;

        interior = new Rect(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>
    /// Finds the items authored under the item layer and wires them up. Their authored spots
    /// are recorded once, the first time through, so every run starts from the same table.
    /// </summary>
    private void CollectItems()
    {
        if (itemLayer == null)
            return;

        items.Clear();
        itemLayer.GetComponentsInChildren(true, items);

        Canvas owning = panelRoot != null ? panelRoot.GetComponentInParent<Canvas>() : null;

        foreach (ZiplockItem item in items)
        {
            item.Configure(board, owning);

            if (!homesCaptured)
                item.CaptureHome();

            // Cleared first: Awake can run more than once across a domain reload, and
            // subscribing twice would pack an item on one drop and immediately unpack it.
            item.PickedUp -= OnPickedUp;
            item.Dropped -= OnDropped;
            item.PickedUp += OnPickedUp;
            item.Dropped += OnDropped;

            item.SetArmed(false);
        }

        homesCaptured = true;

        slots = new ZiplockItem[Mathf.Max(items.Count, packColumns * packRows)];
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

        // Sizes are only real once the layout has been built, so the geometry is worked out
        // here rather than in Awake.
        Canvas.ForceUpdateCanvases();
        BuildGeometry();

        ResetBoard();
    }

    /// <summary>Puts every item back on the table and shuts the bag.</summary>
    private void ResetBoard()
    {
        for (int i = 0; i < slots.Length; i++)
            slots[i] = null;

        foreach (ZiplockItem item in items)
        {
            if (item == null)
                continue;

            StopSettling(item);
            item.Rect.SetParent(itemLayer, false);
            item.GoHome();
            item.SetArmed(false);
        }

        if (zipper != null)
        {
            zipper.SetArmed(false);
            zipper.ResetZipper();
        }
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
        if (slots != null)
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
}

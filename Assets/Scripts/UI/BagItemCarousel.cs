using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows the items of one inventory section through a row of single-item slots, with arrows that
/// slide the view one item at a time. After the last item comes one empty slot to drop new items
/// into. Used by the Small Bag (one slot) and the Medium Bag's top (three slots).
///
/// Each slot carries an InventoryGridDisplay for the section (its own grid cells kept hidden), so
/// the existing drag-and-drop code can move items in and out without knowing about this view.
/// Any number of items fits - only the go-bag weight limit applies.
/// </summary>
public class BagItemCarousel : MonoBehaviour
{
    [SerializeField] private string sectionName = "SmallBag";
    [Tooltip("Left to right. Each needs an InventoryGridDisplay for the section.")]
    [SerializeField] private RectTransform[] slots = new RectTransform[] { };
    [SerializeField] private float itemPadding = 12f;
    [SerializeField] private Button previousButton;
    [SerializeField] private Button nextButton;
    [Tooltip("Draw each slot with the inventory grid's empty / occupied cell sprites, so the slots look like grid cells.")]
    [SerializeField] private bool useGridSlotSprites = false;

    // Display order of the packed items
    private readonly List<InventoryItem> order = new List<InventoryItem>();
    private int start;
    private GameObject[] views;
    private bool shown;
    private bool subscribed;

    public bool IsShown => shown;

    /// <summary>Entries the arrows move through: every item plus one empty slot at the end.</summary>
    private int Length => order.Count + 1;

    void Awake()
    {
        views = new GameObject[slots.Length];

        if (previousButton != null)
            previousButton.onClick.AddListener(() => Step(-1));

        if (nextButton != null)
            nextButton.onClick.AddListener(() => Step(1));

        SetControlsVisible(false);
    }

    void OnEnable()
    {
        if (!subscribed && InventoryManager.Instance != null)
        {
            InventoryManager.Instance.OnInventoryChanged += OnInventoryChanged;
            subscribed = true;
        }
    }

    void OnDisable()
    {
        if (subscribed && InventoryManager.Instance != null)
            InventoryManager.Instance.OnInventoryChanged -= OnInventoryChanged;
        subscribed = false;

        Hide();
    }

    void LateUpdate()
    {
        if (!shown || InventoryItemDragHandler.IsAnyItemBeingDragged || views == null)
            return;

        // After a drag the handler puts the item back under its slot but keeps it where it was let
        // go - over the quiz answer box, or wherever the drop missed. Normal grids hide that by
        // rebuilding their items; these views aren't rebuilt by them, so rebuild them here.
        for (int i = 0; i < views.Length; i++)
        {
            if (views[i] == null)
                continue;

            RectTransform rect = (RectTransform)views[i].transform;
            bool inPlace = rect.parent == slots[i]
                           && rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one
                           && rect.offsetMin == new Vector2(itemPadding, itemPadding)
                           && rect.offsetMax == new Vector2(-itemPadding, -itemPadding);

            if (!inPlace)
            {
                Refresh();
                return;
            }
        }
    }

    /// <summary>Shows the slots and arrows, optionally shuffling the contents first.</summary>
    public void Show(bool shuffle)
    {
        shown = true;

        if (shuffle)
            Shuffle();
        else
            SyncOrder();

        SetControlsVisible(true);
        Refresh();
    }

    public void Hide()
    {
        shown = false;
        SetControlsVisible(false);
        ClearViews();
    }

    /// <summary>Reshuffles while shown (e.g. a storage was opened next to the bag).</summary>
    public void Reshuffle()
    {
        if (!shown)
            return;

        InventoryItemDragHandler.ForceHideDescriptionPanel();
        Shuffle();
        Refresh();
    }

    private void Step(int direction)
    {
        if (!shown || Length <= slots.Length)
            return;

        start = (start + direction + Length) % Length;
        InventoryItemDragHandler.ForceHideDescriptionPanel();
        Refresh();
    }

    /// <summary>Random order of everything in the section (Fisher-Yates).</summary>
    private void Shuffle()
    {
        order.Clear();
        order.AddRange(GetPackedItems());

        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            InventoryItem temp = order[i];
            order[i] = order[j];
            order[j] = temp;
        }

        start = 0;
    }

    /// <summary>Keeps the current order, dropping removed items and appending new ones.</summary>
    private InventoryItem SyncOrder()
    {
        List<InventoryItem> packed = GetPackedItems();
        order.RemoveAll(item => !packed.Contains(item));

        InventoryItem added = null;
        foreach (InventoryItem item in packed)
        {
            if (!order.Contains(item))
            {
                order.Add(item);
                added = item;
            }
        }

        start = Mathf.Clamp(start, 0, Length - 1);
        return added;
    }

    private void OnInventoryChanged()
    {
        if (!shown)
            return;

        InventoryItem added = SyncOrder();

        // Make sure a newly packed item is on screen
        if (added != null && !IsVisible(order.IndexOf(added)))
            start = order.IndexOf(added);

        Refresh();
    }

    /// <summary>The entry shown in slot <paramref name="slot"/>; an index of order.Count or more is empty.</summary>
    private int EntryAt(int slot)
    {
        // Fewer entries than slots: no scrolling, items first then empty slots
        if (Length <= slots.Length)
            return slot;

        return (start + slot) % Length;
    }

    private bool IsVisible(int entry)
    {
        for (int i = 0; i < slots.Length; i++)
            if (EntryAt(i) == entry)
                return true;
        return false;
    }

    private List<InventoryItem> GetPackedItems()
    {
        var items = new List<InventoryItem>();
        if (InventoryManager.Instance != null)
            items.AddRange(InventoryManager.Instance.GetSectionItems(sectionName));
        return items;
    }

    private void Refresh()
    {
        ClearViews();

        bool canStep = shown && Length > slots.Length;
        if (previousButton != null)
            previousButton.interactable = canStep;
        if (nextButton != null)
            nextButton.interactable = canStep;

        if (!shown)
            return;

        InventoryGrid grid = InventoryManager.Instance != null ? InventoryManager.Instance.GetGrid(sectionName) : null;
        if (grid == null)
            return;

        for (int i = 0; i < slots.Length; i++)
        {
            int entry = EntryAt(i);
            if (slots[i] == null)
                continue;

            bool occupied = entry < order.Count;

            if (useGridSlotSprites)
            {
                Image cell = slots[i].GetComponent<Image>();
                if (cell != null)
                    cell.sprite = occupied ? InventoryManager.Instance.GetOccupiedSlotSprite() : InventoryManager.Instance.GetEmptySlotSprite();
            }

            if (occupied)
                views[i] = CreateView(order[entry], slots[i], grid);
        }
    }

    private GameObject CreateView(InventoryItem item, RectTransform slot, InventoryGrid grid)
    {
        // The drag code identifies the item by its grid origin, so look that up
        FindOrigin(grid, item, out int originX, out int originY);

        var view = new GameObject(item.itemName, typeof(RectTransform));
        view.layer = gameObject.layer;
        RectTransform rect = (RectTransform)view.transform;
        rect.SetParent(slot, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(itemPadding, itemPadding);
        rect.offsetMax = new Vector2(-itemPadding, -itemPadding);

        Image image = view.AddComponent<Image>();
        image.sprite = item.itemSprite;
        image.preserveAspect = true;

        Button button = view.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;

        view.AddComponent<InventoryGridItemUI>().Initialize(item, originX, originY, sectionName);
        view.AddComponent<InventoryItemDragHandler>();
        return view;
    }

    private static void FindOrigin(InventoryGrid grid, InventoryItem item, out int originX, out int originY)
    {
        for (int y = 0; y < grid.GetHeight(); y++)
        {
            for (int x = 0; x < grid.GetWidth(); x++)
            {
                InventorySlot slot = grid.GetSlotAt(x, y);
                if (slot != null && slot.item == item && slot.itemGridX == 0 && slot.itemGridY == 0)
                {
                    originX = x;
                    originY = y;
                    return;
                }
            }
        }

        originX = 0;
        originY = 0;
    }

    private void ClearViews()
    {
        if (views == null)
            return;

        for (int i = 0; i < views.Length; i++)
        {
            if (views[i] != null)
                Destroy(views[i]);
            views[i] = null;
        }
    }

    private void SetControlsVisible(bool visible)
    {
        foreach (RectTransform slot in slots)
            if (slot != null)
                slot.gameObject.SetActive(visible);

        if (previousButton != null)
            previousButton.gameObject.SetActive(visible);

        if (nextButton != null)
            nextButton.gameObject.SetActive(visible);
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Small Bag (blue) in the inventory panel. It has a single slot that shows one packed item
/// at a time; the arrows step through everything inside, plus an empty slot at the end to drop
/// the next item into. The bag holds any number of items -
/// only the go-bag weight limit applies - and the order is shuffled every time it is opened.
///
/// Items live in the "SmallBag" InventoryManager section, so weight, the HUD meter, the quiz and
/// scoring all see them like any other go-bag section. The slot carries an InventoryGridDisplay
/// for that section (its own grid cells are kept hidden) so the existing drag-and-drop code can
/// move items in and out without knowing about this view.
/// </summary>
public class SmallBagController : MonoBehaviour
{
    [SerializeField] private string sectionName = "SmallBag";

    [Header("Bag")]
    [SerializeField] private Image bagImage;
    [SerializeField] private Button openButton;
    [Tooltip("Closed bag to gray slot, in order.")]
    [SerializeField] private Sprite[] openFrames = new Sprite[] { };
    [Tooltip("Gray slot back to closed bag, in order.")]
    [SerializeField] private Sprite[] closeFrames = new Sprite[] { };
    [SerializeField] private float framesPerSecond = 24f;

    [Header("Slot")]
    [Tooltip("Covers the gray slot. Holds the InventoryGridDisplay used as the drop target.")]
    [SerializeField] private RectTransform slotArea;
    [SerializeField] private float itemPadding = 12f;
    [SerializeField] private Button previousButton;
    [SerializeField] private Button nextButton;
    [SerializeField] private Button closeButton;

    [Header("Audio")]
    [SerializeField] private AudioClip openAudio;
    [SerializeField] private AudioClip closeAudio;

    private enum State { Closed, Opening, Open, Closing }
    private State state = State.Closed;

    // Display order of the packed items; reshuffled on every open
    private readonly List<InventoryItem> order = new List<InventoryItem>();
    private int currentIndex;
    private GameObject currentItemView;
    private Coroutine animating;
    private bool subscribed;

    void Awake()
    {
        if (openButton != null)
            openButton.onClick.AddListener(Open);

        if (previousButton != null)
            previousButton.onClick.AddListener(() => Step(-1));

        if (nextButton != null)
            nextButton.onClick.AddListener(() => Step(1));

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
    }

    void OnEnable()
    {
        if (!subscribed && InventoryManager.Instance != null)
        {
            InventoryManager.Instance.OnInventoryChanged += OnInventoryChanged;
            subscribed = true;
        }

        // The inventory panel always shows the bag closed when it comes up
        ResetClosed();
    }

    void OnDisable()
    {
        if (subscribed && InventoryManager.Instance != null)
            InventoryManager.Instance.OnInventoryChanged -= OnInventoryChanged;
        subscribed = false;

        animating = null;
        state = State.Closed;
        ClearItemView();
    }

    void LateUpdate()
    {
        if (state != State.Open || currentItemView == null || InventoryItemDragHandler.IsAnyItemBeingDragged)
            return;

        // After a drag the handler puts the item back under the slot but keeps it where it was
        // let go - over the quiz answer box, or wherever the drop missed. Normal grids hide that
        // by rebuilding their items; this view isn't rebuilt by them, so rebuild it here.
        RectTransform rect = (RectTransform)currentItemView.transform;
        bool inPlace = rect.parent == slotArea
                       && rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one
                       && rect.offsetMin == new Vector2(itemPadding, itemPadding)
                       && rect.offsetMax == new Vector2(-itemPadding, -itemPadding);

        if (!inPlace)
            RefreshView();
    }

    /// <summary>Snaps the bag shut with no animation.</summary>
    public void ResetClosed()
    {
        if (animating != null)
        {
            StopCoroutine(animating);
            animating = null;
        }

        state = State.Closed;
        SetOpenControlsVisible(false);
        ClearItemView();

        if (bagImage != null && openFrames.Length > 0)
            bagImage.sprite = openFrames[0];

        if (openButton != null)
            openButton.interactable = true;
    }

    public void Open()
    {
        if (state != State.Closed || !isActiveAndEnabled)
            return;

        PlaySFX(openAudio);
        ShuffleOrder();
        animating = StartCoroutine(PlayFrames(openFrames, State.Opening, () =>
        {
            state = State.Open;
            SetOpenControlsVisible(true);
            RefreshView();
        }));
    }

    /// <summary>
    /// Called when a storage is opened next to the bag. An open bag reshuffles, just like
    /// opening it does; a closed one is left alone since opening it will shuffle anyway.
    /// </summary>
    public void OnStorageOpened()
    {
        if (state != State.Open || !isActiveAndEnabled)
            return;

        InventoryItemDragHandler.ForceHideDescriptionPanel();
        ShuffleOrder();
        RefreshView();
    }

    public void Close()
    {
        if (state != State.Open || !isActiveAndEnabled)
            return;

        PlaySFX(closeAudio);
        SetOpenControlsVisible(false);
        ClearItemView();
        InventoryItemDragHandler.ForceHideDescriptionPanel();

        animating = StartCoroutine(PlayFrames(closeFrames, State.Closing, () =>
        {
            state = State.Closed;
            if (openButton != null)
                openButton.interactable = true;
        }));
    }

    private IEnumerator PlayFrames(Sprite[] frames, State playingState, System.Action onDone)
    {
        state = playingState;
        if (openButton != null)
            openButton.interactable = false;

        float frameTime = framesPerSecond > 0f ? 1f / framesPerSecond : 0f;

        foreach (Sprite frame in frames)
        {
            if (bagImage != null && frame != null)
                bagImage.sprite = frame;

            if (frameTime > 0f)
                yield return new WaitForSecondsRealtime(frameTime);
        }

        animating = null;
        onDone?.Invoke();
    }

    /// <summary>Pages = every packed item plus one empty slot at the end to drop new items into.</summary>
    private int PageCount => order.Count + 1;

    private void Step(int direction)
    {
        if (state != State.Open || PageCount < 2)
            return;

        currentIndex = (currentIndex + direction + PageCount) % PageCount;
        InventoryItemDragHandler.ForceHideDescriptionPanel();
        RefreshView();
    }

    /// <summary>Random order of everything currently in the bag (Fisher-Yates).</summary>
    private void ShuffleOrder()
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

        currentIndex = 0;
    }

    /// <summary>
    /// Keeps the current order when items come and go: removed items drop out, newly packed
    /// items are added at the end and shown straight away.
    /// </summary>
    private void OnInventoryChanged()
    {
        if (state != State.Open)
            return;

        List<InventoryItem> packed = GetPackedItems();
        InventoryItem shown = currentIndex >= 0 && currentIndex < order.Count ? order[currentIndex] : null;

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

        if (added != null)
            currentIndex = order.IndexOf(added);
        else if (shown != null && order.Contains(shown))
            currentIndex = order.IndexOf(shown);
        else
            currentIndex = Mathf.Clamp(currentIndex, 0, order.Count);   // order.Count = the empty page

        RefreshView();
    }

    private List<InventoryItem> GetPackedItems()
    {
        var items = new List<InventoryItem>();
        if (InventoryManager.Instance != null)
            items.AddRange(InventoryManager.Instance.GetSectionItems(sectionName));
        return items;
    }

    private void RefreshView()
    {
        ClearItemView();
        UpdateArrows();

        // The last page is the empty slot
        if (state != State.Open || slotArea == null || currentIndex >= order.Count)
            return;

        InventoryItem item = order[currentIndex];
        InventoryGrid grid = InventoryManager.Instance != null ? InventoryManager.Instance.GetGrid(sectionName) : null;
        if (item == null || grid == null)
            return;

        // The drag code identifies the item by its grid origin, so look that up
        int originX = 0, originY = 0;
        FindOrigin(grid, item, out originX, out originY);

        currentItemView = new GameObject(item.itemName, typeof(RectTransform));
        currentItemView.layer = gameObject.layer;
        RectTransform rect = (RectTransform)currentItemView.transform;
        rect.SetParent(slotArea, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(itemPadding, itemPadding);
        rect.offsetMax = new Vector2(-itemPadding, -itemPadding);

        Image image = currentItemView.AddComponent<Image>();
        image.sprite = item.itemSprite;
        image.preserveAspect = true;

        Button button = currentItemView.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None;

        currentItemView.AddComponent<InventoryGridItemUI>().Initialize(item, originX, originY, sectionName);
        currentItemView.AddComponent<InventoryItemDragHandler>();
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

    private void ClearItemView()
    {
        if (currentItemView != null)
            Destroy(currentItemView);
        currentItemView = null;
    }

    private void UpdateArrows()
    {
        // With nothing packed there is only the empty slot, so there is nowhere to step to
        bool canStep = state == State.Open && PageCount > 1;

        if (previousButton != null)
            previousButton.interactable = canStep;

        if (nextButton != null)
            nextButton.interactable = canStep;
    }

    private void SetOpenControlsVisible(bool visible)
    {
        if (slotArea != null)
            slotArea.gameObject.SetActive(visible);

        if (previousButton != null)
            previousButton.gameObject.SetActive(visible);

        if (nextButton != null)
            nextButton.gameObject.SetActive(visible);

        if (closeButton != null)
            closeButton.gameObject.SetActive(visible);
    }

    private static void PlaySFX(AudioClip clip)
    {
        if (clip != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(clip);
    }
}

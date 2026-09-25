using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;

/// <summary>
/// The single answer slot inside the quiz dialogue box.
/// Accepts one item dragged out of the inventory, hands it straight to
/// QuizManager for scoring, and tints itself green or red as feedback.
///
/// The drop itself never touches the inventory — the box shows a copy of the item's
/// sprite while InventoryItemDragHandler snaps the real one home. A correct answer is
/// then consumed out of its grid by <see cref="ConsumeItem"/>; a wrong one stays packed.
/// </summary>
public class QuizAnswerBox : MonoBehaviour, IDropHandler
{
    [SerializeField] private Image boxImage;

    [Header("Feedback Tint")]
    [SerializeField] private Color idleColor = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private Color correctColor = new Color(0.42f, 0.85f, 0.35f, 1f);
    [SerializeField] private Color wrongColor = new Color(0.90f, 0.30f, 0.28f, 1f);

    [Header("Drop Pop")]
    [Tooltip("How far the box scales up when an item lands, before settling back.")]
    [SerializeField] private float popScale = 1.18f;
    [SerializeField] private float popDuration = 0.16f;

    private QuizManager quizHandler;
    private RectTransform rectTransform;
    private InventoryItem currentItem = null;
    private GameObject displayedItemObject = null;

    // Grid the current item was dragged out of, so a correct answer can be removed from it
    private InventoryGridDisplay sourceGrid = null;

    // Which question this box is currently answering (0-5)
    private int answerBoxIndex = 0;

    // Set the instant an item lands, so the duplicate OnDrop Unity fires at the
    // end of the same drag is ignored.
    private bool isLocked = false;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();

        if (boxImage == null)
            boxImage = GetComponent<Image>();

        // Resolved in Awake because the handler calls PrepareForQuestion on this box
        // during the same frame the quiz panel is switched on.
        if (quizHandler == null)
            quizHandler = FindFirstObjectByType<QuizManager>(FindObjectsInactive.Include);

        ResetVisuals();
    }

    /// <summary>
    /// Re-arms the box for a new question.
    /// </summary>
    public void PrepareForQuestion(int questionIndex)
    {
        answerBoxIndex = questionIndex;
        ClearBox();
    }

    /// <summary>
    /// Empties the box and unlocks it for the next drop.
    /// </summary>
    public void ClearBox()
    {
        RemoveDisplayedItem();
        currentItem = null;
        sourceGrid = null;
        isLocked = false;
        ResetVisuals();
    }

    private void ResetVisuals()
    {
        if (boxImage != null)
            boxImage.color = idleColor;

        if (rectTransform != null)
            rectTransform.localScale = Vector3.one;
    }

    /// <summary>
    /// Called by Unity's event system when an item is dropped on this box.
    /// </summary>
    public void OnDrop(PointerEventData eventData)
    {
        if (isLocked)
            return;

        if (quizHandler != null && quizHandler.IsResolvingAnswer())
            return;

        GameObject draggedObject = eventData.pointerDrag;
        if (draggedObject == null)
            return;

        InventoryGridItemUI itemUI = draggedObject.GetComponent<InventoryGridItemUI>();
        if (itemUI == null)
            return;

        InventoryItem itemToPlace = itemUI.GetItem();
        if (itemToPlace == null)
            return;

        // Lock before anything that can rebuild the canvas — a rebuild while the
        // pointer is still over this box re-fires OnDrop for the same drag.
        isLocked = true;
        currentItem = itemToPlace;

        InventoryItemDragHandler gridDragHandler = draggedObject.GetComponent<InventoryItemDragHandler>();
        sourceGrid = gridDragHandler != null ? gridDragHandler.GetSourceGrid() : null;

        DisplayItemInAnswerBox(itemToPlace);

        if (quizHandler != null)
            quizHandler.OnItemPlaced(answerBoxIndex, itemToPlace);
    }

    /// <summary>
    /// Tints the box for the graded answer and pops it, so the result reads
    /// on the box itself as well as in the full-screen banner.
    /// </summary>
    public void ShowResult(bool isCorrect)
    {
        if (boxImage != null)
            boxImage.color = isCorrect ? correctColor : wrongColor;

        if (isActiveAndEnabled)
            StartCoroutine(PopRoutine());
    }

    private IEnumerator PopRoutine()
    {
        if (rectTransform == null)
            yield break;

        float half = Mathf.Max(0.01f, popDuration * 0.5f);

        for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
        {
            rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, popScale, t / half);
            yield return null;
        }

        for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
        {
            rectTransform.localScale = Vector3.one * Mathf.Lerp(popScale, 1f, t / half);
            yield return null;
        }

        rectTransform.localScale = Vector3.one;
    }

    /// <summary>
    /// Takes the current item out of the grid it came from — used up by answering correctly.
    /// A wrong answer never calls this, so the item stays packed and can be tried again.
    /// </summary>
    public void ConsumeItem()
    {
        if (currentItem == null || sourceGrid == null)
            return;

        InventoryGrid grid = sourceGrid.GetCurrentGrid();
        if (grid == null)
            return;

        for (int y = 0; y < grid.GetHeight(); y++)
        {
            for (int x = 0; x < grid.GetWidth(); x++)
            {
                if (grid.GetItemAt(x, y) != currentItem)
                    continue;

                // Only remove from the item's anchor slot — a multi-cell item occupies
                // several slots but RemoveItem expects its top-left origin.
                InventorySlot slot = grid.GetSlotAt(x, y);
                if (slot == null || slot.itemGridX != 0 || slot.itemGridY != 0)
                    continue;

                grid.RemoveItem(x, y);
                sourceGrid.RefreshDisplay();

                // Keeps the go-bag percentage / progress bar in step
                if (InventoryManager.Instance != null)
                    InventoryManager.Instance.InvokeInventoryChanged();

                return;
            }
        }
    }

    /// <summary>
    /// Draws a copy of the dropped item's sprite inside the box.
    /// </summary>
    private void DisplayItemInAnswerBox(InventoryItem item)
    {
        RemoveDisplayedItem();

        if (item == null)
            return;

        GameObject itemDisplay = new GameObject($"{item.itemName}_Display");
        itemDisplay.transform.SetParent(transform, false);

        RectTransform itemRect = itemDisplay.AddComponent<RectTransform>();
        itemRect.anchorMin = Vector2.zero;
        itemRect.anchorMax = Vector2.one;
        // Inset so the item sits inside the box border instead of covering it
        itemRect.offsetMin = new Vector2(6f, 6f);
        itemRect.offsetMax = new Vector2(-6f, -6f);

        Image itemImage = itemDisplay.AddComponent<Image>();
        itemImage.sprite = item.itemSprite;
        itemImage.type = Image.Type.Simple;
        itemImage.preserveAspect = true;
        itemImage.raycastTarget = false;

        displayedItemObject = itemDisplay;
    }

    private void RemoveDisplayedItem()
    {
        if (displayedItemObject != null)
        {
            Destroy(displayedItemObject);
            displayedItemObject = null;
        }
    }

    /// <summary>
    /// Get the item currently shown in this box.
    /// </summary>
    public InventoryItem GetItem()
    {
        return currentItem;
    }
}

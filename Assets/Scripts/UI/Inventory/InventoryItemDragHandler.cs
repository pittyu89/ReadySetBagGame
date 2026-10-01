using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Handles drag-and-drop functionality for inventory items between grids.
/// Attach this to item UI elements to make them draggable.
/// </summary>
public partial class InventoryItemDragHandler : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    // Static flag to prevent multiple items from being dragged at once
    private static bool isItemCurrentlyBeingDragged = false;
    public static bool IsAnyItemBeingDragged => isItemCurrentlyBeingDragged;

    // Public method to set drag state (used by both inventory and answer box drag handlers)
    public static void SetDragState(bool isDragging)
    {
        isItemCurrentlyBeingDragged = isDragging;
    }

    private InventoryGridItemUI itemUI;
    private InventoryGridDisplay sourceGrid;
    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private Vector2 dragOffset;
    private Image itemImage;
    private Color originalColor;
    private int originalSiblingIndex;
    private Transform originalParent;
    
    // For slot preview during drag
    private InventoryGridDisplay currentTargetGrid;
    private List<InventoryGridSlotUI> highlightedSlots = new List<InventoryGridSlotUI>();

    // For inventory panel reference
    private InventoryPanel inventoryPanelHandler;
    private static InventoryPanel sharedPanelHandler;

    // True only on the handler that actually started the current drag. OnBeginDrag can refuse
    // to start (another item is already being dragged), but Unity still delivers OnDrag and
    // OnEndDrag to this object - on a touch screen a second finger would otherwise drag a
    // second item and clear the shared drag flag out from under the first.
    private bool didBeginDrag;

    private void PlayItemPlacedSFX()
    {
        if (InventoryManager.Instance != null)
            SoundManager.Sfx(InventoryManager.Instance.itemPlacedInBagAudio);
    }

    void Start()
    {
        itemUI = GetComponent<InventoryGridItemUI>();
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        itemImage = GetComponent<Image>();

        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        if (itemImage != null)
            originalColor = itemImage.color;

        // Resolved once for the whole scene rather than once per item; see ResolveDescriptionPanel.
        ResolveDescriptionPanel();
        descriptionPanel = staticDescriptionPanel;
        descriptionPanelRect = sharedPanelRect;
        descriptionPanelCanvasGroup = sharedPanelCanvasGroup;
        itemNameText = sharedItemNameText;
        descriptionText = sharedDescriptionText;
        weightText = sharedWeightText;

        // Find the InventoryPanel to show the "Go Bag full" message. Shared for the same
        // reason as the panel: every spawned item was running its own scene-wide search.
        if (sharedPanelHandler == null)
            sharedPanelHandler = FindFirstObjectByType<InventoryPanel>();
        inventoryPanelHandler = sharedPanelHandler;
    }

    void Update()
    {
        // Nothing to poll unless this item's description is the one open. Checked first so the
        // other items in the bag do no work at all. descriptionPanel can legitimately be null
        // when the scene has no DescriptionPanel.
        if (panelOwner != this || descriptionPanel == null || !descriptionPanel.activeSelf)
            return;

        // While this item is being dragged the popup follows it, and OnEndDrag closes it
        if (didBeginDrag)
            return;

        // A tap anywhere other than this item closes its description. Taps on the item itself
        // are left to OnPointerClick, which toggles it. The old Input Manager also reports
        // touches as mouse button 0, so this covers phones too.
        if (Input.GetMouseButtonDown(0) && !IsScreenPointOverItem(Input.mousePosition))
            HideDescriptionPanel();
    }

    private bool IsScreenPointOverItem(Vector2 screenPoint)
    {
        if (rectTransform == null)
            return false;

        Canvas canvas = GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.rootCanvas.worldCamera
            : null;

        return RectTransformUtility.RectangleContainsScreenPoint(rectTransform, screenPoint, cam);
    }

    void OnDisable()
    {
        // An item consumed mid-drag (dropped on a quiz answer box, or cleared by a panel
        // refresh) is deactivated or destroyed without OnEndDrag ever firing. Without this the
        // shared flag stays true and NOTHING in the game can be dragged again until the scene
        // reloads, because every OnBeginDrag refuses.
        if (didBeginDrag)
        {
            didBeginDrag = false;
            isItemCurrentlyBeingDragged = false;
            ClearSlotHighlights();

            if (canvasGroup != null)
                canvasGroup.blocksRaycasts = true;
        }

        // If this item was the one the popup was describing, close it now. Going away without
        // doing this is what left the description on screen after letting go of an item:
        // OnEndDrag starts a 0.3s fade, then RefreshDisplay destroys this object and the
        // coroutine dies before it can deactivate the panel. ForceHide clears panelOwner.
        // Not moved home from here: this can run while the bag is being deactivated, when
        // Unity refuses any reparenting (closing the bag with a description open did that).
        if (panelOwner == this)
            ForceHideDescriptionPanel(restoreHome: false);
    }

    /// <summary>
    /// A tap on the item toggles its description. Unity does not send a click once a drag has
    /// started, so dragging an item never opens or closes the popup by accident.
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (itemUI == null || descriptionPanel == null || eventData.dragging)
            return;

        if (panelOwner == this && descriptionOpen)
        {
            HideDescriptionPanel();
            return;
        }

        ShowDescriptionPanel();
        UpdateDescriptionPanelPosition();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (itemUI == null)
            return;

        // Prevent dragging if another item is already being dragged
        if (isItemCurrentlyBeingDragged)
            return;

        // The popup sits over the item while its description is open; the item stays put until
        // it is closed (a tap on the item, or anywhere else)
        if (panelOwner == this && descriptionOpen)
            return;

        // The practice run only lets the item it is teaching be moved
        if (!OnboardingManager.DragAllowed(itemUI.GetItem()))
            return;

        // Mark that we're now dragging an item, and that THIS handler is the owner of that drag
        isItemCurrentlyBeingDragged = true;
        didBeginDrag = true;

        // Store the source grid display
        sourceGrid = GetComponentInParent<InventoryGridDisplay>();

        // Hide the original grid's occupied slots for this item
        if (sourceGrid != null && itemUI != null)
        {
            sourceGrid.RestoreSlotVisuals(itemUI.GetItem());
        }

        // Store original parent and sibling index BEFORE any reparenting
        originalParent = rectTransform.parent;
        originalSiblingIndex = rectTransform.GetSiblingIndex();

        // Find root canvas and reparent to it so item renders on top of everything
        Canvas rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas != null)
        {
            // Move to root canvas while maintaining world position
            rectTransform.SetParent(rootCanvas.transform, worldPositionStays: true);
            rectTransform.SetAsLastSibling();

            // Also reparent description panel to root canvas so it appears on top of the item
            if (descriptionPanelRect != null)
            {
                descriptionPanelRect.SetParent(rootCanvas.transform, worldPositionStays: true);
                descriptionPanelRect.SetAsLastSibling();
            }
        }
        else
        {
            // Fallback: just set as last sibling
            rectTransform.SetAsLastSibling();
        }

        // Position description panel after reparenting
        UpdateDescriptionPanelPosition();

        // Calculate drag offset AFTER reparenting
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform.parent as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out Vector2 localPoint
        );
        dragOffset = localPoint - rectTransform.anchoredPosition;

        // Block raycasts so it doesn't interfere with drop detection
        if (canvasGroup != null)
            canvasGroup.blocksRaycasts = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        // Only the handler that actually started the drag may move; see didBeginDrag.
        if (!didBeginDrag || rectTransform == null)
            return;

        // Update position while dragging
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform.parent as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out Vector2 localPoint))
        {
            rectTransform.anchoredPosition = localPoint - dragOffset;
        }

        // Keep description panel following the item
        UpdateDescriptionPanelPosition();

        // Update slot previews as we drag over grids
        UpdateSlotPreview(eventData);
    }

    private void UpdateSlotPreview(PointerEventData eventData)
    {
        // Clear previous highlights
        ClearSlotHighlights();

        // Find the grid under the pointer
        InventoryGridDisplay targetGrid = GetGridUnderPointer(eventData.position, eventData.pressEventCamera);
        
        if (targetGrid == null || itemUI == null)
            return;

        currentTargetGrid = targetGrid;
        InventoryGrid grid = targetGrid.GetCurrentGrid();
        
        if (grid == null)
            return;

        InventoryItem item = itemUI.GetItem();
        Vector2Int cell = GetCellUnderItem(targetGrid, grid, item);
        int gridX = cell.x;
        int gridY = cell.y;

        // Check if item actually fits at this position
        bool itemFits = (gridX + item.width <= grid.GetWidth()) && (gridY + item.height <= grid.GetHeight());

        // Only highlight slots if the item actually fits
        if (itemFits)
        {
            HighlightSlotsForItem(targetGrid, gridX, gridY, item);
        }
        else
        {
            // Item doesn't fit - restore grid to show actual contents only
            targetGrid.RestoreSlotVisuals();
        }
    }

    /// <summary>
    /// The grid cell the dragged item's top-left corner is nearest, clamped onto the grid.
    /// Items are laid out as posX = centerOffsetX + gridX * (cell + spacing) + itemWidth / 2
    /// (see InventoryGridDisplay), so this runs that backwards from where the item is now.
    /// </summary>
    private Vector2Int GetCellUnderItem(InventoryGridDisplay gridDisplay, InventoryGrid grid, InventoryItem item)
    {
        // The item may be parented to the root canvas mid-drag, so go through world space
        Vector3 itemWorldPos = rectTransform.parent.TransformPoint(rectTransform.localPosition);
        Vector3 itemLocalPos = gridDisplay.GetGridContainer().parent.InverseTransformPoint(itemWorldPos);

        float cellSize = gridDisplay.GetCellSize();
        float spacing = gridDisplay.GetSpacing();
        float cellWithSpacing = cellSize + spacing;

        float totalGridWidth = grid.GetWidth() * cellSize + (grid.GetWidth() - 1) * spacing;
        float totalGridHeight = grid.GetHeight() * cellSize + (grid.GetHeight() - 1) * spacing;
        float centerOffsetX = -(totalGridWidth / 2f);
        float centerOffsetY = totalGridHeight / 2f;

        float relativeX = itemLocalPos.x - centerOffsetX - cellSize * item.width / 2f;
        float relativeY = centerOffsetY - itemLocalPos.y - cellSize * item.height / 2f;

        int gridX = Mathf.Clamp(Mathf.RoundToInt(relativeX / cellWithSpacing), 0, grid.GetWidth() - 1);
        int gridY = Mathf.Clamp(Mathf.RoundToInt(relativeY / cellWithSpacing), 0, grid.GetHeight() - 1);
        return new Vector2Int(gridX, gridY);
    }

    private void HighlightSlotsForItem(InventoryGridDisplay targetGrid, int startX, int startY, InventoryItem item)
    {
        if (targetGrid == null || item == null)
            return;

        // Use the grid display's method to highlight the slots
        targetGrid.HighlightSlotsForDragPreview(startX, startY, item.width, item.height);
    }

    private void ClearSlotHighlights()
    {
        // Restore the original visuals of the target grid, excluding the dragged item
        if (currentTargetGrid != null && itemUI != null)
        {
            currentTargetGrid.RestoreSlotVisuals(itemUI.GetItem());
        }

        highlightedSlots.Clear();
        currentTargetGrid = null;
    }

    private InventoryGridDisplay GetGridUnderPointer(Vector2 screenPosition, Camera camera)
    {
        PointerEventData pointerEventData = new PointerEventData(EventSystem.current)
        {
            position = screenPosition
        };

        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerEventData, results);

        foreach (RaycastResult result in results)
        {
            // Skip the item being dragged itself
            if (result.gameObject == rectTransform.gameObject)
                continue;

            InventoryGridDisplay gridDisplay = result.gameObject.GetComponentInParent<InventoryGridDisplay>();
            if (gridDisplay != null)
            {
                return gridDisplay;
            }
        }

        return null;
    }

    public InventoryGridDisplay GetSourceGrid()
    {
        return sourceGrid;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        // A handler that never started a drag must not run the drop logic, and above all must
        // not clear isItemCurrentlyBeingDragged - that flag belongs to whoever is still dragging.
        if (!didBeginDrag)
            return;

        didBeginDrag = false;

        if (itemUI == null)
        {
            // Still our drag to release, even if the item went away underneath us. OnBeginDrag
            // turned raycast blocking off, so it has to go back on or this object stays
            // permanently transparent to clicks.
            if (canvasGroup != null)
                canvasGroup.blocksRaycasts = true;
            isItemCurrentlyBeingDragged = false;
            return;
        }

        // Hide description panel
        HideDescriptionPanel();

        // Clear slot highlights
        ClearSlotHighlights();

        // Restore source grid's original slots to show occupied again (if drag was cancelled)
        if (sourceGrid != null && itemUI != null)
        {
            sourceGrid.RestoreSlotVisuals();
        }

        // First, check if dropped on a quiz answer box or other IDropHandler
        // blocksRaycasts must be FALSE during this raycast — re-enabling it first
        // causes the item to block its own raycast and the answer box is never found.
        // Raycast from the ITEM's position, not the cursor position
        Vector3 itemScreenPos = RectTransformUtility.WorldToScreenPoint(eventData.pressEventCamera, rectTransform.position);
        
        PointerEventData pointerEventData = new PointerEventData(EventSystem.current)
        {
            position = itemScreenPos
        };

        var results = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerEventData, results);

        IDropHandler dropHandler = null;
        foreach (RaycastResult result in results)
        {
            if (result.gameObject == rectTransform.gameObject)
                continue; // Skip the dragged item itself

            dropHandler = result.gameObject.GetComponent<IDropHandler>();
            if (dropHandler != null)
            {
                break;
            }
        }

        // Raycast done — now safe to re-enable
        if (canvasGroup != null)
            canvasGroup.blocksRaycasts = true;

        // If dropped on a quiz answer box or other drop handler, let it handle the drop.
        // NOTE: Unity's event system already fired OnDrop automatically via IDropHandler —
        // do NOT call dropHandler.OnDrop(eventData) manually here, that would double-fire it.
        if (dropHandler != null && !(dropHandler is InventoryGridDisplay))
        {
            // Force hide the description panel immediately (not just animate). This also puts
            // it back under its authored parent and drops the ownership claim - the item is
            // about to be destroyed, so nothing else would clean it up.
            if (descriptionAnimationCoroutine != null)
            {
                StopCoroutine(descriptionAnimationCoroutine);
                descriptionAnimationCoroutine = null;
            }
            ForceHideDescriptionPanel();
            
            // Defer ALL layout operations to next frame — any canvas call here
            // while the pointer is still over the answer box re-fires OnDrop.
            StartCoroutine(CleanupAfterAnswerBoxDrop(originalParent, originalSiblingIndex, sourceGrid));
            isItemCurrentlyBeingDragged = false;
            return;
        }

        // Check if dropped on a valid target grid by finding nearest slot
        InventoryGridDisplay targetGrid = GetGridUnderPointer(eventData.position, eventData.pressEventCamera);

        if (targetGrid != null)
        {
            if (targetGrid == sourceGrid)
            {
                // Moving within same grid - snap to nearest slot. A failed move is not an
                // error: the item simply stays where it was and the restore below puts it back.
                TryMoveItemWithinGridSnapped(itemUI.GetItem(), sourceGrid);
            }
            else if (TryMoveItemBetweenGridsSnapped(itemUI.GetItem(), sourceGrid, targetGrid))
            {
                // Moving to a different grid - both displays need rebuilding
                sourceGrid?.RefreshDisplay();
                targetGrid.RefreshDisplay();
            }
        }

        // Restore parent and sibling index
        if (originalParent != null)
        {
            rectTransform.SetParent(originalParent, worldPositionStays: true);
            rectTransform.SetSiblingIndex(originalSiblingIndex);
        }

        // Force canvas rebuild again after reparenting
        Canvas.ForceUpdateCanvases();

        // Refresh source grid display to ensure visual matches data
        if (sourceGrid != null)
            sourceGrid.RefreshDisplay();

        // Clear the drag flag so other items can be dragged
        isItemCurrentlyBeingDragged = false;
    }

    private IEnumerator CleanupAfterAnswerBoxDrop(Transform parent, int siblingIndex, InventoryGridDisplay grid)
    {
        yield return null;
        if (rectTransform != null && parent != null)
        {
            rectTransform.SetParent(parent, worldPositionStays: true);
            rectTransform.SetSiblingIndex(siblingIndex);
        }
        Canvas.ForceUpdateCanvases();
        if (grid != null)
            grid.RefreshDisplay();
    }
}


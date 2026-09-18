using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Handles the visual display of a grid-based inventory section.
/// Displays items in their grid positions and manages UI updates.
/// </summary>
public class InventoryGridDisplay : MonoBehaviour
{
    [Tooltip("Go bag displays only: the InventoryManager section this display shows.")]
    [SerializeField] private string sectionName;
    [SerializeField] private float cellSize = 9f;
    [SerializeField] private float spacing = 1f;
    [Tooltip("On for furniture compartments, which are given their grid when the furniture is opened.")]
    [SerializeField] private bool useStorageGrid = false;

    private int gridWidth;
    private int gridHeight;

    [SerializeField] private RectTransform gridContainer;

    private InventoryManager inventoryManager;
    private InventoryGrid displayGrid;  // A go bag section, or the furniture compartment on show
    private InventoryGridSlotUI[,] slotUIs;
    private Dictionary<InventoryItem, InventoryGridItemUI> itemUIs;

    private void EnsureInitialized()
    {
        if (inventoryManager == null)
            inventoryManager = InventoryManager.Instance;
        if (itemUIs == null)
            itemUIs = new Dictionary<InventoryItem, InventoryGridItemUI>();
    }

    void Start()
    {
        EnsureInitialized();

        // Furniture compartments are built when their furniture is opened (and usually
        // before this runs, since the layout is created and filled in the same frame)
        if (useStorageGrid)
            return;

        InventoryGrid grid = inventoryManager != null ? inventoryManager.GetGrid(sectionName) : null;
        if (grid != null)
        {
            gridWidth = grid.GetWidth();
            gridHeight = grid.GetHeight();
            displayGrid = grid;
        }

        CreateGridSlots();
        RefreshDisplay();

        if (inventoryManager != null)
            inventoryManager.OnInventoryChanged += RefreshDisplay;
    }

    void OnDestroy()
    {
        if (inventoryManager != null && !useStorageGrid)
            inventoryManager.OnInventoryChanged -= RefreshDisplay;
    }

    /// <summary>
    /// Shows one furniture compartment. Called by the inventory panel for each compartment
    /// of the furniture being opened.
    /// </summary>
    public void ShowStorageCompartment(InventoryGrid grid)
    {
        EnsureInitialized();
        useStorageGrid = true;

        displayGrid = grid;
        if (grid == null)
            return;

        gridWidth = grid.gridWidth;
        gridHeight = grid.gridHeight;
        itemUIs.Clear();

        CreateGridSlots();
        RefreshDisplay();
    }

    /// <summary>
    /// Gets the section name this grid displays.
    /// </summary>
    public string GetSectionName()
    {
        return sectionName;
    }

    /// <summary>
    /// Gets the current grid being displayed (either from InventoryManager or storage model).
    /// </summary>
    public InventoryGrid GetCurrentGrid()
    {
        return displayGrid;
    }

    /// <summary>
    /// Gets the cell size for this grid.
    /// </summary>
    public float GetCellSize()
    {
        return cellSize;
    }

    /// <summary>
    /// Gets the spacing between cells.
    /// </summary>
    public float GetSpacing()
    {
        return spacing;
    }

    /// <summary>
    /// Gets the grid container RectTransform.
    /// </summary>
    public RectTransform GetGridContainer()
    {
        return gridContainer;
    }

    private void CreateGridSlots()
    {
        // Always clear existing children to avoid leftover duplicate slot/item objects,
        // then recreate the slot UI array and populate fresh slots.
        if (gridContainer != null)
        {
            foreach (Transform child in gridContainer)
            {
                Destroy(child.gameObject);
            }
        }

        slotUIs = new InventoryGridSlotUI[gridWidth, gridHeight];
        
        // Calculate total grid size with spacing
        float totalGridWidth = gridWidth * cellSize + (gridWidth - 1) * spacing;
        float totalGridHeight = gridHeight * cellSize + (gridHeight - 1) * spacing;
        
        // Calculate center offset for the entire grid
        float centerOffsetX = -(totalGridWidth / 2f);
        float centerOffsetY = totalGridHeight / 2f;

        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                GameObject slotObj = new GameObject($"Slot_{x}_{y}");
                slotObj.transform.SetParent(gridContainer, false);

                RectTransform slotRect = slotObj.AddComponent<RectTransform>();
                slotRect.sizeDelta = new Vector2(cellSize, cellSize);
                
                // Position slots to match item positioning calculations
                // Anchors and pivot centered to align with items
                slotRect.anchorMin = new Vector2(0.5f, 0.5f);
                slotRect.anchorMax = new Vector2(0.5f, 0.5f);
                slotRect.pivot = new Vector2(0.5f, 0.5f);
                
                float posX = centerOffsetX + x * (cellSize + spacing) + cellSize / 2f;
                float posY = centerOffsetY - (y * (cellSize + spacing) + cellSize / 2f);
                slotRect.anchoredPosition = new Vector2(posX, posY);

                Image slotImage = slotObj.AddComponent<Image>();
                slotImage.sprite = inventoryManager.GetEmptySlotSprite();
                slotImage.type = Image.Type.Simple;

                Button slotButton = slotObj.AddComponent<Button>();
                slotButton.targetGraphic = slotImage;

                InventoryGridSlotUI slotUI = slotObj.AddComponent<InventoryGridSlotUI>();
                slotUI.Initialize(x, y, sectionName);

                slotUIs[x, y] = slotUI;

                // Add interactivity
                int gridX = x;
                int gridY = y;
                slotButton.onClick.AddListener(() => OnSlotClicked(gridX, gridY));
            }
        }
    }

    public void RefreshDisplay()
    {
        InventoryGrid gridToDisplay = displayGrid;

        // If not using storage grid, get from InventoryManager
        if (!useStorageGrid && inventoryManager != null)
        {
            gridToDisplay = inventoryManager.GetGrid(sectionName);
            displayGrid = gridToDisplay;  // Update displayGrid reference
        }

        if (gridToDisplay == null)
            return;

        // Clear existing item UIs
        foreach (var itemUI in itemUIs.Values)
        {
            if (itemUI != null)
                Destroy(itemUI.gameObject);
        }
        itemUIs.Clear();

        // Reset slot sprites
        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (slotUIs[x, y] != null)
                {
                    Image slotImage = slotUIs[x, y].GetComponent<Image>();
                    if (slotImage != null)
                    {
                        slotImage.sprite = inventoryManager.GetEmptySlotSprite();
                        slotImage.color = Color.white;
                        slotUIs[x, y].UpdateOriginalColor();
                    }
                }
            }
        }

        // Ensure layout updates immediately for proper positioning
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.MarkLayoutForRebuild(gridContainer);
        Canvas.ForceUpdateCanvases();

        // Display items
        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                InventoryItem item = gridToDisplay.GetItemAt(x, y);
                if (item != null)
                {
                    InventorySlot slot = gridToDisplay.GetSlotAt(x, y);
                    
                    // Only create UI for the top-left corner of the item
                    if (slot.itemGridX == 0 && slot.itemGridY == 0)
                    {
                        CreateItemUI(item, x, y, gridToDisplay);
                    }

                    // Mark slot as occupied
                    if (slotUIs[x, y] != null)
                    {
                        Image slotImage = slotUIs[x, y].GetComponent<Image>();
                        if (slotImage != null)
                        {
                            slotImage.sprite = inventoryManager.GetOccupiedSlotSprite();
                            slotImage.color = Color.white;
                            slotUIs[x, y].UpdateOriginalColor();
                        }
                    }
                }
            }
        }
    }

    private void CreateItemUI(InventoryItem item, int gridX, int gridY, InventoryGrid grid)
    {
        // Prevent duplicates: if we've already created a UI for this item, skip
        if (itemUIs.ContainsKey(item))
            return;

        // Create unique name for this item UI using grid position
        string itemObjName = $"{item.itemName}_{gridX}_{gridY}";
        
        // If a leftover GameObject with the same name exists in the container, remove it
        Transform existing = gridContainer != null ? gridContainer.Find(itemObjName) : null;
        if (existing != null)
        {
            Destroy(existing.gameObject);
        }

        GameObject itemObj = new GameObject(itemObjName);
        itemObj.transform.SetParent(gridContainer, false);

        RectTransform rectTransform = itemObj.AddComponent<RectTransform>();

        // Size
        float itemWidth = cellSize * item.width;
        float itemHeight = cellSize * item.height;

        // Set item anchors to center
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);

        rectTransform.sizeDelta = new Vector2(itemWidth, itemHeight);

        // Calculate center offset for the entire grid
        float totalGridWidth = gridWidth * cellSize + (gridWidth - 1) * spacing;
        float totalGridHeight = gridHeight * cellSize + (gridHeight - 1) * spacing;
        
        float centerOffsetX = -(totalGridWidth / 2f);
        float centerOffsetY = totalGridHeight / 2f;

        // Position item aligned to grid cells, centered
        // Top-left corner of item should align with top-left corner of its grid cell
        // Then offset by half the item size due to pivot being at center
        float posX = centerOffsetX + gridX * (cellSize + spacing) + itemWidth / 2f;
        float posY = centerOffsetY - (gridY * (cellSize + spacing) + itemHeight / 2f);
        
        rectTransform.anchoredPosition = new Vector2(posX, posY);



        // Display the item sprite
        Image itemImage = itemObj.AddComponent<Image>();
        itemImage.sprite = item.itemSprite;
        itemImage.type = Image.Type.Simple;
        itemImage.preserveAspect = true;

        // Add button for interaction
        Button itemButton = itemObj.AddComponent<Button>();
        itemButton.targetGraphic = itemImage;

        InventoryGridItemUI itemUI = itemObj.AddComponent<InventoryGridItemUI>();
        itemUI.Initialize(item, gridX, gridY, sectionName);

        // Add drag handler for this item
        InventoryItemDragHandler dragHandler = itemObj.AddComponent<InventoryItemDragHandler>();

        itemUIs[item] = itemUI;

        // Add click listener
        itemButton.onClick.AddListener(() => OnItemClicked(item, gridX, gridY));

        // Add text for quantity if stackable
        if (item.isStackable && item.quantity > 1)
        {
            GameObject quantityObj = new GameObject("Quantity");
            quantityObj.transform.SetParent(itemObj.transform, false);

            RectTransform quantityRect = quantityObj.AddComponent<RectTransform>();
            quantityRect.anchorMin = Vector2.one;
            quantityRect.anchorMax = Vector2.one;
            quantityRect.pivot = Vector2.one;
            quantityRect.offsetMin = Vector2.zero;
            quantityRect.offsetMax = new Vector2(-5, -5);

            Text quantityText = quantityObj.AddComponent<Text>();
            quantityText.text = item.quantity.ToString();
            quantityText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            quantityText.fontSize = 14;
            quantityText.fontStyle = FontStyle.Bold;
            quantityText.alignment = TextAnchor.LowerRight;
            quantityText.color = Color.white;
        }
    }

    private void OnSlotClicked(int gridX, int gridY)
    {
        // Slot interaction can be handled here if needed
    }

    private void OnItemClicked(InventoryItem item, int gridX, int gridY)
    {
        // Item interaction can be handled here if needed
    }

    /// <summary>
    /// Updates the display when inventory changes.
    /// </summary>
    public void OnInventoryUpdated()
    {
        RefreshDisplay();
    }

    /// <summary>
    /// Highlights slots for a preview during drag operations.
    /// </summary>
    public void HighlightSlotsForDragPreview(int startX, int startY, int width, int height)
    {
        if (slotUIs == null)
            return;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int slotX = startX + x;
                int slotY = startY + y;

                if (slotX >= 0 && slotX < gridWidth && slotY >= 0 && slotY < gridHeight)
                {
                    if (slotUIs[slotX, slotY] != null)
                    {
                        Image slotImage = slotUIs[slotX, slotY].GetComponent<Image>();
                        if (slotImage != null)
                        {
                            slotImage.sprite = inventoryManager.GetOccupiedSlotSprite();
                            slotImage.color = Color.white;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets the slot UI at a specific grid position.
    /// </summary>
    public InventoryGridSlotUI GetSlotUIAt(int x, int y)
    {
        if (slotUIs != null && x >= 0 && x < gridWidth && y >= 0 && y < gridHeight)
        {
            return slotUIs[x, y];
        }
        return null;
    }

    /// <summary>
    /// Restores slot visuals to their proper state based on actual grid contents.
    /// Optionally excludes an item from the restoration (useful during drag operations).
    /// </summary>
    public void RestoreSlotVisuals(InventoryItem excludeItem = null)
    {
        if (slotUIs == null || displayGrid == null)
            return;

        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (slotUIs[x, y] != null)
                {
                    Image slotImage = slotUIs[x, y].GetComponent<Image>();
                    if (slotImage != null)
                    {
                        InventoryItem item = displayGrid.GetItemAt(x, y);
                        
                        // If this item should be excluded (being dragged), show as empty
                        if (item == excludeItem)
                        {
                            slotImage.sprite = inventoryManager.GetEmptySlotSprite();
                        }
                        else if (item != null)
                        {
                            slotImage.sprite = inventoryManager.GetOccupiedSlotSprite();
                        }
                        else
                        {
                            slotImage.sprite = inventoryManager.GetEmptySlotSprite();
                        }
                        slotImage.color = Color.white;
                        slotUIs[x, y].UpdateOriginalColor();
                    }
                }
            }
        }
    }
}

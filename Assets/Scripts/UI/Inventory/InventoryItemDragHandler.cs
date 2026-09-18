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
public class InventoryItemDragHandler : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    // Static flag to prevent multiple items from being dragged at once
    private static bool isItemCurrentlyBeingDragged = false;
    public static bool IsAnyItemBeingDragged => isItemCurrentlyBeingDragged;

    // Public method to set drag state (used by both inventory and answer box drag handlers)
    public static void SetDragState(bool isDragging)
    {
        isItemCurrentlyBeingDragged = isDragging;
    }

    // The description panel is one shared object, so it is resolved once per scene and reused
    // by every item rather than once per item. The lookup is expensive: the panel starts
    // inactive, GameObject.Find only returns ACTIVE objects, so the search below always falls
    // through to a scan of every loaded object - including assets. Paying that per spawned item
    // meant one full scan per item in the bag.
    private static GameObject staticDescriptionPanel;
    private static RectTransform sharedPanelRect;
    private static CanvasGroup sharedPanelCanvasGroup;
    private static TextMeshProUGUI sharedItemNameText;
    private static TextMeshProUGUI sharedDescriptionText;
    private static TextMeshProUGUI sharedWeightText;
    // The item shown standing in the display case at the top of the popup
    private static Image sharedItemImage;

    // Root canvas the panel lives under, used to keep the popup on screen.
    private static RectTransform sharedCanvasRect;

    // Where the panel lives when it is not being dragged over. OnBeginDrag moves it to the root
    // canvas so it draws above the dragged item, and it has to be put back afterwards: parked
    // under the always-active root canvas it outlives the InventoryPanel that owns it, so
    // closing the bag no longer takes the popup with it and it stays on screen.
    private static Transform panelHomeParent;
    private static int panelHomeSiblingIndex;

    // Which handler the panel is currently showing information for. The panel is shared, but the
    // hide ANIMATION runs as a coroutine on the individual item - and RefreshDisplay destroys
    // every item object right after a drop. That kills the coroutine before it reaches its final
    // SetActive(false), leaving the popup stuck on screen at full alpha. Knowing the owner lets
    // OnDisable close the panel outright instead of relying on that coroutine finishing.
    private static InventoryItemDragHandler panelOwner;

    // Scene the cache above belongs to. Statics outlive scene loads, so the handle is what
    // tells us the cached panel is from a scene that no longer exists.
    private static int panelResolvedForScene = -1;

    /// <summary>
    /// Finds and wires up the shared description panel, at most once per scene.
    /// </summary>
    private static void ResolveDescriptionPanel()
    {
        int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        if (panelResolvedForScene == scene)
            return;

        panelResolvedForScene = scene;
        staticDescriptionPanel = GameObject.Find("DescriptionPanel");

        if (staticDescriptionPanel == null)
        {
            // Panel is inactive, so search everything loaded, including disabled objects.
            foreach (GameObject obj in Resources.FindObjectsOfTypeAll(typeof(GameObject)) as GameObject[])
            {
                if (obj.name == "DescriptionPanel" && obj.scene.IsValid())
                {
                    staticDescriptionPanel = obj;
                    break;
                }
            }
        }

        sharedPanelRect = null;
        sharedPanelCanvasGroup = null;
        sharedItemNameText = null;
        sharedDescriptionText = null;
        sharedWeightText = null;
        sharedItemImage = null;
        sharedCanvasRect = null;

        if (staticDescriptionPanel == null)
            return;

        // Remember where the panel belongs before any drag moves it to the root canvas.
        panelHomeParent = staticDescriptionPanel.transform.parent;
        panelHomeSiblingIndex = staticDescriptionPanel.transform.GetSiblingIndex();

        sharedPanelRect = staticDescriptionPanel.GetComponent<RectTransform>();
        sharedPanelCanvasGroup = staticDescriptionPanel.GetComponent<CanvasGroup>();
        if (sharedPanelCanvasGroup == null)
            sharedPanelCanvasGroup = staticDescriptionPanel.AddComponent<CanvasGroup>();

        TextMeshProUGUI[] allTexts =
            staticDescriptionPanel.GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true);

        // Bind by object name, not by position in the array. The panel's depth-first order is
        // DescriptionText, WeightText, ItemNameText - so the old index-based binding (0=desc,
        // 1=name, 2=weight) wrote the item's NAME into the weight box and its WEIGHT into the
        // name box. Matching on name also means re-ordering the hierarchy can't silently swap
        // the fields again.
        foreach (var t in allTexts)
        {
            string n = t.gameObject.name.ToLowerInvariant();
            // Fixed captions such as the "Weight" label next to the value
            if (n.Contains("label")) continue;
            if (n.Contains("name")) sharedItemNameText = t;
            else if (n.Contains("weight")) sharedWeightText = t;
            else if (n.Contains("desc")) sharedDescriptionText = t;
        }

        // Fall back to the original positional guess only for slots the names didn't resolve,
        // so a panel with differently-named children still shows something.
        if (sharedDescriptionText == null && allTexts.Length > 0) sharedDescriptionText = allTexts[0];
        if (sharedItemNameText == null && allTexts.Length > 2) sharedItemNameText = allTexts[2];
        if (sharedWeightText == null && allTexts.Length > 1) sharedWeightText = allTexts[1];

        Transform itemImage = staticDescriptionPanel.transform.Find("ItemImage");
        sharedItemImage = itemImage != null ? itemImage.GetComponent<Image>() : null;

        staticDescriptionPanel.SetActive(false);
    }

    /// <summary>
    /// Puts the panel back under the object it was authored on, undoing the reparent that
    /// OnBeginDrag does to raise it above the dragged item.
    /// </summary>
    private static void RestorePanelHome()
    {
        if (staticDescriptionPanel == null || panelHomeParent == null)
            return;
        if (staticDescriptionPanel.transform.parent == panelHomeParent)
            return;

        staticDescriptionPanel.transform.SetParent(panelHomeParent, worldPositionStays: false);
        staticDescriptionPanel.transform.SetSiblingIndex(panelHomeSiblingIndex);
    }

    // Public static method to force hide the description panel
    public static void ForceHideDescriptionPanel()
    {
        // Closing outright ends any ownership claim, whoever held it.
        panelOwner = null;

        ResolveDescriptionPanel();
        RestorePanelHome();

        // Deactivate and reset the panel immediately
        if (staticDescriptionPanel != null)
        {
            staticDescriptionPanel.SetActive(false);
            RectTransform panelRect = staticDescriptionPanel.GetComponent<RectTransform>();
            if (panelRect != null)
                panelRect.localScale = Vector3.zero;
            CanvasGroup panelCanvasGroup = staticDescriptionPanel.GetComponent<CanvasGroup>();
            if (panelCanvasGroup != null)
                panelCanvasGroup.alpha = 0f;
        }
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

    // For description panel
    private GameObject descriptionPanel;
    private TextMeshProUGUI itemNameText;
    private TextMeshProUGUI descriptionText;
    private TextMeshProUGUI weightText;
    private RectTransform descriptionPanelRect;
    private CanvasGroup descriptionPanelCanvasGroup;
    private Coroutine descriptionAnimationCoroutine;
    
    [Header("Description Popup")]
    [Tooltip("Gap in UI units between the item and the popup while the item is being dragged.")]
    [SerializeField] private float panelGap = 2f;

    // True between this item opening its description and asking for it to close, so a second
    // tap closes it even while the pop-in is still animating.
    private bool descriptionOpen = false;

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
        if (InventoryManager.Instance != null && InventoryManager.Instance.itemPlacedInBagAudio != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(InventoryManager.Instance.itemPlacedInBagAudio);
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
            sharedPanelHandler = FindObjectOfType<InventoryPanel>();
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
        if (panelOwner == this)
            ForceHideDescriptionPanel();
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

    private void ShowDescriptionPanel()
    {
        if (descriptionPanel == null || itemUI == null)
            return;

        InventoryItem item = itemUI.GetItem();
        if (item == null)
            return;

        // Update item name text
        if (itemNameText != null)
            itemNameText.text = item.itemName;

        // Update description text
        if (descriptionText != null)
        {
            descriptionText.text = string.IsNullOrWhiteSpace(item.description)
                ? "No description available."
                : item.description;
        }

        // Update weight text. This popup describes the item itself, not the stack, so it is
        // always the single-item weight. The format string is what stops raw float
        // interpolation printing values like "0.30000001kg".
        if (weightText != null)
            weightText.text = $"{item.weightKg:0.##} Kg";

        if (sharedItemImage != null)
        {
            sharedItemImage.sprite = item.itemSprite;
            sharedItemImage.enabled = item.itemSprite != null;
        }

        // Tapping one item while another's description is still fading out: that fade would
        // switch the shared panel off underneath this one when it finished, so stop it first.
        if (panelOwner != null && panelOwner != this)
            panelOwner.StopPanelAnimation();

        // This handler now owns the shared panel; see panelOwner.
        panelOwner = this;
        descriptionOpen = true;

        // Stop any existing animation and start the popup animation
        if (descriptionAnimationCoroutine != null)
            StopCoroutine(descriptionAnimationCoroutine);

        descriptionAnimationCoroutine = StartCoroutine(PopupPanelAnimation(show: true));
    }

    private void UpdateDescriptionPanelPosition()
    {
        if (descriptionPanel == null || descriptionPanelRect == null || rectTransform == null)
            return;

        RectTransform canvasRect = ResolveCanvasRect();
        Vector2 extents = GetPanelWorldExtents();

        // A tapped item gets the popup centred on it. While dragging, it sits just above the
        // item instead, so it doesn't hide what is being dragged.
        Vector3 target = rectTransform.TransformPoint(rectTransform.rect.center);
        if (didBeginDrag)
        {
            float gap = panelGap * Mathf.Abs(rectTransform.lossyScale.y);
            GetDrawnItemWorldY(out float itemTop, out _);
            target.y = itemTop + extents.y * 0.5f + gap;
        }

        descriptionPanelRect.position = target;

        if (canvasRect == null)
            return;

        Vector3[] canvasCorners = new Vector3[4];
        canvasRect.GetWorldCorners(canvasCorners);
        float canvasTop = canvasCorners[2].y;
        float canvasBottom = canvasCorners[0].y;
        float canvasLeft = canvasCorners[0].x;
        float canvasRight = canvasCorners[2].x;

        // Keep the whole panel inside the canvas on both axes.
        float halfW = extents.x * 0.5f;
        float halfH = extents.y * 0.5f;
        target.x = Mathf.Clamp(target.x, canvasLeft + halfW, canvasRight - halfW);
        target.y = Mathf.Clamp(target.y, canvasBottom + halfH, canvasTop - halfH);

        descriptionPanelRect.position = target;
    }

    /// <summary>
    /// World-space top and bottom of the item's picture as drawn. Items keep their proportions
    /// inside their grid box, so a wide item leaves empty bands above and below it - measuring
    /// the box put the popup visibly far from the picture. Falls back to the box itself when
    /// there is no sprite to measure.
    /// </summary>
    private void GetDrawnItemWorldY(out float top, out float bottom)
    {
        Rect box = rectTransform.rect;
        float drawnHeight = box.height;

        if (itemImage != null && itemImage.sprite != null && itemImage.preserveAspect)
        {
            Vector2 spriteSize = itemImage.sprite.rect.size;
            if (spriteSize.x > 0f && spriteSize.y > 0f)
            {
                // Fit inside the box: whichever side runs out first sets the scale
                float fit = Mathf.Min(box.width / spriteSize.x, box.height / spriteSize.y);
                drawnHeight = spriteSize.y * fit;
            }
        }

        // The picture is centred in the box
        float centreY = box.center.y;
        top = rectTransform.TransformPoint(new Vector3(0f, centreY + drawnHeight * 0.5f, 0f)).y;
        bottom = rectTransform.TransformPoint(new Vector3(0f, centreY - drawnHeight * 0.5f, 0f)).y;
    }

    /// <summary>
    /// World-space size of the panel's visible content. The panel's own rect is only an anchor -
    /// its children are authored much larger and scaled down - so the bounds are measured from
    /// the child graphics that actually draw.
    /// </summary>
    private Vector2 GetPanelWorldExtents()
    {
        if (descriptionPanelRect == null)
            return Vector2.zero;

        // Measure the popup at its FINAL size. The show animation scales it 0 -> 1 over 0.3s,
        // and this runs on the first frame of that animation, so measuring the live transform
        // returned an almost-zero panel and parked it overlapping the item - then it appeared
        // to jump upwards once the animation finished and dragging re-positioned it.
        // Transform changes apply immediately and these children have fixed rects, so no canvas
        // rebuild is needed for the corners to be correct.
        Vector3 savedScale = descriptionPanelRect.localScale;
        descriptionPanelRect.localScale = Vector3.one;

        bool any = false;
        float minX = 0f, maxX = 0f, minY = 0f, maxY = 0f;
        Vector3[] corners = new Vector3[4];

        foreach (var child in descriptionPanelRect.GetComponentsInChildren<RectTransform>(true))
        {
            if (child == descriptionPanelRect) continue;
            if (child.GetComponent<UnityEngine.UI.Graphic>() == null) continue;

            child.GetWorldCorners(corners);
            for (int i = 0; i < 4; i++)
            {
                if (!any)
                {
                    minX = maxX = corners[i].x;
                    minY = maxY = corners[i].y;
                    any = true;
                    continue;
                }
                if (corners[i].x < minX) minX = corners[i].x;
                if (corners[i].x > maxX) maxX = corners[i].x;
                if (corners[i].y < minY) minY = corners[i].y;
                if (corners[i].y > maxY) maxY = corners[i].y;
            }
        }

        if (!any)
        {
            descriptionPanelRect.GetWorldCorners(corners);
            descriptionPanelRect.localScale = savedScale;
            return new Vector2(corners[2].x - corners[0].x, corners[2].y - corners[0].y);
        }

        descriptionPanelRect.localScale = savedScale;
        return new Vector2(maxX - minX, maxY - minY);
    }

    private RectTransform ResolveCanvasRect()
    {
        if (sharedCanvasRect != null)
            return sharedCanvasRect;

        Canvas c = descriptionPanelRect != null
            ? descriptionPanelRect.GetComponentInParent<Canvas>()
            : GetComponentInParent<Canvas>();

        if (c != null)
            sharedCanvasRect = c.rootCanvas.GetComponent<RectTransform>();

        return sharedCanvasRect;
    }

    private void StopPanelAnimation()
    {
        descriptionOpen = false;

        if (descriptionAnimationCoroutine != null)
        {
            StopCoroutine(descriptionAnimationCoroutine);
            descriptionAnimationCoroutine = null;
        }
    }

    private void HideDescriptionPanel()
    {
        descriptionOpen = false;

        if (descriptionPanel == null)
            return;

        // Ownership is deliberately NOT released here. The fade below can be cut short - the
        // item is destroyed by RefreshDisplay immediately after a drop - and OnDisable can only
        // clean up the panel if it still recognises this handler as the owner. Ownership is
        // released when the fade actually finishes, or by ForceHideDescriptionPanel.

        // Stop any existing animation and start the close animation
        if (descriptionAnimationCoroutine != null)
            StopCoroutine(descriptionAnimationCoroutine);

        // A disabled or inactive object cannot run a coroutine, so the fade would never start
        // and the panel would stay up. Close it outright in that case.
        if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
        {
            descriptionAnimationCoroutine = null;
            ForceHideDescriptionPanel();
            return;
        }

        descriptionAnimationCoroutine = StartCoroutine(PopupPanelAnimation(show: false));
    }

    private IEnumerator PopupPanelAnimation(bool show)
    {
        if (descriptionPanel == null || descriptionPanelCanvasGroup == null || descriptionPanelRect == null)
            yield break;

        float duration = 0.3f;  // Animation duration in seconds
        float elapsed = 0f;

        if (show)
        {
            // Activate the panel
            descriptionPanel.SetActive(true);
            
            // Start with scale 0 and alpha 0
            descriptionPanelRect.localScale = Vector3.zero;
            descriptionPanelCanvasGroup.alpha = 0f;

            // Animate to scale 1 and alpha 1
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float progress = elapsed / duration;
                
                // Ease out cubic for smooth popup
                float easeProgress = 1f - Mathf.Pow(1f - progress, 3f);
                
                descriptionPanelRect.localScale = Vector3.one * easeProgress;
                descriptionPanelCanvasGroup.alpha = easeProgress;
                
                yield return null;
            }

            // Ensure final values
            descriptionPanelRect.localScale = Vector3.one;
            descriptionPanelCanvasGroup.alpha = 1f;
        }
        else
        {
            // Start with current scale and alpha
            descriptionPanelRect.localScale = Vector3.one;
            descriptionPanelCanvasGroup.alpha = 1f;

            // Animate to scale 0 and alpha 0
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float progress = 1f - (elapsed / duration);
                
                // Ease in cubic for smooth close
                float easeProgress = 1f - Mathf.Pow(1f - progress, 3f);
                
                descriptionPanelRect.localScale = Vector3.one * easeProgress;
                descriptionPanelCanvasGroup.alpha = easeProgress;
                
                yield return null;
            }

            // Deactivate the panel
            descriptionPanel.SetActive(false);
            descriptionPanelRect.localScale = Vector3.zero;
            descriptionPanelCanvasGroup.alpha = 0f;

            // The fade ran to completion, so the panel is genuinely closed and this handler no
            // longer owns it. Put it back under its authored parent too, otherwise it stays
            // under the root canvas and outlives the InventoryPanel.
            RestorePanelHome();

            if (panelOwner == this)
                panelOwner = null;
        }
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

    private bool TryMoveItemWithinGridSnapped(InventoryItem item, InventoryGridDisplay gridDisplay)
    {
        InventoryGrid grid = gridDisplay != null ? gridDisplay.GetCurrentGrid() : null;
        if (item == null || grid == null || !TryFindItemOrigin(grid, item, out Vector2Int old))
            return false;

        // Lift the item out first so CanPlaceItem doesn't see it occupying its own cells
        grid.RemoveItem(old.x, old.y);

        if (!TryFindPlacement(grid, item, GetCellUnderItem(gridDisplay, grid, item), out Vector2Int target)
            || !CanPlaceItemWithGlobalWeightCheck(item, gridDisplay))
        {
            grid.PlaceItem(old.x, old.y, item);
            return false;
        }

        grid.PlaceItem(target.x, target.y, item);
        OnItemPlaced();
        return true;
    }

    private bool TryMoveItemBetweenGridsSnapped(InventoryItem item, InventoryGridDisplay sourceGridDisplay, InventoryGridDisplay targetGridDisplay)
    {
        InventoryGrid sourceGrid = sourceGridDisplay != null ? sourceGridDisplay.GetCurrentGrid() : null;
        InventoryGrid targetGrid = targetGridDisplay != null ? targetGridDisplay.GetCurrentGrid() : null;
        if (item == null || sourceGrid == null || targetGrid == null || !TryFindItemOrigin(sourceGrid, item, out Vector2Int old))
            return false;

        // Lift the item out of the source first so the target grid's checks aren't confused by it
        sourceGrid.RemoveItem(old.x, old.y);

        if (!TryFindPlacement(targetGrid, item, GetCellUnderItem(targetGridDisplay, targetGrid, item), out Vector2Int target)
            || !CanPlaceItemWithGlobalWeightCheck(item, targetGridDisplay))
        {
            sourceGrid.PlaceItem(old.x, old.y, item);
            return false;
        }

        targetGrid.PlaceItem(target.x, target.y, item);
        OnItemPlaced();
        return true;
    }

    /// <summary>The cell holding the item's top-left corner, where the grid stores it.</summary>
    private static bool TryFindItemOrigin(InventoryGrid grid, InventoryItem item, out Vector2Int origin)
    {
        for (int y = 0; y < grid.GetHeight(); y++)
        {
            for (int x = 0; x < grid.GetWidth(); x++)
            {
                if (grid.GetItemAt(x, y) != item)
                    continue;

                InventorySlot slot = grid.GetSlotAt(x, y);
                if (slot != null && slot.itemGridX == 0 && slot.itemGridY == 0)
                {
                    origin = new Vector2Int(x, y);
                    return true;
                }
            }
        }

        origin = default;
        return false;
    }

    /// <summary>
    /// Where the item goes: the cell it was dropped on if it fits there, otherwise the nearest
    /// cell that fits. False when it fits nowhere in the grid.
    /// </summary>
    private static bool TryFindPlacement(InventoryGrid grid, InventoryItem item, Vector2Int dropped, out Vector2Int placement)
    {
        placement = dropped;
        if (grid.CanPlaceItem(dropped.x, dropped.y, item))
            return true;

        float nearestDistance = float.MaxValue;
        bool found = false;

        for (int y = 0; y < grid.GetHeight(); y++)
        {
            for (int x = 0; x < grid.GetWidth(); x++)
            {
                if (!grid.CanPlaceItem(x, y, item))
                    continue;

                float distance = Vector2.Distance(dropped, new Vector2(x, y));
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    placement = new Vector2Int(x, y);
                    found = true;
                }
            }
        }

        return found;
    }

    private void OnItemPlaced()
    {
        PlayItemPlacedSFX();

        // Progress bars and the like listen for this
        if (InventoryManager.Instance != null)
            InventoryManager.Instance.InvokeInventoryChanged();
    }

    /// <summary>
    /// Checks if an item can be placed in a grid, considering global GoBag weight limits.
    /// </summary>
    private bool CanPlaceItemWithGlobalWeightCheck(InventoryItem item, InventoryGridDisplay gridDisplay)
    {
        if (item == null || gridDisplay == null)
            return false;

        string sectionName = gridDisplay.GetSectionName();

        // Check if this is a GoBag section (not a storage compartment)
        if (sectionName.Contains("Refrigerator") || sectionName.Contains("Storage"))
        {
            // Storage sections don't have global weight limits
            return true;
        }

        // This is a GoBag section - check global weight limit
        InventoryManager manager = InventoryManager.Instance;
        if (manager == null)
            return true;

        bool canAdd = manager.CanAddItemToGoBag(item);
        if (!canAdd)
        {
            // Determine if it's a weight issue or go bag at capacity issue
            float currentWeight = manager.GetGoBagTotalWeight();
            float weightLimit = manager.GetGoBagWeightLimit();
            float remainingWeight = weightLimit - currentWeight;

            // If remaining weight is 0 or less, the bag is full
            if (remainingWeight <= 0)
            {
                if (inventoryPanelHandler != null)
                    inventoryPanelHandler.ShowGoBagFullMessage();
            }
            // If the item itself exceeds remaining weight, it's a weight limit issue
            else if (item.weightKg > remainingWeight)
            {
                if (inventoryPanelHandler != null)
                    inventoryPanelHandler.ShowWeightLimitMessage();
            }
        }
        return canAdd;
    }
}


using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The item description popup: the one shared panel every item opens, how it is found, shown,
/// kept beside the item and on screen, and animated away.
/// </summary>
public partial class InventoryItemDragHandler
{
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

    /// <summary>True while an item's description popup is up. The onboarding waits on it.</summary>
    public static bool IsDescriptionOpen => panelOwner != null && panelOwner.descriptionOpen;

    /// <summary>The description popup while it is open, else null. The onboarding keeps its cards off it.</summary>
    public static RectTransform OpenDescriptionPanel => IsDescriptionOpen ? sharedPanelRect : null;

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

    /// <summary>
    /// Closes the description panel outright. <paramref name="restoreHome"/> false leaves it
    /// where it is: an item's OnDisable can run while Unity is deactivating the bag around it,
    /// and moving a transform in the middle of that is refused with an error. The panel is
    /// hidden either way, and the next close that can move it - CloseInventory's own, or the
    /// next hide - puts it back.
    /// </summary>
    public static void ForceHideDescriptionPanel(bool restoreHome = true)
    {
        // Closing outright ends any ownership claim, whoever held it.
        panelOwner = null;

        ResolveDescriptionPanel();
        if (restoreHome)
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
            weightText.text = $"{item.weightKg:0.##}kg";

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
}

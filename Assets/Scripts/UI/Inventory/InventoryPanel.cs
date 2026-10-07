using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class InventoryPanel : MonoBehaviour
{
    [Header("Main Inventory Panel")]
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button closeButtonBack;
    [SerializeField] private Button bagButton;
    [SerializeField] private GameObject bagButtonGroup;
    [SerializeField] private Image modelDisplayImage;

    // The compartments of the furniture currently open, laid over modelDisplayImage
    private StorageLayout openLayout;
    [SerializeField] private RectTransform goBagSide;
    [SerializeField] private RectTransform storageSide;
    [SerializeField] private GameObject quizPanel;

    [Header("Pouch Navigation")]
    [Tooltip("The arrows above the bag that zoom and pan between its pouches.")]
    [SerializeField] private BagPouchNavigator pouchNavigator;

    [Header("Bag Type")]
    [Tooltip("The standard (orange) bag, shown unless the Small or Medium Bag was picked.")]
    [SerializeField] private StandardBagController standardBag;
    [Tooltip("Shown instead of the standard (orange) bag when the Small Bag was picked.")]
    [SerializeField] private SmallBagController smallBag;
    [Tooltip("HUD bag button icon used with the Small Bag.")]
    [SerializeField] private Sprite smallBagButtonIcon;
    [Tooltip("Shown instead of the standard (orange) bag when the Medium Bag was picked.")]
    [SerializeField] private MediumBagController mediumBag;
    [Tooltip("HUD bag button icon used with the Medium Bag.")]
    [SerializeField] private Sprite mediumBagButtonIcon;

    // Matches the order of the go-bag picker on the difficulty panel
    private const int STANDARD_BAG = 0;
    private const int SMALL_BAG = 1;
    private const int MEDIUM_BAG = 2;

    [Header("Bag Button Progress")]
    private BagButtonProgressBar bagButtonProgressBar;

    [Header("Storage Size")]
    [Tooltip("Kept clear around the furniture picture when it is scaled to match the bag's grid cells.")]
    [SerializeField] private float storagePadding = 16f;

    // The pouches of every bag with a grid, which shrink (frame and all) when the furniture can't grow to match them
    private InventoryGridDisplay[] goBagGrids;

    [Header("Go Bag Full Message")]
    [SerializeField] private TextMeshProUGUI goBagFullMessage;
    private Coroutine goBagFullMessageCoroutine;

    /// <summary>True while the inventory is on screen, from the bag button or a furniture tap.</summary>
    public bool IsOpen => inventoryPanel != null && inventoryPanel.activeInHierarchy;

    /// <summary>True while a furniture's storage is showing beside the bag.</summary>
    public bool IsStorageOpen => IsOpen && storageSide != null && storageSide.gameObject.activeInHierarchy;

    /// <summary>
    /// True once a pocket of whichever bag is in use is open and ready to take items. Pockets
    /// show their grid only after the opening animation, so this waits for that too.
    /// </summary>
    public bool IsBagPocketOpen
    {
        get
        {
            if (smallBag != null && smallBag.isActiveAndEnabled)
                return smallBag.IsOpen;

            if (mediumBag != null && mediumBag.isActiveAndEnabled)
                return mediumBag.IsOpen;

            return standardBag != null && standardBag.isActiveAndEnabled && standardBag.IsOpen;
        }
    }

    /// <summary>The pieces of the panel the onboarding points at.</summary>
    public RectTransform GoBagSide => goBagSide;
    public RectTransform StorageSide => storageSide;
    public BagPouchNavigator PouchNavigator => pouchNavigator;

    /// <summary>The picture of the furniture being searched, with its compartments on it.</summary>
    public RectTransform StoragePicture => modelDisplayImage != null ? modelDisplayImage.rectTransform : storageSide;

    /// <summary>
    /// The open pocket's grid items can be dropped into, or the go-bag side of the screen while
    /// the bag is still moving between pockets.
    /// </summary>
    public RectTransform OpenPocket
    {
        get
        {
            RectTransform pocket = pouchNavigator != null ? pouchNavigator.OpenPouchGrid : null;
            return pocket != null ? pocket : goBagSide;
        }
    }
    public Button CloseButton => closeButton;
    public GameObject BagButtonGroup => bagButtonGroup;

    /// <summary>The bag in use, whose pouches the navigator moves through.</summary>
    private IBagPouches ActiveBag
    {
        get
        {
            if (smallBag != null && smallBag.isActiveAndEnabled)
                return smallBag;
            if (mediumBag != null && mediumBag.isActiveAndEnabled)
                return mediumBag;
            if (standardBag != null && standardBag.isActiveAndEnabled)
                return standardBag;
            return null;
        }
    }

    void Start()
    {
        // Setup main inventory button listeners
        if (bagButton != null)
        {
            bagButton.onClick.AddListener(OpenInventory);
            bagButtonProgressBar = bagButton.GetComponent<BagButtonProgressBar>();
        }

        if (closeButton != null)
            closeButton.onClick.AddListener(CloseInventory);

        // Hide inventory panel initially
        if (inventoryPanel != null)
            inventoryPanel.SetActive(false);

        // Hide bag button initially
        if (bagButtonGroup != null)
            bagButtonGroup.SetActive(false);

        // Hide Go Bag full message initially
        if (goBagFullMessage != null)
            goBagFullMessage.gameObject.SetActive(false);

        ApplySelectedBag();
    }

    /// <summary>
    /// Shows the bag picked on the difficulty panel, falling back to the standard bag if the
    /// picked one isn't set up in this scene.
    /// </summary>
    private void ApplySelectedBag()
    {
        int selected = DifficultyPanel.GetActiveGoBag();
        bool useSmallBag = selected == SMALL_BAG && smallBag != null;
        bool useMediumBag = selected == MEDIUM_BAG && mediumBag != null;

        if (standardBag != null)
        {
            // Its grids sit beside the bag rather than under it, so hide them first
            standardBag.ResetClosed();
            standardBag.gameObject.SetActive(!useSmallBag && !useMediumBag);
        }

        if (smallBag != null)
            smallBag.gameObject.SetActive(useSmallBag);

        if (mediumBag != null)
            mediumBag.gameObject.SetActive(useMediumBag);

        Sprite icon = useSmallBag ? smallBagButtonIcon : useMediumBag ? mediumBagButtonIcon : null;
        if (icon != null && bagButton != null)
        {
            Image image = bagButton.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = icon;
                image.preserveAspect = true;
            }
        }
    }

    /// <summary>Lets the Small / Medium Bag reshuffle their slot contents when a storage opens.</summary>
    private void NotifyBagsStorageOpened()
    {
        if (smallBag != null && smallBag.isActiveAndEnabled)
            smallBag.OnStorageOpened();

        if (mediumBag != null && mediumBag.isActiveAndEnabled)
            mediumBag.OnStorageOpened();
    }

    /// <summary>Zooms in on the bag's last-viewed pouch and opens it, ready for the arrows.</summary>
    private void ShowBagPouches()
    {
        if (pouchNavigator != null)
            pouchNavigator.Show(ActiveBag);
    }

    public void OpenInventory()
    {
        OpenInventory(null);
    }

    public void OpenInventory(Sprite modelSprite)
    {
        // Hide quiz panel when opening inventory
        if (quizPanel != null)
            quizPanel.SetActive(false);

        if (inventoryPanel != null)
            inventoryPanel.SetActive(true);

        // Show close button (default behavior)
        ShowCloseButton();

        // If no sprite, it's being opened from the bag button (full screen mode)
        if (modelSprite == null)
        {
            SetFullScreenMode();
        }
        else
        {
            // Sprite provided, it's from a model click (split screen mode)
            SetSplitScreenMode(modelSprite);

            // Every storage opened reshuffles the Small / Medium Bag's slots
            NotifyBagsStorageOpened();
        }

        ShowBagPouches();
    }

    public void OpenInventoryWithStorage(StorageFurniture furniture)
    {
        StorageLayout layout = furniture != null ? furniture.Layout : null;

        // Hide quiz panel when opening inventory
        if (quizPanel != null)
            quizPanel.SetActive(false);

        if (inventoryPanel != null)
            inventoryPanel.SetActive(true);

        // Show close button (default behavior)
        ShowCloseButton();

        // Set up split screen mode with storage
        SetSplitScreenMode(layout != null ? layout.FurnitureSprite : null);

        // Every storage opened reshuffles the Small / Medium Bag's slots
        NotifyBagsStorageOpened();

        ShowFurnitureCompartments(furniture, layout);

        ShowBagPouches();
    }

    public void HideCloseButton()
    {
        if (closeButton != null)
            closeButton.gameObject.SetActive(false);

        if (closeButtonBack != null)
            closeButtonBack.gameObject.SetActive(false);
    }

    public void ShowCloseButton()
    {
        if (closeButton != null)
            closeButton.gameObject.SetActive(true);

        if (closeButtonBack != null)
            closeButtonBack.gameObject.SetActive(true);
    }

    private void SetFullScreenMode()
    {
        // GoBagSide takes full screen, StorageSide is hidden
        if (goBagSide != null)
        {
            goBagSide.anchorMin = Vector2.zero;
            goBagSide.anchorMax = Vector2.one;
            goBagSide.offsetMin = Vector2.zero;
            goBagSide.offsetMax = Vector2.zero;
            goBagSide.gameObject.SetActive(true);
        }

        if (storageSide != null)
        {
            storageSide.gameObject.SetActive(false);
        }

        if (modelDisplayImage != null)
        {
            modelDisplayImage.gameObject.SetActive(false);
        }
    }

    void LateUpdate()
    {
        MatchStorageToBag();
    }

    /// <summary>
    /// Scales the furniture picture, compartments and all, so its grid cells come out the same
    /// size on screen as the bag's. The bag scales itself to fit its side of the screen, so this
    /// keeps up with it. Where the furniture can't grow that much and still fit, it fills its
    /// side and the bag's pouches shrink to its cells instead. Bags without a grid (the Small Bag)
    /// leave the furniture at its normal size.
    /// </summary>
    private void MatchStorageToBag()
    {
        if (goBagGrids == null && goBagSide != null)
            goBagGrids = System.Array.FindAll(goBagSide.GetComponentsInChildren<InventoryGridDisplay>(true), d => d.IsGoBagGrid);

        float bagGridScale = 1f;
        InventoryGridDisplay bagGrid = ActiveBagGrid();

        if (IsStorageOpen && openLayout != null && modelDisplayImage != null)
        {
            RectTransform picture = modelDisplayImage.rectTransform;
            InventoryGridDisplay storageGrid = null;
            foreach (var compartment in openLayout.Compartments)
            {
                if (compartment.display != null)
                {
                    storageGrid = compartment.display;
                    break;
                }
            }

            float scale = 1f;
            if (bagGrid != null && storageGrid != null && picture.parent != null)
            {
                float storageCell = storageGrid.GetCellSize() * picture.parent.lossyScale.x;
                float match = storageCell > 0f ? bagGrid.WorldCellSize() / storageCell : 1f;

                Vector2 room = storageSide.rect.size - Vector2.one * (storagePadding * 2f);
                Vector2 size = picture.rect.size;
                float fit = size.x > 0f && size.y > 0f ? Mathf.Min(room.x / size.x, room.y / size.y) : match;

                scale = Mathf.Max(Mathf.Min(match, fit), 0.1f);
                if (match > 0f)
                    bagGridScale = Mathf.Min(scale / match, 1f);
            }

            if (!Mathf.Approximately(picture.localScale.x, scale))
                picture.localScale = new Vector3(scale, scale, 1f);
        }

        if (goBagGrids != null)
            foreach (InventoryGridDisplay grid in goBagGrids)
                grid.SetGridScale(bagGridScale);
    }

    /// <summary>A pouch grid of the bag in use, or null for a bag without one.</summary>
    private InventoryGridDisplay ActiveBagGrid()
    {
        IBagPouches bag = ActiveBag;
        if (bag == null)
            return null;

        for (int i = 0; i < bag.PouchCount; i++)
        {
            RectTransform pouch = bag.GetPouchGrid(i);
            InventoryGridDisplay grid = pouch != null ? pouch.GetComponentInChildren<InventoryGridDisplay>(true) : null;
            if (grid != null && grid.IsGoBagGrid)
                return grid;
        }
        return null;
    }

    /// <summary>
    /// Lays the furniture's compartments over its picture: the layout for its furniture type
    /// is created on top of the picture, and each compartment display is handed that piece of
    /// furniture's own grid. The previous furniture's layout is thrown away first.
    /// </summary>
    private void ShowFurnitureCompartments(StorageFurniture furniture, StorageLayout layout)
    {
        if (openLayout != null)
        {
            Destroy(openLayout.gameObject);
            openLayout = null;
        }

        if (furniture == null || layout == null || modelDisplayImage == null)
            return;

        RectTransform imageRect = modelDisplayImage.rectTransform;
        imageRect.sizeDelta = layout.DisplaySize;

        openLayout = Instantiate(layout, imageRect, false);
        var compartments = openLayout.Compartments;
        for (int i = 0; i < compartments.Count; i++)
        {
            if (compartments[i].display != null)
                compartments[i].display.ShowStorageCompartment(furniture.GetCompartment(i));
        }
    }

    private void SetSplitScreenMode(Sprite modelSprite)
    {
        // Both take half the screen side-by-side
        if (goBagSide != null)
        {
            goBagSide.anchorMin = new Vector2(0, 0);
            goBagSide.anchorMax = new Vector2(0.5f, 1);
            goBagSide.offsetMin = Vector2.zero;
            goBagSide.offsetMax = Vector2.zero;
            goBagSide.gameObject.SetActive(true);
        }

        if (storageSide != null)
        {
            storageSide.anchorMin = new Vector2(0.5f, 0);
            storageSide.anchorMax = Vector2.one;
            storageSide.offsetMin = Vector2.zero;
            storageSide.offsetMax = Vector2.zero;
            storageSide.gameObject.SetActive(true);
        }

        // Display the model sprite
        if (modelSprite != null && modelDisplayImage != null)
        {
            modelDisplayImage.sprite = modelSprite;
            modelDisplayImage.gameObject.SetActive(true);
        }
    }

    public void OpenInventoryHalfScreen()
    {
        // Hide quiz panel when opening inventory
        if (quizPanel != null)
            quizPanel.SetActive(false);

        if (inventoryPanel != null)
            inventoryPanel.SetActive(true);

        // GoBagSide takes whatever the quiz panel leaves on the left. The quiz is authored a
        // little wider than half: its dialogue box alone fills a half-screen at 16:9.
        float split = quizPanel != null ? ((RectTransform)quizPanel.transform).anchorMin.x : 0.5f;
        if (goBagSide != null)
        {
            goBagSide.anchorMin = new Vector2(0, 0);
            goBagSide.anchorMax = new Vector2(split, 1);
            goBagSide.offsetMin = Vector2.zero;
            goBagSide.offsetMax = Vector2.zero;
            goBagSide.gameObject.SetActive(true);
        }

        if (storageSide != null)
        {
            storageSide.gameObject.SetActive(false);
        }

        if (modelDisplayImage != null)
        {
            modelDisplayImage.gameObject.SetActive(false);
        }

        ShowBagPouches();
    }

    private void CloseInventory()
    {
        if (inventoryPanel != null)
            inventoryPanel.SetActive(false);

        // Stop all running coroutines
        StopAllCoroutines();

        // Hide the Go Bag full message
        if (goBagFullMessage != null)
            goBagFullMessage.gameObject.SetActive(false);

        // Hide the description panel when closing inventory
        InventoryItemDragHandler.ForceHideDescriptionPanel();
    }

    public void ShowBagButton()
    {
        if (bagButtonGroup != null)
            bagButtonGroup.SetActive(true);
    }
    
    public void ShowGoBagFullMessage()
    {
        ShowGoBagMessage("Go-bag is full!");
    }

    public void ShowWeightLimitMessage()
    {
        ShowGoBagMessage("Item exceeds the weight limit!");
    }

    private void ShowGoBagMessage(string messageText)
    {
        if (goBagFullMessage == null)
            return;

        // Update the message text
        goBagFullMessage.text = messageText;

        // Stop any existing fade coroutine
        if (goBagFullMessageCoroutine != null)
            StopCoroutine(goBagFullMessageCoroutine);

        goBagFullMessageCoroutine = StartCoroutine(FadeGoBagFullMessage(show: true, duration: 3f));
    }

    private IEnumerator FadeGoBagFullMessage(bool show, float duration)
    {
        if (goBagFullMessage == null)
            yield break;

        CanvasGroup canvasGroup = goBagFullMessage.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = goBagFullMessage.gameObject.AddComponent<CanvasGroup>();

        float elapsed = 0f;

        if (show)
        {
            goBagFullMessage.gameObject.SetActive(true);
            canvasGroup.alpha = 0f;

            while (elapsed < 0.3f)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = Mathf.Clamp01(elapsed / 0.3f);
                yield return null;
            }

            canvasGroup.alpha = 1f;

            // Keep visible for remainder of duration
            yield return new WaitForSeconds(duration - 0.3f);

            // Then fade out
            elapsed = 0f;
            while (elapsed < 0.3f)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / 0.3f);
                yield return null;
            }

            goBagFullMessage.gameObject.SetActive(false);
        }
    }
}

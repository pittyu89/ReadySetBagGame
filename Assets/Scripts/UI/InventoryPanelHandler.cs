using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class InventoryPanelHandler : MonoBehaviour
{
    [Header("Main Inventory Panel")]
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button closeButtonBack;
    [SerializeField] private Button bagButton;
    [SerializeField] private GameObject bagButtonGroup;
    [SerializeField] private Image modelDisplayImage;
    [SerializeField] private RectTransform goBagSide;
    [SerializeField] private RectTransform storageSide;
    [SerializeField] private GameObject quizPanel;

    [Header("Slot Buttons")]
    [SerializeField] private Button topButton;
    [SerializeField] private Button middleButton;
    [SerializeField] private Button bottomButton;
    [SerializeField] private Button leftButton;
    [SerializeField] private Button rightButton;
    [SerializeField] private Button slotPanelCloseButton;

    [Header("Slot Panels")]
    [SerializeField] private GameObject topPanel;
    [SerializeField] private GameObject middlePanel;
    [SerializeField] private GameObject bottomPanel;
    [SerializeField] private GameObject leftPanel;
    [SerializeField] private GameObject rightPanel;

    [Header("Animation")]
    [SerializeField] private Animator BagAnimator;

    [Header("Bag Type")]
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

    [Header("Audio")]
    [SerializeField] private AudioClip openTopBagAudio;
    [SerializeField] private AudioClip openZipBagAudio;

    [Header("Bag Button Progress")]
    private BagButtonProgressBar bagButtonProgressBar;
    
    [Header("Go Bag Full Message")]
    [SerializeField] private TextMeshProUGUI goBagFullMessage;
    private Coroutine goBagFullMessageCoroutine;

    private GameObject currentOpenPanel;
    private Vector2 originalTopButtonSize;

    private void PlayZipBagSFX()
    {
        if (openZipBagAudio != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(openZipBagAudio);
    }

    private void PlayTopBagSFX()
    {
        if (openTopBagAudio != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(openTopBagAudio);
    }

    void Start()
    {
        // Store original top button size
        if (topButton != null)
        {
            RectTransform topButtonRect = topButton.GetComponent<RectTransform>();
            if (topButtonRect != null)
                originalTopButtonSize = topButtonRect.sizeDelta;
        }

        // Setup main inventory button listeners
        if (bagButton != null)
        {
            bagButton.onClick.AddListener(OpenInventory);
            bagButtonProgressBar = bagButton.GetComponent<BagButtonProgressBar>();
        }

        if (closeButton != null)
            closeButton.onClick.AddListener(CloseInventory);

        // Setup slot button listeners
        if (topButton != null)
            topButton.onClick.AddListener(() => ToggleTopPanelWithAnimation(topButton, middleButton, bottomButton, leftButton, rightButton));

        if (middleButton != null)
            middleButton.onClick.AddListener(() => ToggleMiddlePanelWithAnimation(middleButton, topButton, bottomButton, leftButton, rightButton));

        if (bottomButton != null)
            bottomButton.onClick.AddListener(() => ToggleBottomPanelWithAnimation(bottomButton, topButton, middleButton, leftButton, rightButton));

        if (leftButton != null)
            leftButton.onClick.AddListener(() => ToggleSidePanelsWithDisable(leftButton, rightButton, topButton, bottomButton));

        if (rightButton != null)
            rightButton.onClick.AddListener(() => ToggleSidePanelsWithDisable(rightButton, leftButton, topButton, bottomButton));

        // Setup slot panel close button listener
        if (slotPanelCloseButton != null)
        {
            slotPanelCloseButton.onClick.AddListener(CloseCurrentSlotPanel);
            slotPanelCloseButton.gameObject.SetActive(false);
        }

        // Hide inventory panel initially
        if (inventoryPanel != null)
            inventoryPanel.SetActive(false);

        // Hide bag button initially
        if (bagButtonGroup != null)
            bagButtonGroup.SetActive(false);

        // Hide Go Bag full message initially
        if (goBagFullMessage != null)
            goBagFullMessage.gameObject.SetActive(false);

        // Hide all slot panels initially
        HideAllSlotPanels();

        ApplySelectedBag();
    }

    /// <summary>
    /// Shows the bag picked on the difficulty panel, falling back to the standard bag if the
    /// picked one isn't set up in this scene.
    /// </summary>
    private void ApplySelectedBag()
    {
        int selected = PlayerPrefs.GetInt(DifficultyPanelManager.SELECTED_GO_BAG_KEY, STANDARD_BAG);
        bool useSmallBag = selected == SMALL_BAG && smallBag != null;
        bool useMediumBag = selected == MEDIUM_BAG && mediumBag != null;

        if (BagAnimator != null)
            BagAnimator.gameObject.SetActive(!useSmallBag && !useMediumBag);

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

        // Rebind animator and reset to idle state
        // Skipped when the Small Bag is in use and the standard bag is hidden
        if (BagAnimator != null && BagAnimator.isActiveAndEnabled)
        {
            BagAnimator.Rebind();
            BagAnimator.Update(0f);
        }

        // Make sure all buttons are active
        if (topButton != null)
        {
            topButton.gameObject.SetActive(true);
            topButton.interactable = true;
        }
        if (middleButton != null)
        {
            middleButton.gameObject.SetActive(true);
            middleButton.interactable = true;
        }
        if (bottomButton != null)
        {
            bottomButton.gameObject.SetActive(true);
            bottomButton.interactable = true;
        }
        if (leftButton != null)
        {
            leftButton.gameObject.SetActive(true);
            leftButton.interactable = true;
        }
        if (rightButton != null)
        {
            rightButton.gameObject.SetActive(true);
            rightButton.interactable = true;
        }

        currentOpenPanel = null;
    }

    public void OpenInventoryWithStorage(Sprite modelSprite, ClickableModel model)
    {
        // Hide quiz panel when opening inventory
        if (quizPanel != null)
            quizPanel.SetActive(false);

        if (inventoryPanel != null)
            inventoryPanel.SetActive(true);

        // Show close button (default behavior)
        ShowCloseButton();

        // Set up split screen mode with storage
        SetSplitScreenMode(modelSprite);

        // Every storage opened reshuffles the Small / Medium Bag's slots
        NotifyBagsStorageOpened();

        // Set model display size from ClickableModel
        if (model != null && modelDisplayImage != null)
        {
            RectTransform imageRect = modelDisplayImage.GetComponent<RectTransform>();
            if (imageRect != null)
            {
                imageRect.sizeDelta = new Vector2(model.GetModelDisplayWidth(), model.GetModelDisplayHeight());

            }
        }

        // Display ALL storage sections at once
        if (model != null && storageSide != null)
        {
            string[] sectionNames = model.GetAllStorageSectionNames();

            
            // First, hide all storage grid displays to clear previous model's display
            InventoryGridDisplay[] allDisplays = storageSide.GetComponentsInChildren<InventoryGridDisplay>(true);
            foreach (InventoryGridDisplay display in allDisplays)
            {
                if (display.gameObject != null)
                {
                    display.gameObject.SetActive(false);
                }
            }
            
            // For each section, find matching container (search all children, not just direct)
            foreach (string sectionName in sectionNames)
            {

                
                // Get all children recursively
                Transform[] allChildren = storageSide.GetComponentsInChildren<Transform>(true);
                
                foreach (Transform child in allChildren)
                {
                    string childName = child.gameObject.name;
                    
                    // Check if this transform matches the section name
                    if (childName.Contains(sectionName))
                    {

                        
                        // Get InventoryGridDisplay from this container
                        InventoryGridDisplay gridDisplay = child.GetComponent<InventoryGridDisplay>();
                        if (gridDisplay != null)
                        {

                            child.gameObject.SetActive(true); // Make sure it's visible
                            gridDisplay.SetStorageModelForSection(model, sectionName);
                        }
                        else
                        {

                        }
                        break; // Found match for this section, move to next section
                    }
                }
            }
        }

        // Rebind animator and reset to idle state
        // Skipped when the Small Bag is in use and the standard bag is hidden
        if (BagAnimator != null && BagAnimator.isActiveAndEnabled)
        {
            BagAnimator.Rebind();
            BagAnimator.Update(0f);
        }

        // Make sure all buttons are active
        if (topButton != null)
        {
            topButton.gameObject.SetActive(true);
            topButton.interactable = true;
        }
        if (middleButton != null)
        {
            middleButton.gameObject.SetActive(true);
            middleButton.interactable = true;
        }
        if (bottomButton != null)
        {
            bottomButton.gameObject.SetActive(true);
            bottomButton.interactable = true;
        }
        if (leftButton != null)
        {
            leftButton.gameObject.SetActive(true);
            leftButton.interactable = true;
        }
        if (rightButton != null)
        {
            rightButton.gameObject.SetActive(true);
            rightButton.interactable = true;
        }

        currentOpenPanel = null;
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

    private void ShowSlotPanelCloseButton()
    {
        if (slotPanelCloseButton != null)
            slotPanelCloseButton.gameObject.SetActive(true);
    }

    private void HideSlotPanelCloseButton()
    {
        if (slotPanelCloseButton != null)
            slotPanelCloseButton.gameObject.SetActive(false);
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

        // GoBagSide takes left half, no storage side or model display
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
            storageSide.gameObject.SetActive(false);
        }

        if (modelDisplayImage != null)
        {
            modelDisplayImage.gameObject.SetActive(false);
        }

        // Rebind animator and reset to idle state
        // Skipped when the Small Bag is in use and the standard bag is hidden
        if (BagAnimator != null && BagAnimator.isActiveAndEnabled)
        {
            BagAnimator.Rebind();
            BagAnimator.Update(0f);
        }

        // Make sure all buttons are active
        if (topButton != null)
        {
            topButton.gameObject.SetActive(true);
            topButton.interactable = true;
        }
        if (middleButton != null)
        {
            middleButton.gameObject.SetActive(true);
            middleButton.interactable = true;
        }
        if (bottomButton != null)
        {
            bottomButton.gameObject.SetActive(true);
            bottomButton.interactable = true;
        }
        if (leftButton != null)
        {
            leftButton.gameObject.SetActive(true);
            leftButton.interactable = true;
        }
        if (rightButton != null)
        {
            rightButton.gameObject.SetActive(true);
            rightButton.interactable = true;
        }

        currentOpenPanel = null;
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

        // Also close all slot panels
        HideAllSlotPanels();
        
        // Hide the slot panel close button
        HideSlotPanelCloseButton();
        
        // Hide the description panel when closing inventory
        InventoryItemDragHandler.ForceHideDescriptionPanel();
        
        // Re-enable all buttons
        if (topButton != null)
            topButton.interactable = true;
        if (middleButton != null)
            middleButton.interactable = true;
        if (bottomButton != null)
            bottomButton.interactable = true;
        if (leftButton != null)
            leftButton.interactable = true;
        if (rightButton != null)
            rightButton.interactable = true;

        currentOpenPanel = null;
    }

    private void HideAllSlotPanels()
    {
        if (topPanel != null)
            topPanel.SetActive(false);
        if (middlePanel != null)
            middlePanel.SetActive(false);
        if (bottomPanel != null)
            bottomPanel.SetActive(false);
        if (leftPanel != null)
            leftPanel.SetActive(false);
        if (rightPanel != null)
            rightPanel.SetActive(false);
    }

    private void CloseCurrentSlotPanel()
    {
        if (currentOpenPanel == null)
            return;

        // Hide close button immediately when closing starts
        HideSlotPanelCloseButton();

        // Determine which panel is open and close it appropriately
        if (currentOpenPanel == topPanel)
        {
            if (topPanel != null)
                topPanel.SetActive(false);
            if (topButton != null)
            {
                RectTransform rectTransform = topButton.GetComponent<RectTransform>();
                if (rectTransform != null)
                    rectTransform.sizeDelta = originalTopButtonSize;
            }
            if (BagAnimator != null)
                BagAnimator.Play("CloseTop");
            
            // Play close top bag audio
            PlayTopBagSFX();
            
            // Re-enable all buttons
            if (topButton != null)
                topButton.interactable = true;
            if (middleButton != null)
                middleButton.interactable = true;
            if (bottomButton != null)
                bottomButton.interactable = true;
            if (leftButton != null)
                leftButton.interactable = true;
            if (rightButton != null)
                rightButton.interactable = true;
        }
        else if (currentOpenPanel == middlePanel)
        {
            if (middlePanel != null)
                middlePanel.SetActive(false);
            if (BagAnimator != null)
                BagAnimator.Play("CloseMiddle");
            
            // Play close zip bag audio
            PlayZipBagSFX();
            
            // Re-enable all buttons
            if (topButton != null)
                topButton.interactable = true;
            if (middleButton != null)
                middleButton.interactable = true;
            if (bottomButton != null)
                bottomButton.interactable = true;
            if (leftButton != null)
                leftButton.interactable = true;
            if (rightButton != null)
                rightButton.interactable = true;
        }
        else if (currentOpenPanel == bottomPanel)
        {
            if (bottomPanel != null)
                bottomPanel.SetActive(false);
            if (BagAnimator != null)
                BagAnimator.Play("CloseBottom");
            
            // Play close zip bag audio
            PlayZipBagSFX();
            
            // Re-enable all buttons
            if (topButton != null)
                topButton.interactable = true;
            if (middleButton != null)
                middleButton.interactable = true;
            if (bottomButton != null)
                bottomButton.interactable = true;
            if (leftButton != null)
                leftButton.interactable = true;
            if (rightButton != null)
                rightButton.interactable = true;
        }
        else if (currentOpenPanel == leftPanel || currentOpenPanel == rightPanel)
        {
            if (leftPanel != null)
                leftPanel.SetActive(false);
            if (rightPanel != null)
                rightPanel.SetActive(false);
            if (BagAnimator != null)
                BagAnimator.Play("CloseSides");
            
            // Play close zip bag audio
            PlayZipBagSFX();
            
            currentOpenPanel = null;
            
            // Re-enable all buttons
            if (topButton != null)
                topButton.interactable = true;
            if (middleButton != null)
                middleButton.interactable = true;
            if (bottomButton != null)
                bottomButton.interactable = true;
            if (leftButton != null)
                leftButton.interactable = true;
            if (rightButton != null)
                rightButton.interactable = true;
        }

        currentOpenPanel = null;
    }

    private void ToggleSidePanelsWithDisable(Button clickedButton, Button otherSideButton, Button topBtn, Button bottomBtn)
    {
        // Check if side panels are currently open
        bool areSidePanelsOpen = leftPanel != null && leftPanel.activeSelf && rightPanel != null && rightPanel.activeSelf;

        if (areSidePanelsOpen)
        {
            HideSlotPanelCloseButton();
            
            // Hide both side panels first
            if (leftPanel != null)
                leftPanel.SetActive(false);
            if (rightPanel != null)
                rightPanel.SetActive(false);
            
            // Then play close animation
            if (BagAnimator != null)
                BagAnimator.Play("CloseSides");
            
            // Play close zip bag audio
            PlayZipBagSFX();
            
            currentOpenPanel = null;
            
            // Re-enable all buttons
            if (clickedButton != null)
                clickedButton.interactable = true;
            if (otherSideButton != null)
                otherSideButton.interactable = true;
            if (topBtn != null)
                topBtn.interactable = true;
            if (bottomBtn != null)
                bottomBtn.interactable = true;
            if (middleButton != null)
                middleButton.interactable = true;
        }
        else
        {
            // Close all other panels
            HideAllSlotPanels();
            
            // Play open animation FIRST
            if (BagAnimator != null)
                BagAnimator.Play("OpenSides");
            
            // Play open zip bag audio
            PlayZipBagSFX();
            
            // Show panels after animation completes
            StartCoroutine(ShowSidePanelsAfterAnimation());
            
            currentOpenPanel = leftPanel; // Track that side panels are open
            
            // Disable all slot buttons
            if (clickedButton != null)
                clickedButton.interactable = false;
            if (otherSideButton != null)
                otherSideButton.interactable = false;
            if (topBtn != null)
                topBtn.interactable = false;
            if (middleButton != null)
                middleButton.interactable = false;
            if (bottomBtn != null)
                bottomBtn.interactable = false;
        }
    }

    private void ToggleBottomPanelWithAnimation(Button clickedButton, Button topBtn, Button middleBtn, Button leftBtn, Button rightBtn)
    {
        // If bottom panel is already open, close it with animation
        if (currentOpenPanel == bottomPanel)
        {
            HideSlotPanelCloseButton();
            
            // Hide panel first
            if (bottomPanel != null)
                bottomPanel.SetActive(false);
            
            // Then play close animation
            if (BagAnimator != null)
                BagAnimator.Play("CloseBottom");
            
            // Play close zip bag audio
            PlayZipBagSFX();

            currentOpenPanel = null;
            
            // Re-enable all buttons
            if (clickedButton != null)
                clickedButton.interactable = true;
            if (topBtn != null)
                topBtn.interactable = true;
            if (middleBtn != null)
                middleBtn.interactable = true;
            if (leftBtn != null)
                leftBtn.interactable = true;
            if (rightBtn != null)
                rightBtn.interactable = true;
        }
        else
        {
            // Close all other panels
            HideAllSlotPanels();
            
            // Play open animation FIRST
            if (BagAnimator != null)
                BagAnimator.Play("OpenBottom");
            
            // Play open zip bag audio
            PlayZipBagSFX();
            
            // Show panel after animation completes
            StartCoroutine(ShowPanelAfterAnimation(bottomPanel));
            
            currentOpenPanel = bottomPanel;
            
            // Disable all slot buttons
            if (clickedButton != null)
                clickedButton.interactable = false;
            if (topBtn != null)
                topBtn.interactable = false;
            if (middleBtn != null)
                middleBtn.interactable = false;
            if (leftBtn != null)
                leftBtn.interactable = false;
            if (rightBtn != null)
                rightBtn.interactable = false;
        }
    }

    private void ToggleMiddlePanelWithAnimation(Button clickedButton, Button topBtn, Button bottomBtn, Button leftBtn, Button rightBtn)
    {
        // If middle panel is already open, close it with animation
        if (currentOpenPanel == middlePanel)
        {
            HideSlotPanelCloseButton();
            
            // Hide panel first
            if (middlePanel != null)
                middlePanel.SetActive(false);
            
            // Then play close animation
            if (BagAnimator != null)
                BagAnimator.Play("CloseMiddle");
            
            // Play close zip bag audio
            PlayZipBagSFX();

            currentOpenPanel = null;
            
            // Re-enable all buttons
            if (clickedButton != null)
                clickedButton.interactable = true;
            if (topBtn != null)
                topBtn.interactable = true;
            if (bottomBtn != null)
                bottomBtn.interactable = true;
            if (leftBtn != null)
                leftBtn.interactable = true;
            if (rightBtn != null)
                rightBtn.interactable = true;
        }
        else
        {
            // Close all other panels
            HideAllSlotPanels();
            
            // Play open animation FIRST
            if (BagAnimator != null)
                BagAnimator.Play("OpenMiddle");
            
            // Play open zip bag audio
            PlayZipBagSFX();
            
            // Show panel after animation completes
            StartCoroutine(ShowPanelAfterAnimation(middlePanel));
            
            currentOpenPanel = middlePanel;
            
            // Disable all slot buttons
            if (clickedButton != null)
                clickedButton.interactable = false;
            if (topBtn != null)
                topBtn.interactable = false;
            if (bottomBtn != null)
                bottomBtn.interactable = false;
            if (leftBtn != null)
                leftBtn.interactable = false;
            if (rightBtn != null)
                rightBtn.interactable = false;
        }
    }

    private void ToggleTopPanelWithAnimation(Button clickedButton, Button middleBtn, Button bottomBtn, Button leftBtn, Button rightBtn)
    {
        // If top panel is already open, close it with animation
        if (currentOpenPanel == topPanel)
        {
            HideSlotPanelCloseButton();
            
            // Hide panel first
            if (topPanel != null)
                topPanel.SetActive(false);
            
            // Resize button back to original size
            if (topButton != null)
            {
                RectTransform rectTransform = topButton.GetComponent<RectTransform>();
                if (rectTransform != null)
                    rectTransform.sizeDelta = originalTopButtonSize;
            }
            
            // Then play close animation
            if (BagAnimator != null)
                BagAnimator.Play("CloseTop");
            
            // Play close top bag audio
            PlayTopBagSFX();

            currentOpenPanel = null;
            
            // Re-enable all buttons
            if (clickedButton != null)
                clickedButton.interactable = true;
            if (middleBtn != null)
                middleBtn.interactable = true;
            if (bottomBtn != null)
                bottomBtn.interactable = true;
            if (leftBtn != null)
                leftBtn.interactable = true;
            if (rightBtn != null)
                rightBtn.interactable = true;
        }
        else
        {
            // Close all other panels
            HideAllSlotPanels();
            
            // Play open animation FIRST
            if (BagAnimator != null)
                BagAnimator.Play("OpenTop");
            
            // Play open top bag audio
            PlayTopBagSFX();
            
            // Show panel after animation completes and resize button
            StartCoroutine(ShowPanelAfterAnimation(topPanel));
            
            currentOpenPanel = topPanel;
            
            // Disable all slot buttons
            if (clickedButton != null)
                clickedButton.interactable = false;
            if (middleBtn != null)
                middleBtn.interactable = false;
            if (bottomBtn != null)
                bottomBtn.interactable = false;
            if (leftBtn != null)
                leftBtn.interactable = false;
            if (rightBtn != null)
                rightBtn.interactable = false;
        }
    }

    private IEnumerator ShowPanelAfterAnimation(GameObject panel)
    {
        // Wait a frame for animation to start
        yield return null;
        
        if (BagAnimator != null)
        {
            // Get the current animation state info
            AnimatorStateInfo stateInfo = BagAnimator.GetCurrentAnimatorStateInfo(0);
            float animationDuration = stateInfo.length;
            
            // Wait for animation to complete
            yield return new WaitForSeconds(animationDuration);
        }
        
        // Show the panel after animation finishes
        if (panel != null)
            panel.SetActive(true);
        
        // Show the close button after animation finishes
        ShowSlotPanelCloseButton();
    }

    private IEnumerator ShowSidePanelsAfterAnimation()
    {
        // Wait a frame for animation to start
        yield return null;
        
        if (BagAnimator != null)
        {
            // Get the current animation state info
            AnimatorStateInfo stateInfo = BagAnimator.GetCurrentAnimatorStateInfo(0);
            float animationDuration = stateInfo.length;
            
            // Wait for animation to complete
            yield return new WaitForSeconds(animationDuration);
        }
        
        // Show the panels after animation finishes
        if (leftPanel != null)
            leftPanel.SetActive(true);
        if (rightPanel != null)
            rightPanel.SetActive(true);
        
        // Show the close button after animation finishes
        ShowSlotPanelCloseButton();
    }

    public void ShowBagButton()
    {
        if (bagButtonGroup != null)
            bagButtonGroup.SetActive(true);
    }
    
    public void ShowGoBagFullMessage()
    {
        ShowGoBagMessage("Go Bag is Full!");
    }

    public void ShowWeightLimitMessage()
    {
        ShowGoBagMessage("Item Exceeds Weight Limit!");
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

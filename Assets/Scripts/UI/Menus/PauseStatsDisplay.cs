using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PauseStatsDisplay : MonoBehaviour
{
    [SerializeField] private Image avatarImage;
    [SerializeField] private TextMeshProUGUI weightValue;
    [SerializeField] private TextMeshProUGUI itemsValue;
    [SerializeField] private TextMeshProUGUI timeValue;

    [Header("Progress Bar")]
    [SerializeField] private Image progressBarBackground;
    [SerializeField] private RectTransform progressFillBar;
    [SerializeField] private Image progressFillBarImage;

    [Header("Progress Bar Colors")]
    [SerializeField] private Color emptyColor = Color.green;
    [SerializeField] private Color warningColor = new Color(1f, 0.843f, 0f);  // Yellow
    [SerializeField] private Color fullColor = Color.red;
    [SerializeField] private float warningThreshold = 0.75f;  // 75% capacity
    [SerializeField] private float criticalThreshold = 0.95f; // 95% capacity

    [Header("Avatar Sprites")]
    [SerializeField] private Sprite femaleAvatarSprite;
    [SerializeField] private Sprite maleAvatarSprite;

    private GameTimer timerScript;
    private InventoryManager inventoryManager;
    private const string SELECTED_CHARACTER_SUFFIX = "_SelectedCharacter";

    private void OnEnable()
    {
        // Update display when pause panel is shown
        UpdateAllDisplays();
    }

    private void Start()
    {
        // Find Timer and InventoryManager
        timerScript = FindObjectOfType<GameTimer>();
        inventoryManager = InventoryManager.Instance;

        // Get Image component from progressFillBar if not already assigned
        if (progressFillBar != null && progressFillBarImage == null)
        {
            progressFillBarImage = progressFillBar.GetComponent<Image>();
        }

        // Initial update
        UpdateAllDisplays();
    }

    private void UpdateAllDisplays()
    {
        UpdateAvatarDisplay();
        UpdateWeightDisplay();
        UpdateItemsDisplay();
        UpdateTimeDisplay();
    }

    private void UpdateAvatarDisplay()
    {
        if (avatarImage == null)
        {
            return;
        }

        // Get player gender from PlayerPrefs
        bool isGuest = PlayerPrefs.GetString("IsGuest", "false") == "true";
        string userName = isGuest ? "Guest" : PlayerPrefs.GetString("StudentName", "User");
        string userKey = userName + SELECTED_CHARACTER_SUFFIX;
        string selectedCharacter = PlayerPrefs.GetString(userKey, "Female");

        // Set avatar sprite based on selection
        if (selectedCharacter == "Male")
        {
            avatarImage.sprite = maleAvatarSprite;
        }
        else
        {
            avatarImage.sprite = femaleAvatarSprite;
        }
    }

    private void UpdateWeightDisplay()
    {
        if (weightValue == null)
        {
            return;
        }

        if (inventoryManager == null)
        {
            weightValue.text = "0kg/0kg";
            return;
        }

        // Get current weight and limit
        float currentWeight = inventoryManager.GetGoBagTotalWeight();
        float weightLimit = inventoryManager.GetGoBagWeightLimit();

        // Format as Xkg/Ykg (floor the current weight so it doesn't round up)
        int displayCurrentWeight = Mathf.FloorToInt(currentWeight);
        int displayWeightLimit = Mathf.FloorToInt(weightLimit);
        string weightText = string.Format("{0}kg/{1}kg", displayCurrentWeight, displayWeightLimit);
        weightValue.text = weightText;

        // Update progress bar
        UpdateProgressBar(currentWeight, weightLimit);
    }

    private void UpdateProgressBar(float currentWeight, float weightLimit)
    {
        if (progressFillBar == null || progressBarBackground == null || progressFillBarImage == null)
            return;

        // Calculate fill percentage (0-1)
        float fillPercentage = 0f;
        if (weightLimit > 0)
        {
            fillPercentage = Mathf.Clamp01(currentWeight / weightLimit);
        }

        // Get the parent (background) width
        RectTransform backgroundRect = progressBarBackground.GetComponent<RectTransform>();
        if (backgroundRect == null)
            return;

        // Calculate the fill bar width based on background width
        float backgroundWidth = backgroundRect.rect.width;
        float fillWidth = backgroundWidth * fillPercentage;

        // Set the fill bar width directly
        Vector2 sizeDelta = progressFillBar.sizeDelta;
        sizeDelta.x = fillWidth;
        progressFillBar.sizeDelta = sizeDelta;

        // Update color based on fill percentage
        progressFillBarImage.color = GetColorForFillPercentage(fillPercentage);
    }

    private Color GetColorForFillPercentage(float fillPercentage)
    {
        if (fillPercentage >= criticalThreshold)
        {
            // Red when critical (95%+)
            return Color.Lerp(warningColor, fullColor, (fillPercentage - criticalThreshold) / (1f - criticalThreshold));
        }
        else if (fillPercentage >= warningThreshold)
        {
            // Yellow when warning (75%-95%)
            return Color.Lerp(emptyColor, warningColor, (fillPercentage - warningThreshold) / (criticalThreshold - warningThreshold));
        }
        else
        {
            // Green when normal (0%-75%)
            return emptyColor;
        }
    }

    private void UpdateItemsDisplay()
    {
        if (itemsValue == null)
        {
            return;
        }

        if (inventoryManager == null)
        {
            itemsValue.text = "0";
            return;
        }

        // Count total unique items in all GoBag sections (excluding storage)
        int totalItems = 0;
        System.Collections.Generic.HashSet<InventoryItem> allItems = new System.Collections.Generic.HashSet<InventoryItem>();

        // Access the sections through reflection
        var sectionsField = inventoryManager.GetType()
            .GetField("sections", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (sectionsField != null)
        {
            var sections = sectionsField.GetValue(inventoryManager) as InventoryManager.InventorySection[];
            
            if (sections != null)
            {
                foreach (var section in sections)
                {
                    // Skip storage and refrigerator compartments
                    if (section.sectionName.Contains("Storage") || section.sectionName.Contains("Refrigerator"))
                        continue;

                    // Get all items from this section's grid
                    if (section.grid != null)
                    {
                        var items = section.grid.GetAllItems();
                        foreach (var item in items)
                        {
                            allItems.Add(item);
                        }
                    }
                }
            }
        }

        totalItems = allItems.Count;
        itemsValue.text = totalItems.ToString();
    }

    private void UpdateTimeDisplay()
    {
        if (timeValue == null)
        {
            return;
        }

        if (timerScript == null)
        {
            timeValue.text = "0/0";
            return;
        }

        float remainingTime = timerScript.GetTimeRemaining();
        float totalTime = timerScript.GetTotalTime();

        // Convert to minutes only (round down)
        int remainingMinutes = Mathf.FloorToInt(remainingTime / 60f);
        int totalMinutes = Mathf.FloorToInt(totalTime / 60f);

        // Format as Xmin/Ymin
        string timeText = string.Format("{0}min/{1}min", remainingMinutes, totalMinutes);
        timeValue.text = timeText;
    }
}

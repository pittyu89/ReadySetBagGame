using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// Manages the bag button visual feedback with a progress bar showing inventory capacity.
/// Displays a linear border outline based on GoBag weight usage and shows percentage text.
/// </summary>
public class BagButtonProgressBar : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image backgroundImage;  // White square background
    [SerializeField] private Image borderImage;      // Green border that fills around the square
    [SerializeField] private TextMeshProUGUI percentageText;  // Text showing capacity percentage

    [Header("Settings")]
    [SerializeField] private Color emptyColor = Color.green;
    [SerializeField] private Color warningColor = new Color(1f, 0.843f, 0f);  // Yellow
    [SerializeField] private Color fullColor = Color.red;
    [SerializeField] private float warningThreshold = 0.75f;  // 75% capacity
    [SerializeField] private float criticalThreshold = 0.95f; // 95% capacity

    [Header("Animation")]
    [SerializeField] private float animationDuration = 1f;  // Duration of fill animation in seconds

    private float currentFillPercentage = 0f;
    private float targetFillPercentage = 0f;
    private Coroutine animationCoroutine;

    void Start()
    {
        // Validate references
        if (backgroundImage == null)
            backgroundImage = GetComponent<Image>();

        if (percentageText == null)
            percentageText = GetComponentInChildren<TextMeshProUGUI>();

        // Subscribe to inventory changes
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.OnInventoryChanged += UpdateProgressBar;
        }

        // Initial update
        UpdateProgressBar();
    }

    void OnDestroy()
    {
        // Unsubscribe from inventory changes
        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.OnInventoryChanged -= UpdateProgressBar;
        }
    }

    public void UpdateProgressBar()
    {
        if (InventoryManager.Instance == null)
            return;

        float currentWeight = InventoryManager.Instance.GetGoBagTotalWeight();
        float weightLimit = InventoryManager.Instance.GetGoBagWeightLimit();

        // Calculate fill percentage (0-1)
        if (weightLimit > 0)
        {
            targetFillPercentage = Mathf.Clamp01(currentWeight / weightLimit);
        }
        else
        {
            targetFillPercentage = 0f; // No limit set
        }

        // The button is hidden while the inventory is open; OnEnable animates to the target instead
        if (!isActiveAndEnabled)
            return;

        // Stop any existing animation and start a new one
        if (animationCoroutine != null)
        {
            StopCoroutine(animationCoroutine);
        }
        animationCoroutine = StartCoroutine(AnimateFillAmount(targetFillPercentage));
    }

    void OnEnable()
    {
        // Catch up on weight changes made while the button was hidden
        if (!Mathf.Approximately(currentFillPercentage, targetFillPercentage))
            animationCoroutine = StartCoroutine(AnimateFillAmount(targetFillPercentage));
    }

    private IEnumerator AnimateFillAmount(float targetFill)
    {
        float elapsedTime = 0f;
        float startFill = currentFillPercentage;

        while (elapsedTime < animationDuration)
        {
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / animationDuration;
            
            // Smooth interpolation using easing
            progress = Mathf.SmoothStep(0f, 1f, progress);
            
            currentFillPercentage = Mathf.Lerp(startFill, targetFill, progress);

            // Update border fill and color
            if (borderImage != null)
            {
                borderImage.fillAmount = currentFillPercentage;
                borderImage.color = GetColorForFillPercentage(currentFillPercentage);
            }

            // Update percentage text
            if (percentageText != null)
            {
                int percentageValue = Mathf.FloorToInt(currentFillPercentage * 100f);
                percentageText.text = $"{percentageValue}%";
            }

            yield return null;
        }

        // Ensure final values are set precisely
        currentFillPercentage = targetFill;
        if (borderImage != null)
        {
            borderImage.fillAmount = currentFillPercentage;
            borderImage.color = GetColorForFillPercentage(currentFillPercentage);
        }

        if (percentageText != null)
        {
            int percentageValue = Mathf.FloorToInt(currentFillPercentage * 100f);
            percentageText.text = $"{percentageValue}%";
        }
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

    public float GetCurrentFillPercentage()
    {
        return currentFillPercentage;
    }
}

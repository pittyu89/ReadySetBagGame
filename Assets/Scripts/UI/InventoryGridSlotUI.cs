using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Represents a single slot in the inventory grid UI.
/// </summary>
public class InventoryGridSlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private int gridX;
    private int gridY;
    private string sectionName;
    private Image slotImage;
    private Color originalColor;

    public void Initialize(int x, int y, string section)
    {
        gridX = x;
        gridY = y;
        sectionName = section;
        
        slotImage = GetComponent<Image>();
        if (slotImage != null)
        {
            originalColor = slotImage.color;
        }
    }

    public int GetGridX() => gridX;
    public int GetGridY() => gridY;
    public string GetSectionName() => sectionName;

    public void OnPointerEnter(PointerEventData eventData)
    {
        // Apply a brighter tint when hovering to show selection while keeping sprite visible
        if (slotImage != null)
        {
            slotImage.color = originalColor * 1.3f;
            slotImage.color = new Color(slotImage.color.r, slotImage.color.g, slotImage.color.b, originalColor.a);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // Restore original color when not hovering
        if (slotImage != null)
        {
            slotImage.color = originalColor;
        }
    }

    public void UpdateOriginalColor()
    {
        // Update the stored original color when the sprite changes
        if (slotImage != null)
        {
            originalColor = slotImage.color;
        }
    }
}

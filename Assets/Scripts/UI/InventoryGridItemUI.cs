using UnityEngine;

/// <summary>
/// Represents an item displayed in the inventory grid UI.
/// </summary>
public class InventoryGridItemUI : MonoBehaviour
{
    private InventoryItem item;
    private int gridX;
    private int gridY;
    private string sectionName;

    public void Initialize(InventoryItem itemData, int x, int y, string section)
    {
        item = itemData;
        gridX = x;
        gridY = y;
        sectionName = section;
    }

    public InventoryItem GetItem() => item;
    public int GetGridX() => gridX;
    public int GetGridY() => gridY;
    public string GetSectionName() => sectionName;
}

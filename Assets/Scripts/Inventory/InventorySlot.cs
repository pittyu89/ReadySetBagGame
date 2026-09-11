using UnityEngine;

/// <summary>
/// Represents a single slot in the inventory grid.
/// Tracks which item occupies this slot and its relative position within that item.
/// </summary>
public class InventorySlot
{
    public InventoryItem item;
    public int itemGridX;  // Position within the item's grid (0 if top-left of item)
    public int itemGridY;  // Position within the item's grid (0 if top-left of item)
    public bool isOccupied;

    public InventorySlot()
    {
        item = null;
        itemGridX = 0;
        itemGridY = 0;
        isOccupied = false;
    }

    public void Clear()
    {
        item = null;
        itemGridX = 0;
        itemGridY = 0;
        isOccupied = false;
    }
}

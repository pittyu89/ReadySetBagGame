using UnityEngine;

/// <summary>
/// Represents a 2D grid-based inventory section (e.g., one compartment of the bag).
/// Handles placement, removal, and validation of items with variable dimensions.
/// </summary>
public class InventoryGrid
{
    public int gridWidth;
    public int gridHeight;
    public float maxWeightKg;  // Weight limit for this grid (0 = unlimited)
    private InventorySlot[,] slots;

    public InventoryGrid(int width, int height, float weightLimit = 0f)
    {
        gridWidth = width;
        gridHeight = height;
        maxWeightKg = weightLimit;
        slots = new InventorySlot[width, height];

        // Initialize all slots
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                slots[x, y] = new InventorySlot();
            }
        }
    }

    /// <summary>
    /// Checks if an item can be placed at the specified position.
    /// </summary>
    public bool CanPlaceItem(int gridX, int gridY, InventoryItem item)
    {
        // Check if position is within grid bounds
        if (gridX + item.width > gridWidth || gridY + item.height > gridHeight)
            return false;

        if (gridX < 0 || gridY < 0)
            return false;

        // Check if all required slots are empty
        for (int x = gridX; x < gridX + item.width; x++)
        {
            for (int y = gridY; y < gridY + item.height; y++)
            {
                if (slots[x, y].isOccupied)
                    return false;
            }
        }

        // Check weight limit
        if (!CanAddItemByWeight(item))
            return false;

        return true;
    }

    /// <summary>
    /// Places an item at the specified grid position.
    /// Returns true if successful, false otherwise.
    /// </summary>
    public bool PlaceItem(int gridX, int gridY, InventoryItem item)
    {
        if (!CanPlaceItem(gridX, gridY, item))
            return false;

        // Mark all slots as occupied by this item
        for (int x = gridX; x < gridX + item.width; x++)
        {
            for (int y = gridY; y < gridY + item.height; y++)
            {
                slots[x, y].item = item;
                slots[x, y].itemGridX = x - gridX;  // Relative position within item
                slots[x, y].itemGridY = y - gridY;
                slots[x, y].isOccupied = true;
            }
        }

        return true;
    }

    /// <summary>
    /// Removes an item from the grid. Returns the item if found, null otherwise.
    /// </summary>
    public InventoryItem RemoveItem(int gridX, int gridY)
    {
        if (gridX < 0 || gridX >= gridWidth || gridY < 0 || gridY >= gridHeight)
            return null;

        InventorySlot slot = slots[gridX, gridY];
        if (!slot.isOccupied || slot.item == null)
            return null;

        InventoryItem item = slot.item;

        // Clear all slots occupied by this item
        for (int x = gridX - slot.itemGridX; x < gridX - slot.itemGridX + item.width; x++)
        {
            for (int y = gridY - slot.itemGridY; y < gridY - slot.itemGridY + item.height; y++)
            {
                if (x >= 0 && x < gridWidth && y >= 0 && y < gridHeight)
                {
                    slots[x, y].Clear();
                }
            }
        }

        return item;
    }

    /// <summary>
    /// Finds the first available position for an item.
    /// Returns true and sets outX, outY if space found.
    /// </summary>
    public bool FindSpaceForItem(InventoryItem item, out int outX, out int outY)
    {
        outX = -1;
        outY = -1;

        // Scan grid left to right, top to bottom
        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (CanPlaceItem(x, y, item))
                {
                    outX = x;
                    outY = y;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the item at the specified grid position.
    /// </summary>
    public InventoryItem GetItemAt(int gridX, int gridY)
    {
        if (gridX < 0 || gridX >= gridWidth || gridY < 0 || gridY >= gridHeight)
            return null;

        return slots[gridX, gridY].item;
    }

    /// <summary>
    /// Gets the slot at the specified grid position.
    /// </summary>
    public InventorySlot GetSlotAt(int gridX, int gridY)
    {
        if (gridX < 0 || gridX >= gridWidth || gridY < 0 || gridY >= gridHeight)
            return null;

        return slots[gridX, gridY];
    }

    /// <summary>
    /// Returns all unique items currently in the grid.
    /// </summary>
    public InventoryItem[] GetAllItems()
    {
        System.Collections.Generic.List<InventoryItem> items = new System.Collections.Generic.List<InventoryItem>();
        System.Collections.Generic.HashSet<InventoryItem> uniqueItems = new System.Collections.Generic.HashSet<InventoryItem>();

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                if (slots[x, y].isOccupied && slots[x, y].item != null)
                {
                    uniqueItems.Add(slots[x, y].item);
                }
            }
        }

        return new System.Collections.Generic.List<InventoryItem>(uniqueItems).ToArray();
    }

    /// <summary>
    /// Clears all items from the grid.
    /// </summary>
    public void Clear()
    {
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                slots[x, y].Clear();
            }
        }
    }

    /// <summary>
    /// Gets the width of the grid.
    /// </summary>
    public int GetWidth()
    {
        return gridWidth;
    }

    /// <summary>
    /// Gets the height of the grid.
    /// </summary>
    public int GetHeight()
    {
        return gridHeight;
    }

    /// <summary>
    /// Calculates the total weight of all items in the grid.
    /// </summary>
    public float GetTotalWeight()
    {
        float totalWeight = 0f;
        System.Collections.Generic.HashSet<InventoryItem> countedItems = new System.Collections.Generic.HashSet<InventoryItem>();

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                if (slots[x, y].isOccupied && slots[x, y].item != null)
                {
                    // Only count each unique item once (don't count multi-tile items multiple times)
                    if (!countedItems.Contains(slots[x, y].item))
                    {
                        totalWeight += slots[x, y].item.weightKg * slots[x, y].item.quantity;
                        countedItems.Add(slots[x, y].item);
                    }
                }
            }
        }

        return totalWeight;
    }

    /// <summary>
    /// Checks if adding an item would exceed the weight limit.
    /// Returns true if the item can be added (doesn't exceed limit).
    /// </summary>
    public bool CanAddItemByWeight(InventoryItem item)
    {
        // No weight limit set
        if (maxWeightKg <= 0f)
            return true;

        float currentWeight = GetTotalWeight();
        float itemWeight = item.weightKg * item.quantity;
        
        return (currentWeight + itemWeight) <= maxWeightKg;
    }
}

using UnityEngine;

public class InventoryItem
{
    public string itemId;
    public string itemName;
    public Sprite itemSprite;
    public string description;
    
    // Grid dimensions in tiles
    public int width;  // Horizontal tiles
    public int height; // Vertical tiles
    
    // Stack properties
    public int quantity;
    public int maxStackSize;
    public bool isStackable;
    
    // Weight in kilograms
    public float weightKg;
    
    // Item categorization
    public ItemCategory category;

    /// <summary>
    /// The item data this item was made from. Scoring and the Journal read importance and
    /// essentials from here, rather than matching the display name back to its data.
    /// </summary>
    public SupplyItem source;

    /// <summary>A single unit of a supply item, ready to place in a grid.</summary>
    public static InventoryItem FromSupply(SupplyItem supply)
    {
        return new InventoryItem(
            supply.ItemName.ToLower().Replace(" ", "_"),
            supply.ItemName,
            supply.ItemImage,
            supply.GridWidth,
            supply.GridHeight,
            ItemCategory.Misc,
            supply.WeightKg)
        {
            description = supply.Description,
            source = supply
        };
    }

    public InventoryItem(string id, string name, Sprite sprite, int w, int h, ItemCategory cat, float weight = 0f, bool stackable = false, int maxStack = 1)
    {
        itemId = id;
        itemName = name;
        itemSprite = sprite;
        width = w;
        height = h;
        category = cat;
        weightKg = weight;
        isStackable = stackable;
        maxStackSize = maxStack;
        quantity = 1;
        description = "";
    }

    public InventoryItem Clone()
    {
        return new InventoryItem(itemId, itemName, itemSprite, width, height, category, weightKg, isStackable, maxStackSize)
        {
            quantity = this.quantity,
            description = this.description,
            source = this.source
        };
    }
}

public enum ItemCategory
{
    Document,
    Clothing,
    Medical,
    Electronics,
    Food,
    Utility,
    Quest,
    Misc
}

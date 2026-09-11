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
            description = this.description
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

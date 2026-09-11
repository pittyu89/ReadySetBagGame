using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages the overall inventory system with multiple grid sections.
/// Handles item placement across compartments and provides a unified interface.
/// </summary>
public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance { get; private set; }

    [System.Serializable]
    public class InventorySection
    {
        public string sectionName;
        public int gridWidth;
        public int gridHeight;
        public float maxWeightKg;  // Weight limit in kilograms (0 = unlimited)
        [HideInInspector] public InventoryGrid grid;
    }

    [SerializeField] private InventorySection[] sections;
    [SerializeField] private Sprite emptySlotSprite;
    [SerializeField] private Sprite occupiedSlotSprite;
    [SerializeField] public AudioClip itemPlacedInBagAudio;
    
    private Dictionary<string, InventoryGrid> gridMap;
    private float globalGoBagWeightLimit = 0f;  // Total weight limit for all GoBag sections (0 = unlimited)
    
    // Events
    public delegate void InventoryChanged();
    public event InventoryChanged OnInventoryChanged;

    public delegate void ItemAdded(InventoryItem item, string section);
    public event ItemAdded OnItemAdded;

    public delegate void ItemRemoved(InventoryItem item, string section);
    public event ItemRemoved OnItemRemoved;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            InitializeGrids();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void InitializeGrids()
    {
        gridMap = new Dictionary<string, InventoryGrid>();

        if (sections != null)
        {
            foreach (var section in sections)
            {
                section.grid = new InventoryGrid(section.gridWidth, section.gridHeight, section.maxWeightKg);
                gridMap[section.sectionName] = section.grid;
            }
        }
    }

    /// <summary>
    /// Attempts to add an item to the specified inventory section.
    /// If no section is specified, tries all sections in order.
    /// </summary>
    public bool AddItem(InventoryItem item, string sectionName = null)
    {
        if (sectionName != null)
        {
            if (gridMap.ContainsKey(sectionName))
            {
                if (TryAddToSection(item, sectionName))
                    return true;
            }
        }
        else
        {
            // Try to add to each section in order
            foreach (var section in sections)
            {
                if (TryAddToSection(item, section.sectionName))
                    return true;
            }
        }

        return false;
    }

    private bool TryAddToSection(InventoryItem item, string sectionName)
    {
        InventoryGrid grid = gridMap[sectionName];

        if (grid.FindSpaceForItem(item, out int x, out int y))
        {
            grid.PlaceItem(x, y, item);
            OnItemAdded?.Invoke(item, sectionName);
            OnInventoryChanged?.Invoke();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Removes an item from the specified section at the given grid position.
    /// </summary>
    public InventoryItem RemoveItem(int gridX, int gridY, string sectionName)
    {
        if (!gridMap.ContainsKey(sectionName))
            return null;

        InventoryItem removedItem = gridMap[sectionName].RemoveItem(gridX, gridY);
        if (removedItem != null)
        {
            OnItemRemoved?.Invoke(removedItem, sectionName);
            OnInventoryChanged?.Invoke();
        }

        return removedItem;
    }

    /// <summary>
    /// Gets all items in a specific section.
    /// </summary>
    public InventoryItem[] GetSectionItems(string sectionName)
    {
        if (gridMap.ContainsKey(sectionName))
            return gridMap[sectionName].GetAllItems();

        return new InventoryItem[0];
    }

    /// <summary>
    /// Gets the grid for a specific section.
    /// </summary>
    public InventoryGrid GetGrid(string sectionName)
    {
        if (gridMap.ContainsKey(sectionName))
            return gridMap[sectionName];

        return null;
    }

    /// <summary>
    /// Gets all available section names.
    /// </summary>
    public string[] GetAllSectionNames()
    {
        List<string> names = new List<string>(gridMap.Keys);
        return names.ToArray();
    }

    /// <summary>
    /// Finds which section and position an item is located at.
    /// </summary>
    public bool FindItem(InventoryItem item, out string sectionName, out int gridX, out int gridY)
    {
        foreach (var kvp in gridMap)
        {
            InventoryItem[] items = kvp.Value.GetAllItems();
            foreach (var inv_item in items)
            {
                if (inv_item == item)
                {
                    sectionName = kvp.Key;
                    // Find the top-left position of the item
                    for (int y = 0; y < kvp.Value.gridHeight; y++)
                    {
                        for (int x = 0; x < kvp.Value.gridWidth; x++)
                        {
                            InventorySlot slot = kvp.Value.GetSlotAt(x, y);
                            if (slot != null && slot.item == item && slot.itemGridX == 0 && slot.itemGridY == 0)
                            {
                                gridX = x;
                                gridY = y;
                                return true;
                            }
                        }
                    }
                }
            }
        }

        sectionName = null;
        gridX = -1;
        gridY = -1;
        return false;
    }

    /// <summary>
    /// Gets total item count (for all stackable items, sums quantities).
    /// </summary>
    public int GetTotalItemCount()
    {
        int count = 0;
        foreach (var grid in gridMap.Values)
        {
            var items = grid.GetAllItems();
            foreach (var item in items)
            {
                count += item.quantity;
            }
        }
        return count;
    }

    /// <summary>
    /// Clears all items from all sections.
    /// </summary>
    public void ClearInventory()
    {
        foreach (var grid in gridMap.Values)
        {
            grid.Clear();
        }
        OnInventoryChanged?.Invoke();
    }

    /// <summary>
    /// Gets the sprite for empty slots.
    /// </summary>
    public Sprite GetEmptySlotSprite()
    {
        return emptySlotSprite;
    }

    /// <summary>
    /// Gets the sprite for occupied slots.
    /// </summary>
    public Sprite GetOccupiedSlotSprite()
    {
        return occupiedSlotSprite;
    }

    /// <summary>
    /// Gets the total weight of items in a specific section.
    /// </summary>
    public float GetSectionWeight(string sectionName)
    {
        if (gridMap.ContainsKey(sectionName))
            return gridMap[sectionName].GetTotalWeight();

        return 0f;
    }

    /// <summary>
    /// Gets the weight limit for a specific section.
    /// </summary>
    public float GetSectionWeightLimit(string sectionName)
    {
        if (gridMap.ContainsKey(sectionName))
            return gridMap[sectionName].maxWeightKg;

        return 0f;
    }

    /// <summary>
    /// Gets the total weight of all items in the inventory.
    /// </summary>
    public float GetTotalInventoryWeight()
    {
        float totalWeight = 0f;
        foreach (var grid in gridMap.Values)
        {
            totalWeight += grid.GetTotalWeight();
        }
        return totalWeight;
    }

    /// <summary>
    /// Gets the total weight of all items in GoBag sections (excludes storage compartments).
    /// </summary>
    public float GetGoBagTotalWeight()
    {
        float totalWeight = 0f;
        foreach (var kvp in gridMap)
        {
            // Only count GoBag sections, not storage (Refrigerator, etc.)
            if (!kvp.Key.Contains("Refrigerator") && !kvp.Key.Contains("Storage"))
            {
                totalWeight += kvp.Value.GetTotalWeight();
            }
        }
        return totalWeight;
    }

    /// <summary>
    /// Every item currently in the go bag, ignoring storage compartments — the same sections
    /// <see cref="GetGoBagTotalWeight"/> counts. Scoring uses this to work out what was
    /// actually packed when the drill ends.
    /// </summary>
    public List<InventoryItem> GetGoBagItems()
    {
        List<InventoryItem> packed = new List<InventoryItem>();
        foreach (var kvp in gridMap)
        {
            if (kvp.Key.Contains("Refrigerator") || kvp.Key.Contains("Storage"))
                continue;

            InventoryItem[] items = kvp.Value.GetAllItems();
            if (items != null)
                packed.AddRange(items);
        }
        return packed;
    }

    /// <summary>
    /// Checks if adding an item to a GoBag section would exceed the global weight limit.
    /// </summary>
    public bool CanAddItemToGoBag(InventoryItem item)
    {
        // No global weight limit set
        if (globalGoBagWeightLimit <= 0f)
            return true;

        float currentGoBagWeight = GetGoBagTotalWeight();
        float itemWeight = item.weightKg * item.quantity;

        return (currentGoBagWeight + itemWeight) <= globalGoBagWeightLimit;
    }

    /// <summary>
    /// Sets the global weight limit for all GoBag compartments combined.
    /// </summary>
    public void SetGoBagWeightLimit(float weightLimit)
    {
        globalGoBagWeightLimit = weightLimit;

    }

    /// <summary>
    /// Gets the current global GoBag weight limit.
    /// </summary>
    public float GetGoBagWeightLimit()
    {
        return globalGoBagWeightLimit;
    }

    /// <summary>
    /// Manually invokes the OnInventoryChanged event for UI updates.
    /// Used by drag handlers that directly modify grids without going through AddItem/RemoveItem.
    /// </summary>
    public void InvokeInventoryChanged()
    {
        OnInventoryChanged?.Invoke();
    }
}


using UnityEngine;
using System.Collections.Generic;

public class ClickableModel : MonoBehaviour
{
    [System.Serializable]
    public class StorageSection
    {
        public string sectionName;
        public int gridWidth;
        public int gridHeight;
        [HideInInspector] public InventoryGrid grid;
    }

    [Header("Display")]
    [SerializeField] private Sprite modelSprite;
    [SerializeField] private float modelDisplayWidth = 200f;
    [SerializeField] private float modelDisplayHeight = 200f;

    [Header("Storage Sections")]
    [SerializeField] private StorageSection[] storageSections;

    [Header("Starting Items")]
    [SerializeField] private SupplyItemStack[] supplyItems;

    private Dictionary<string, InventoryGrid> storageGridMap;
    private bool isInitialized = false;
    private Collider cachedCollider;
    private bool colliderCached = false;

    void Start()
    {
        InitializeStorage();
    }

    /// <summary>
    /// The collider used for reach tests. Cached on first use rather than in Awake so it is
    /// safe to call from another component's Awake, whatever order Unity runs them in.
    /// </summary>
    private Collider GetReachCollider()
    {
        if (!colliderCached)
        {
            cachedCollider = GetComponent<Collider>();
            if (cachedCollider == null)
                cachedCollider = GetComponentInChildren<Collider>();
            colliderCached = true;
        }

        return cachedCollider;
    }

    /// <summary>
    /// Flat (X/Z) distance from a world position to this model's collider surface.
    ///
    /// Measured to the surface rather than the pivot. Cabinets and shelves are wide and their
    /// pivot often sits at the centre or a base corner, so a pivot-based check can report a
    /// prop as metres away while the player is standing flush against it - which reads to the
    /// player as the object simply not being clickable. Same reasoning as DoorProximityHandler.
    /// </summary>
    public float GetPlanarDistanceTo(Vector3 worldPosition)
    {
        Vector3 measureFrom = transform.position;

        Collider reachCollider = GetReachCollider();
        if (reachCollider != null)
            measureFrom = reachCollider.bounds.ClosestPoint(worldPosition);

        return Vector2.Distance(
            new Vector2(worldPosition.x, worldPosition.z),
            new Vector2(measureFrom.x, measureFrom.z)
        );
    }

    /// <summary>
    /// Whether a player at the given position can reach this model.
    ///
    /// Shared by ModelClickHandler and ClickableHighlightManager on purpose: if the highlight
    /// and the click used different tests, props would glow without opening, which is more
    /// confusing than no highlight at all.
    /// </summary>
    public bool IsWithinReach(Vector3 worldPosition, float maxDistance, float verticalReach)
    {
        // Reject props on another floor before the flat distance check, which cannot tell them
        // apart: upstairs furniture sits directly above the ground-floor furniture.
        if (Mathf.Abs(worldPosition.y - transform.position.y) > verticalReach)
            return false;

        return GetPlanarDistanceTo(worldPosition) <= maxDistance;
    }

    private void InitializeStorage()
    {
        if (isInitialized)
            return;

        storageGridMap = new Dictionary<string, InventoryGrid>();

        // Create grids for each storage section using section-specific dimensions
        if (storageSections != null)
        {
            foreach (var section in storageSections)
            {
                section.grid = new InventoryGrid(section.gridWidth, section.gridHeight);
                storageGridMap[section.sectionName] = section.grid;
            }
        }

        // Add starting items to first available section with space
        if (supplyItems != null && supplyItems.Length > 0 && storageSections != null && storageSections.Length > 0)
        {
            foreach (var stack in supplyItems)
            {
                if (stack != null && stack.supplyItem != null)
                {
                    // Create inventory items from supply item - one per count
                    for (int i = 0; i < stack.count; i++)
                    {
                        InventoryItem item = new InventoryItem(
                            stack.supplyItem.ItemName.ToLower().Replace(" ", "_"),
                            stack.supplyItem.ItemName,
                            stack.supplyItem.ItemImage,
                            stack.supplyItem.GridWidth,
                            stack.supplyItem.GridHeight,
                            ItemCategory.Misc,
                            stack.supplyItem.WeightKg
                        );
                        item.quantity = 1;
                        item.description = stack.supplyItem.Description;  // Copy description from SupplyItem

                        bool placed = false;

                        // Try to place in each section until one has space
                        foreach (var section in storageSections)
                        {
                            InventoryGrid grid = storageGridMap[section.sectionName];
                            if (grid.FindSpaceForItem(item, out int x, out int y))
                            {
                                grid.PlaceItem(x, y, item);
                                placed = true;
                                break;
                            }
                        }

                        if (!placed)
                        {
                            // Item could not be placed in any section
                        }
                    }
                }
            }
        }

        isInitialized = true;
    }

    public Sprite GetModelSprite()
    {
        return modelSprite;
    }

    /// <summary>
    /// Gets the storage grid for a specific section.
    /// If no section name provided, returns the first section.
    /// </summary>
    public InventoryGrid GetStorageGrid(string sectionName = null)
    {
        if (!isInitialized)
            InitializeStorage();

        if (sectionName == null && storageSections != null && storageSections.Length > 0)
            sectionName = storageSections[0].sectionName;

        if (sectionName != null && storageGridMap.ContainsKey(sectionName))
            return storageGridMap[sectionName];

        return null;
    }

    /// <summary>
    /// Gets all items in a specific storage section.
    /// If no section name provided, returns items from first section.
    /// </summary>
    public InventoryItem[] GetStorageItems(string sectionName = null)
    {
        InventoryGrid grid = GetStorageGrid(sectionName);
        if (grid != null)
            return grid.GetAllItems();

        return new InventoryItem[0];
    }

    /// <summary>
    /// Gets all storage section names.
    /// </summary>
    public string[] GetAllStorageSectionNames()
    {
        if (!isInitialized)
            InitializeStorage();

        return new List<string>(storageGridMap.Keys).ToArray();
    }

    /// <summary>
    /// Get the display width of the model sprite.
    /// </summary>
    public float GetModelDisplayWidth()
    {
        return modelDisplayWidth;
    }

    /// <summary>
    /// Get the display height of the model sprite.
    /// </summary>
    public float GetModelDisplayHeight()
    {
        return modelDisplayHeight;
    }
}

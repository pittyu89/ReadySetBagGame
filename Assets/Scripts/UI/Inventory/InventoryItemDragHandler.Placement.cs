using UnityEngine;

/// <summary>
/// Where a dropped item lands: snapping it to a free spot in the grid it was dropped on, moving it
/// between grids, and the go bag's weight limit.
/// </summary>
public partial class InventoryItemDragHandler
{
    private bool TryMoveItemWithinGridSnapped(InventoryItem item, InventoryGridDisplay gridDisplay)
    {
        InventoryGrid grid = gridDisplay != null ? gridDisplay.GetCurrentGrid() : null;
        if (item == null || grid == null || !TryFindItemOrigin(grid, item, out Vector2Int old))
            return false;

        // Lift the item out first so CanPlaceItem doesn't see it occupying its own cells
        grid.RemoveItem(old.x, old.y);

        if (!TryFindPlacement(grid, item, GetCellUnderItem(gridDisplay, grid, item), out Vector2Int target)
            || !CanPlaceItemWithGlobalWeightCheck(item, gridDisplay))
        {
            grid.PlaceItem(old.x, old.y, item);
            return false;
        }

        grid.PlaceItem(target.x, target.y, item);
        OnItemPlaced();
        return true;
    }

    private bool TryMoveItemBetweenGridsSnapped(InventoryItem item, InventoryGridDisplay sourceGridDisplay, InventoryGridDisplay targetGridDisplay)
    {
        InventoryGrid sourceGrid = sourceGridDisplay != null ? sourceGridDisplay.GetCurrentGrid() : null;
        InventoryGrid targetGrid = targetGridDisplay != null ? targetGridDisplay.GetCurrentGrid() : null;
        if (item == null || sourceGrid == null || targetGrid == null || !TryFindItemOrigin(sourceGrid, item, out Vector2Int old))
            return false;

        // Lift the item out of the source first so the target grid's checks aren't confused by it
        sourceGrid.RemoveItem(old.x, old.y);

        if (!TryFindPlacement(targetGrid, item, GetCellUnderItem(targetGridDisplay, targetGrid, item), out Vector2Int target)
            || !CanPlaceItemWithGlobalWeightCheck(item, targetGridDisplay))
        {
            sourceGrid.PlaceItem(old.x, old.y, item);
            return false;
        }

        targetGrid.PlaceItem(target.x, target.y, item);
        OnItemPlaced();
        return true;
    }

    /// <summary>The cell holding the item's top-left corner, where the grid stores it.</summary>
    private static bool TryFindItemOrigin(InventoryGrid grid, InventoryItem item, out Vector2Int origin)
    {
        for (int y = 0; y < grid.GetHeight(); y++)
        {
            for (int x = 0; x < grid.GetWidth(); x++)
            {
                if (grid.GetItemAt(x, y) != item)
                    continue;

                InventorySlot slot = grid.GetSlotAt(x, y);
                if (slot != null && slot.itemGridX == 0 && slot.itemGridY == 0)
                {
                    origin = new Vector2Int(x, y);
                    return true;
                }
            }
        }

        origin = default;
        return false;
    }

    /// <summary>
    /// Where the item goes: the cell it was dropped on if it fits there, otherwise the nearest
    /// cell that fits. False when it fits nowhere in the grid.
    /// </summary>
    private static bool TryFindPlacement(InventoryGrid grid, InventoryItem item, Vector2Int dropped, out Vector2Int placement)
    {
        placement = dropped;
        if (grid.CanPlaceItem(dropped.x, dropped.y, item))
            return true;

        float nearestDistance = float.MaxValue;
        bool found = false;

        for (int y = 0; y < grid.GetHeight(); y++)
        {
            for (int x = 0; x < grid.GetWidth(); x++)
            {
                if (!grid.CanPlaceItem(x, y, item))
                    continue;

                float distance = Vector2.Distance(dropped, new Vector2(x, y));
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    placement = new Vector2Int(x, y);
                    found = true;
                }
            }
        }

        return found;
    }

    private void OnItemPlaced()
    {
        PlayItemPlacedSFX();
        Haptics.Tap();

        // Progress bars and the like listen for this
        if (InventoryManager.Instance != null)
            InventoryManager.Instance.InvokeInventoryChanged();
    }

    /// <summary>
    /// Checks if an item can be placed in a grid, considering global GoBag weight limits.
    /// </summary>
    private bool CanPlaceItemWithGlobalWeightCheck(InventoryItem item, InventoryGridDisplay gridDisplay)
    {
        if (item == null || gridDisplay == null)
            return false;

        // Only the go bag has a weight limit. Moves into furniture - including rearranging a
        // shelf while the bag is nearly full - are always allowed.
        InventoryGrid target = gridDisplay.GetCurrentGrid();
        if (target == null || !target.isGoBag)
            return true;

        // The item has already been lifted out of wherever it came from, so the bag's weight
        // here is what it would be without it
        InventoryManager manager = InventoryManager.Instance;
        if (manager == null)
            return true;

        bool canAdd = manager.CanAddItemToGoBag(item);
        if (!canAdd)
        {
            Haptics.Fail();

            // Determine if it's a weight issue or go bag at capacity issue
            float currentWeight = manager.GetGoBagTotalWeight();
            float weightLimit = manager.GetGoBagWeightLimit();
            float remainingWeight = weightLimit - currentWeight;

            // If remaining weight is 0 or less, the bag is full
            if (remainingWeight <= 0)
            {
                if (inventoryPanelHandler != null)
                    inventoryPanelHandler.ShowGoBagFullMessage();
            }
            // If the item itself exceeds remaining weight, it's a weight limit issue
            else if (item.weightKg > remainingWeight)
            {
                if (inventoryPanelHandler != null)
                    inventoryPanelHandler.ShowWeightLimitMessage();
            }
        }
        return canAdd;
    }
}

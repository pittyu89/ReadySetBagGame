using UnityEngine;

/// <summary>
/// DRAG-AND-DROP INVENTORY SYSTEM SETUP GUIDE
/// 
/// This system allows items from clickable model storage to be dragged into the player's bag inventory.
/// 
/// ===== SETUP INSTRUCTIONS =====
/// 
/// 1. INVENTORY MANAGER SETUP
///    - Add an InventoryManager to a persistent GameObject in your scene
///    - In Inspector, configure sections for your bag compartments (e.g., "LeftCompartment", "RightCompartment")
///    - Set grid dimensions for each section (e.g., 6 width × 10 height)
/// 
/// 2. INVENTORY PANEL UI SETUP
///    - You should have an InventoryPanelHandler on your inventory panel
///    - The goBagSide should contain InventoryGridDisplay components for each bag compartment
///    - The storageSide will display the model's storage contents
/// 
/// 3. INVENTORY GRID DISPLAY SETUP
///    - On each UI grid element (left/right compartments):
///    - Add InventoryGridDisplay component
///    - Set Section Name to match your InventoryManager sections ("LeftCompartment", etc.)
///    - Set Grid Width/Height to match your bag dimensions
///    - Set Cell Size to your desired grid cell size (typically 50-60 pixels)
///    - Configure colors for empty/occupied slots
/// 
///    - For the storage side:
///    - Add InventoryGridDisplay component
///    - Leave Section Name empty (it will be set dynamically)
///    - Set Grid Width/Height to match the initial storage size
///    - This will be overridden when SetStorageModel is called
/// 
/// 4. CLICKABLE MODEL SETUP
///    - On each interactive object/model in your scene:
///    - Add ClickableModel component
///    - Set Item properties (ID, Name, Sprite, Dimensions, etc.)
///    - Set Storage Grid Width/Height (e.g., 6×8 for a storage chest)
///    - Optionally add Starting Items (items that spawn in the storage)
/// 
/// 5. MODEL CLICK HANDLER SETUP
///    - ModelClickHandler should be on a GameObject that raycasts for clicks
///    - It will automatically find InventoryManager and InventoryPanelHandler
///    - Set Auto Add To Inventory = true to collect single items automatically
///    - Set Destroy After Collection = true to remove the model object when item is collected
/// 
/// ===== USAGE FLOW =====
/// 
/// 1. Player clicks a ClickableModel
/// 2. ModelClickHandler detects the click and raycasts
/// 3. If autoAddToInventory is on and bag has space → item is added, model is destroyed
/// 4. If bag is full → OpenInventoryWithStorage is called
/// 5. Split-screen opens: Bag on left, Model's storage on right
/// 6. Player can now drag items from storage (right) to bag (left)
/// 7. Items are removed from storage grid and added to bag grid
/// 
/// ===== DRAG AND DROP SYSTEM =====
/// 
/// The drag system works by:
/// - InventoryItemDragHandler is auto-added to each item UI
/// - On drag start: Item becomes semi-transparent
/// - On drag: Item follows the mouse
/// - On drag end: System checks if dropped on a different grid
/// - If valid: Item is moved from source grid to target grid
/// - If invalid: Item returns to original position (automatic with RefreshDisplay)
/// 
/// ===== CREATING STORAGE ITEMS PROGRAMMATICALLY =====
/// 
/// To add items to a ClickableModel's storage in the inspector:
/// 1. Select the ClickableModel GameObject
/// 2. In Inspector, find "Storage" section
/// 3. Adjust "Starting Items" array size
/// 4. For each item, create an InventoryItem using the "Create Item" button
/// 
/// Alternative (code):
/// InventoryItem sword = new InventoryItem("sword_01", "Iron Sword", swordSprite, 1, 2, ItemCategory.Misc);
/// clickableModel.GetStorageGrid().PlaceItem(0, 0, sword);
/// 
/// ===== IMPORTANT NOTES =====
/// 
/// - Each ClickableModel has its own independent storage grid
/// - Items are stored by reference, so modifications affect the actual item
/// - The drag system handles multi-tile items correctly
/// - Stackable items show quantity in the UI corner
/// - Storage grids persist until the model is destroyed
/// - You can customize colors, cell sizes, and animations
/// 
/// </summary>
public class DragDropInventorySetup : MonoBehaviour
{
    // This script is documentation only - no logic needed
}

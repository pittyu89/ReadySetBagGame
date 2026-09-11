using UnityEngine;

/// <summary>
/// Complete grid-based inventory system with 5 separate compartments.
/// Shows how to configure the bag with dedicated sections for each panel.
/// 
/// ===== COMPARTMENT LAYOUT =====
/// 
/// Your bag has 5 independent compartments, each with its own storage:
/// 
/// TOP PANEL:    6 wide × 4 tall
/// LEFT PANEL:   2 wide × 4 tall    |    RIGHT PANEL:  2 wide × 4 tall
/// MIDDLE PANEL: 4 wide × 3 tall
/// BOTTOM PANEL: 4 wide × 2 tall
/// 
/// Each compartment stores items completely separately.
/// 
/// ===== QUICK SETUP (5 SECTIONS) =====
/// 
/// 1. ADD INVENTORY MANAGER
///    - Create a new GameObject called "InventoryManager"
///    - Attach InventoryManager component
///    - In Inspector, find the "Sections" field
///    - Set Size to 5 (five compartments)
///    - Set Empty Slot Sprite: Drag your empty slot image
///    - Set Occupied Slot Sprite: Drag your occupied slot image
///    (All panels will use these sprites automatically)
/// 
/// 2. CONFIGURE EACH SECTION
///    - Click on Element 0, configure:
///      * Section Name: "TopSection"
///      * Grid Width: 6
///      * Grid Height: 4
///    
///    - Click on Element 1:
///      * Section Name: "LeftSection"
///      * Grid Width: 2
///      * Grid Height: 4
///    
///    - Click on Element 2:
///      * Section Name: "RightSection"
///      * Grid Width: 2
///      * Grid Height: 4
///    
///    - Click on Element 3:
///      * Section Name: "MiddleSection"
///      * Grid Width: 4
///      * Grid Height: 3
///    
///    - Click on Element 4:
///      * Section Name: "BottomSection"
///      * Grid Width: 4
///      * Grid Height: 2
/// 
/// 3. SETUP UI PANELS
///    - In your inventory panel, find each compartment UI:
///      * Top compartment → Add InventoryGridDisplay
///      * Left compartment → Add InventoryGridDisplay
///      * Right compartment → Add InventoryGridDisplay
///      * Middle compartment → Add InventoryGridDisplay
///      * Bottom compartment → Add InventoryGridDisplay
///    
///    - For each InventoryGridDisplay:
///      * Section Name: Match the section (TopSection, LeftSection, etc.)
///      * Grid Width and Height: AUTOMATICALLY POPULATED from InventoryManager
///      * Cell Size: 40-60 pixels (your preference)
///      * Sprites: AUTOMATICALLY POPULATED from InventoryManager (no need to set per-panel)
/// 
/// 4. SETUP STORAGE SIDE
///    - In storageSide panel, add InventoryGridDisplay
///    - Leave Section Name empty (set dynamically when looting)
///    - This will display model storage
/// 
/// 5. CLICKABLE MODELS - MULTI-COMPARTMENT STORAGE
///    
///    ===== SETUP A LOOTABLE MODEL =====
///    
///    Add ClickableModel to any lootable object (furniture, containers, etc.):
///    
///    IN INSPECTOR:
///      [Display]
///      - Model Sprite: The image shown in UI when looting
///      - Model Display Width: Width of sprite in UI (e.g., 200)
///      - Model Display Height: Height of sprite in UI (e.g., 200)
///    
///      [Storage Sections]
///      - Set up compartments with unique names, widths, heights
///      - Each section is independent and stores items separately
///    
///    ===== SINGLE-COMPARTMENT MODEL =====
///    
///    Simple chest with 1 storage area:
///      Storage Sections (size: 1)
///        - Element 0:
///          * Section Name: "Chest"
///          * Grid Width: 6
///          * Grid Height: 4
///    
///    ===== MULTI-COMPARTMENT MODEL (Example: Refrigerator) =====
///    
///    Refrigerator with 5 separate drawers/sections:
///      Storage Sections (size: 5)
///        - Element 0:
///          * Section Name: "Refrigerator1"
///          * Grid Width: 5
///          * Grid Height: 3
///        - Element 1:
///          * Section Name: "Refrigerator2"
///          * Grid Width: 5
///          * Grid Height: 2
///        - Element 2:
///          * Section Name: "FreezerDrawer"
///          * Grid Width: 4
///          * Grid Height: 3
///        - Element 3:
///          * Section Name: "LeftDoor"
///          * Grid Width: 2
///          * Grid Height: 5
///        - Element 4:
///          * Section Name: "RightDoor"
///          * Grid Width: 2
///          * Grid Height: 5
///    
///    When player loots this model, ALL 5 compartments display at once in UI containers.
///    Each compartment can have different items.
///    
///    ===== REUSING COMPARTMENT LAYOUT =====
///    
///    Multiple models can use the SAME compartment structure:
///    
///    Drawer A (ClickableModel):
///      - Drawer1: 5×3
///      - Drawer2: 5×2
///    
///    Drawer B (ClickableModel):
///      - Drawer1: 5×3 ← SAME layout
///      - Drawer2: 5×2 ← SAME layout
///    
///    But they have DIFFERENT items inside! Each model stores its own inventory.
///    The UI containers (storage-side) are REUSED for both models.
///    
///    ===== UI SETUP FOR MODEL STORAGE =====
///    
///    In your inventory panel, create UI containers for model compartments:
///    
///    Example hierarchy (for Refrigerator):
///      StorageSide
///        ├─ Refrigerator1
///        │   └─ GridContainer (GridLayoutGroup)
///        │       └─ InventoryGridDisplay (script)
///        │
///        └─ Refrigerator2
///            └─ GridContainer (GridLayoutGroup)
///                └─ InventoryGridDisplay (script)
///    
///    For each compartment container:
///      - Parent name must match section name (e.g., "Refrigerator1", "Refrigerator2")
///      - Attach InventoryGridDisplay script to parent
///      - Set in Inspector:
///        * Section Name: Match the section name
///        * Grid Container: Point to the GridContainer child
///        * Cell Size: Your preferred slot size (9px shown to work well)
///    
///    The system will automatically populate each container with the model's items!
/// 
/// ===== WORKFLOW EXAMPLE =====
/// 
/// Player clicks on Refrigerator in the world:
///   1. ModelClickHandler detects the click
///   2. Calls OpenInventoryWithStorage() with the Refrigerator model
///   3. InventoryPanelHandler searches StorageSide for matching containers:
///      - Finds "Refrigerator1" container → loads Refrigerator1 section
///      - Finds "Refrigerator2" container → loads Refrigerator2 section
///      - And so on for all sections
///   4. All compartments display in their UI containers simultaneously
///   5. Player can drag items between bag (left) and ANY refrigerator section
///   6. Each section stores items independently
/// 
/// Different model (Drawer):
///   - Click Drawer → StorageSide containers re-populate with Drawer's items
///   - Same UI containers, different model data!
/// 
/// ===== HOW IT WORKS =====
/// 
/// Each compartment is **completely independent**:
/// 
/// Example:
///   - Add sword to TopSection → Only appears in Top Panel
///   - Add key to LeftSection → Only appears in Left Panel
///   - Sword and key can BOTH fit because they're in different sections
///   - But Top Panel can't hold more than 6×4 grid worth of items
/// 
/// ===== COMPARTMENT CAPACITIES =====
/// 
/// TopSection:    6 × 4 = 24 tile units
/// LeftSection:   2 × 4 = 8 tile units
/// RightSection:  2 × 4 = 8 tile units
/// MiddleSection: 4 × 3 = 12 tile units
/// BottomSection: 4 × 2 = 8 tile units
/// TOTAL:                 = 60 tile units
/// 
/// ===== CODE EXAMPLES =====
/// 
/// // Add to specific compartment:
/// InventoryItem sword = new InventoryItem("sword_01", "Iron Sword", swordSprite, 1, 2, ItemCategory.Misc);
/// InventoryManager.Instance.AddItem(sword, "TopSection");
/// 
/// // Add to any compartment (tries in order):
/// InventoryItem key = new InventoryItem("key_01", "Key", keySprite, 1, 1, ItemCategory.Quest);
/// InventoryManager.Instance.AddItem(key);  // Auto-finds first section with space
/// 
/// // Get all items in a compartment:
/// InventoryItem[] topItems = InventoryManager.Instance.GetSectionItems("TopSection");
/// 
/// // Check if specific compartment is full:
/// InventoryGrid topGrid = InventoryManager.Instance.GetGrid("TopSection");
/// bool isFull = !topGrid.FindSpaceForItem(item, out int x, out int y);
/// 
/// ===== COMPARTMENT BREAKDOWN =====
/// 
/// TOP (6×4 = 24 units)
///   - Main storage area
///   - Good for mixed items
///   - Example items: documents (1×1), clothing (2×2), etc.
/// 
/// LEFT & RIGHT (2×4 each = 8 units)
///   - Narrow vertical compartments
///   - Good for thin items
///   - Example items: cards (1×1), thin books (1×2)
/// 
/// MIDDLE (4×3 = 12 units)
///   - Medium storage
///   - Example items: rations, supplies
/// 
/// BOTTOM (4×2 = 8 units)
///   - Limited horizontal space
///   - Good for flat items
///   - Example items: documents (2×1), small supplies
/// 
/// ===== ITEM PLACEMENT STRATEGY =====
/// 
/// When player picks up item without specifying section:
/// 
/// InventoryManager.AddItem(item) tries sections in order:
///   1. TopSection (6×4) - Largest, most flexible
///   2. LeftSection (2×4)
///   3. RightSection (2×4)
///   4. MiddleSection (4×3)
///   5. BottomSection (4×2) - Smallest
/// 
/// This means big items go to Top first, then cascade down.
/// 
/// To put items in specific compartments:
/// InventoryManager.Instance.AddItem(item, "BottomSection");  // Force to Bottom
/// 
/// ===== DRAG-AND-DROP BETWEEN COMPARTMENTS =====
/// 
/// When moving items via drag-and-drop:
/// - Drag from one panel to another
/// - Item moves between sections
/// - If target section has no space → move fails (item stays in source)
/// - Drag system handles this automatically
/// 
/// ===== LOOTING MODELS =====
/// 
/// When opening a ClickableModel's storage:
/// - Left side (Bag): Shows one of the 5 sections (or swappable)
/// - Right side (Storage): Shows model's storage
/// - Player drags items from storage to any bag section
/// 
/// To support multiple bag sections in loot view:
/// - Could add UI to switch which bag section is shown
/// - Or show all 5 sections at once (compact view)
/// 
/// ===== SECTION REFERENCES =====
/// 
/// Always use exact section names:
///   - "TopSection"     (NOT "top" or "Top")
///   - "LeftSection"    (NOT "left" or "Left")
///   - "RightSection"   (NOT "right" or "Right")
///   - "MiddleSection"  (NOT "middle" or "Middle")
///   - "BottomSection"  (NOT "bottom" or "Bottom")
/// 
/// Case-sensitive! "topsection" ≠ "TopSection"
/// 
/// ===== TOTAL BAG CAPACITY =====
/// 
/// Your bag can hold a maximum of 60 tile-units of items.
/// This is equivalent to:
/// - 60 items of 1×1 each, OR
/// - 15 items of 2×2 each, OR
/// - Any combination that fits within the grid constraints
/// 
/// Each section has independent constraints, so a 2×1 item
/// won't fit in BottomSection (only 4×2), but will fit in TopSection (6×4).
/// 
/// ===== MODEL SPRITE DISPLAY SIZING =====
/// 
/// Each ClickableModel can have custom display size for its sprite:
/// 
/// In ClickableModel Inspector:
///   [Display]
///   - Model Display Width: 200 (pixels)
///   - Model Display Height: 200 (pixels)
/// 
/// When looting the model, the sprite shows at this exact size.
/// 
/// Example sizes:
///   - Small item (key): 100×100
///   - Medium item (book): 150×200
///   - Large item (chest): 300×250
/// 
/// Different models show at different sizes automatically!
/// 
/// 
/// </summary>
public class InventorySystemSetup : MonoBehaviour
{
    // This script serves as documentation and examples
    // It can be attached to a manager GameObject for reference
    // 
    // For detailed drag-and-drop setup, see: DragDropInventorySetup.cs
}

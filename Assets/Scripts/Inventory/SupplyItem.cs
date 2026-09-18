using UnityEngine;

/// <summary>
/// ScriptableObject representing a single supply item.
/// Create these as assets in your Resources folder.
/// Right-click > Create > SupplyItem
/// </summary>
[CreateAssetMenu(fileName = "New SupplyItem", menuName = "SupplyItem")]
public class SupplyItem : ScriptableObject
{
    [SerializeField] private string itemName;
    [SerializeField] private Sprite itemImage;
    [SerializeField] private int gridWidth = 1;
    [SerializeField] private int gridHeight = 1;
    [SerializeField] private ItemImportance importance = ItemImportance.Useful;
    [SerializeField] private float weightKg = 0f;  // Weight in kilograms
    [SerializeField] private string description = "";  // Item description

    [Tooltip("0 = not an essential. 1 and up = an essential the packing score counts, and its " +
             "place in the Journal's order. Items sharing a rank are alternatives - packing " +
             "either one covers it (e.g. the small and big flashlight). Keep this in step with " +
             "the quiz: essentials are exactly the items the quiz teaches.")]
    [Min(0)]
    [SerializeField] private int essentialRank = 0;

    public string ItemName => itemName;
    public Sprite ItemImage => itemImage;
    public int GridWidth => gridWidth;
    public int GridHeight => gridHeight;
    public ItemImportance Importance => importance;
    public float WeightKg => weightKg;
    public string Description => description;
    public int EssentialRank => essentialRank;
    public bool IsEssential => essentialRank > 0;
}

public enum ItemImportance
{
    Nuisance,
    Conditional,
    Useful,
    Important,
    Critical
}

/// <summary>
/// Reference to a SupplyItem with a count.
/// Pick an existing SupplyItem and set the quantity.
/// </summary>
[System.Serializable]
public class SupplyItemStack
{
    [SerializeField] public SupplyItem supplyItem;
    [SerializeField] public int count = 1;
}


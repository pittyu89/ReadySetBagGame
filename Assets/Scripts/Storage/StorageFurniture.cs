using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// A piece of furniture in the house that holds supply items: tapping it opens the inventory
/// with this furniture's compartments beside the go bag.
///
/// The compartments come from its <see cref="StorageLayout"/> (one layout per furniture type),
/// and their contents are filled by <see cref="HouseStorage"/> the first time any storage in
/// the house is opened - from <see cref="startingItems"/>, or shuffled across the house on the
/// harder difficulties.
/// </summary>
public class StorageFurniture : MonoBehaviour
{
    [Tooltip("The layout prefab for this type of furniture (Prefabs/StorageLayouts).")]
    [SerializeField] private StorageLayout layout;

    [Tooltip("Items this furniture holds when the drill starts. On difficulties that shuffle " +
             "items they are spread across the house instead.")]
    [FormerlySerializedAs("supplyItems")]
    [SerializeField] private SupplyItemStack[] startingItems;

    private InventoryGrid[] compartments;
    private Collider cachedCollider;
    private bool colliderCached;

    /// <summary>Set by <see cref="HouseStorage"/> once the house's items have been placed.</summary>
    internal bool IsFilled { get; set; }

    public StorageLayout Layout => layout;
    public SupplyItemStack[] StartingItems => startingItems;

    public int CompartmentCount
    {
        get
        {
            EnsureCompartments();
            return compartments.Length;
        }
    }

    /// <summary>
    /// The grid behind one compartment. Fills the house's storage first if nothing has
    /// opened it yet, so every caller sees the same, settled contents.
    /// </summary>
    public InventoryGrid GetCompartment(int index)
    {
        HouseStorage.EnsureFilled(this);
        EnsureCompartments();
        return index >= 0 && index < compartments.Length ? compartments[index] : null;
    }

    /// <summary>Puts an item in the first compartment with room for it.</summary>
    internal bool TryStore(InventoryItem item)
    {
        EnsureCompartments();
        foreach (InventoryGrid grid in compartments)
        {
            if (grid.FindSpaceForItem(item, out int x, out int y))
            {
                grid.PlaceItem(x, y, item);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Built on first use rather than in Awake: furniture on floors that start hidden never
    /// runs Awake until the floor is shown, but its storage is filled with everyone else's.
    /// </summary>
    private void EnsureCompartments()
    {
        if (compartments != null)
            return;

        if (layout == null)
        {
            Debug.LogWarning($"{name} has no storage layout, so it has no compartments to open.", this);
            compartments = new InventoryGrid[0];
            return;
        }

        var shape = layout.Compartments;
        compartments = new InventoryGrid[shape.Count];
        for (int i = 0; i < shape.Count; i++)
            compartments[i] = new InventoryGrid(shape[i].width, shape[i].height);
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
    /// Flat (X/Z) distance from a world position to this furniture's collider surface.
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
    /// Whether a player at the given position can reach this furniture.
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
}

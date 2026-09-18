using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What one type of furniture looks like when opened: its picture, and a grid display for
/// each of its compartments, placed over the picture. Lives on the root of a layout prefab
/// (one per furniture type: fridge, bookshelf, closet, ...), which the inventory panel
/// instantiates over the furniture picture whenever a piece of that furniture is opened.
///
/// Every piece of furniture of the same type shares its layout, so adding another bookshelf
/// to the house is just a matter of pointing its <see cref="StorageFurniture"/> at the
/// bookshelf layout - no storage UI to build.
/// </summary>
public class StorageLayout : MonoBehaviour
{
    [Serializable]
    public class Compartment
    {
        [Tooltip("Only for reading the layout in the Inspector.")]
        public string name;
        [Tooltip("The grid display drawn over this compartment in the furniture picture.")]
        public InventoryGridDisplay display;
        [Tooltip("Size of the compartment in item tiles.")]
        [Min(1)] public int width = 1;
        [Min(1)] public int height = 1;
    }

    [Tooltip("Shown behind the compartments when a piece of this furniture is opened.")]
    [SerializeField] private Sprite furnitureSprite;
    [Tooltip("Size of that picture in the storage side of the inventory panel.")]
    [SerializeField] private Vector2 displaySize = new Vector2(400f, 400f);
    [SerializeField] private Compartment[] compartments = new Compartment[0];

    public Sprite FurnitureSprite => furnitureSprite;
    public Vector2 DisplaySize => displaySize;
    public IReadOnlyList<Compartment> Compartments => compartments;
}

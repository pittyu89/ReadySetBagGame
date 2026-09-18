using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Puts the house's supply items into its furniture at the start of a drill.
///
/// On Beginner every piece of furniture holds its own authored <see cref="StorageFurniture.StartingItems"/>,
/// put where people would expect to find them, so a new player can learn where things live.
/// On the harder difficulties the items from every open area of the house are pooled and
/// spread at random across the furniture in those areas - including the garage and upstairs
/// furniture those difficulties unlock - with at least one item in each, so the search is
/// different every run and cannot be memorised.
///
/// Runs once per scene, the first time any furniture's compartments are asked for, and fills
/// every piece of furniture in one go so a shuffle can move items between them.
/// </summary>
public static class HouseStorage
{
    public static bool ShufflesItems(string difficulty)
    {
        return difficulty == "intermediate" || difficulty == "advanced";
    }

    internal static void EnsureFilled(StorageFurniture asking)
    {
        if (asking == null || asking.IsFilled)
            return;

        FillHouse();
    }

    private static void FillHouse()
    {
        StorageFurniture[] all = Object.FindObjectsByType<StorageFurniture>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        string difficulty = PlayerPrefs.GetString("SessionDifficulty", "beginner").ToLowerInvariant();
        bool shuffle = ShufflesItems(difficulty);
        GameDifficultyApplier areas = Object.FindAnyObjectByType<GameDifficultyApplier>(FindObjectsInactive.Include);

        List<StorageFurniture> open = new List<StorageFurniture>();
        List<InventoryItem> pool = new List<InventoryItem>();

        foreach (StorageFurniture furniture in all)
        {
            if (furniture.IsFilled)
                continue;
            furniture.IsFilled = true;

            List<InventoryItem> items = CreateStartingItems(furniture);
            bool reachable = areas == null || areas.IsAreaOpen(furniture.transform);

            // Furniture in a closed-off area keeps its own items: the player can't get there,
            // so moving its items into the open house would change what the difficulty offers.
            if (shuffle && reachable)
            {
                open.Add(furniture);
                pool.AddRange(items);
            }
            else
            {
                foreach (InventoryItem item in items)
                    StoreOrWarn(item, furniture);
            }
        }

        if (!shuffle)
            return;

        // First, one item into every open piece of furniture, so no search comes up empty
        Shuffle(open);
        Shuffle(pool);
        foreach (StorageFurniture furniture in open)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                if (furniture.TryStore(pool[i]))
                {
                    pool.RemoveAt(i);
                    break;
                }
            }
        }

        // Then the rest, biggest first, so the bulky items still find room before the small
        // ones scatter into every gap; items of the same size are in random order.
        pool.Sort((a, b) => (b.width * b.height).CompareTo(a.width * a.height));

        foreach (InventoryItem item in pool)
        {
            Shuffle(open);
            bool placed = false;
            foreach (StorageFurniture furniture in open)
            {
                if (furniture.TryStore(item))
                {
                    placed = true;
                    break;
                }
            }

            if (!placed)
                Debug.LogWarning($"No open furniture has room for {item.itemName}, so it is left out of this drill.");
        }
    }

    private static List<InventoryItem> CreateStartingItems(StorageFurniture furniture)
    {
        List<InventoryItem> items = new List<InventoryItem>();
        SupplyItemStack[] stacks = furniture.StartingItems;
        if (stacks == null)
            return items;

        foreach (SupplyItemStack stack in stacks)
        {
            if (stack == null || stack.supplyItem == null)
            {
                Debug.LogWarning($"{furniture.name} has an empty starting-item slot.", furniture);
                continue;
            }

            for (int i = 0; i < stack.count; i++)
                items.Add(InventoryItem.FromSupply(stack.supplyItem));
        }
        return items;
    }

    private static void StoreOrWarn(InventoryItem item, StorageFurniture furniture)
    {
        if (!furniture.TryStore(item))
            Debug.LogWarning($"{item.itemName} doesn't fit in {furniture.name}'s compartments, so it is left out of this drill.", furniture);
    }

    private static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

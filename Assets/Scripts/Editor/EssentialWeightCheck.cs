using UnityEditor;
using UnityEngine;

/// <summary>
/// Warns in the console when a perfect packing score can no longer be reached: the lightest
/// bag that covers every essential weighs more than the tightest weight limit.
///
/// There is only a little room to spare (4.65 kg of essentials against 5 kg), so making an
/// essential heavier or adding a new one can quietly tip it over. Nothing in play would show
/// it - the bag just refuses the last item - so this checks whenever item data changes or
/// scripts recompile. Also available from Tools > ReadySetBag > Check Essential Weight.
/// </summary>
public class EssentialWeightCheck : AssetPostprocessor
{
    private const string ITEM_DATA_FOLDER = "Assets/Resources/ItemData";
    private const string ITEM_DATA_RESOURCES_PATH = "ItemData";

    [InitializeOnLoadMethod]
    private static void CheckAfterReload()
    {
        // Wait for the editor to finish loading before touching Resources
        EditorApplication.delayCall += () => Check(false);
    }

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
                                               string[] moved, string[] movedFrom)
    {
        if (TouchesItemData(imported) || TouchesItemData(deleted) ||
            TouchesItemData(moved) || TouchesItemData(movedFrom))
            EditorApplication.delayCall += () => Check(false);
    }

    [MenuItem("Tools/ReadySetBag/Check Essential Weight")]
    private static void CheckFromMenu()
    {
        Check(true);
    }

    private static void Check(bool reportSuccess)
    {
        SupplyItem[] items = Resources.LoadAll<SupplyItem>(ITEM_DATA_RESOURCES_PATH);
        float needed = DrillScore.LightestFullSetKg(items);
        float limit = GameDifficultyApplier.SmallestWeightLimit();
        int essentials = DrillScore.CountEssentialTarget(items);

        if (needed > limit + 0.0001f)
        {
            Debug.LogWarning(
                $"Essential weight check: the lightest full set of {essentials} essentials weighs " +
                $"{needed:0.00} kg, over the {limit:0.##} kg weight limit. A perfect packing score " +
                "can't be reached. Lighten an essential or raise the limit in GameDifficultyApplier.");
        }
        else if (reportSuccess)
        {
            Debug.Log(
                $"Essential weight check passed: the lightest full set of {essentials} essentials " +
                $"weighs {needed:0.00} kg, leaving {limit - needed:0.00} kg under the {limit:0.##} kg limit.");
        }
    }

    private static bool TouchesItemData(string[] paths)
    {
        foreach (string path in paths)
        {
            if (path.StartsWith(ITEM_DATA_FOLDER))
                return true;
        }
        return false;
    }
}

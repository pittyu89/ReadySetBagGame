using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Remembers which supply items each player has unlocked in the Journal. An item unlocks
/// once it is in the go bag when a drill ends, and shows up in the Journal from the next
/// game on. Stored per student in PlayerPrefs, keyed by <see cref="ProgressUser"/>.
/// </summary>
public static class JournalProgress
{
    private const string UNLOCKED_SUFFIX = "_JournalUnlocked";
    // Unlocks the player has already watched animate open in the Journal
    private const string SEEN_SUFFIX = "_JournalSeen";
    private const char SEPARATOR = '|';

    // Items that have been renamed since they were first saved, old name first
    private static readonly string[,] RENAMED = { { "Dust Mask", "N95 Mask" } };

    private static string UserKey => ProgressUser.Key;

    public static HashSet<string> GetUnlocked() => Load(UNLOCKED_SUFFIX);

    public static HashSet<string> GetSeen() => Load(SEEN_SUFFIX);

    public static bool IsUnlocked(string itemName) => GetUnlocked().Contains(itemName);

    /// <summary>
    /// Registers everything that was in the bag at the end of a drill.
    /// </summary>
    public static void Unlock(IEnumerable<string> itemNames)
    {
        HashSet<string> unlocked = GetUnlocked();
        bool changed = false;

        foreach (string itemName in itemNames)
        {
            if (!string.IsNullOrEmpty(itemName) && unlocked.Add(itemName))
                changed = true;
        }

        if (changed)
            Save(UNLOCKED_SUFFIX, unlocked);
    }

    public static void MarkSeen(string itemName)
    {
        HashSet<string> seen = GetSeen();
        if (seen.Add(itemName))
            Save(SEEN_SUFFIX, seen);
    }

    public static void ResetProgress()
    {
        PlayerPrefs.DeleteKey(UserKey + UNLOCKED_SUFFIX);
        PlayerPrefs.DeleteKey(UserKey + SEEN_SUFFIX);
        PlayerPrefs.Save();
    }

    private static HashSet<string> Load(string suffix)
    {
        ProgressUser.CarryOverString(suffix);

        HashSet<string> names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        string raw = PlayerPrefs.GetString(UserKey + suffix, "");

        foreach (string itemName in raw.Split(SEPARATOR))
        {
            if (!string.IsNullOrEmpty(itemName))
                names.Add(itemName);
        }

        // An unlock saved under an item's old name still counts for the item
        bool renamed = false;
        for (int i = 0; i < RENAMED.GetLength(0); i++)
        {
            if (names.Remove(RENAMED[i, 0]))
            {
                names.Add(RENAMED[i, 1]);
                renamed = true;
            }
        }
        if (renamed)
            Save(suffix, names);

        return names;
    }

    private static void Save(string suffix, HashSet<string> names)
    {
        PlayerPrefs.SetString(UserKey + suffix, string.Join(SEPARATOR.ToString(), names));
        PlayerPrefs.Save();
    }
}

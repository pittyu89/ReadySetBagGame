using UnityEngine;

/// <summary>
/// Whose progress the Journal and the difficulty locks are reading. Keyed by the student ID
/// the login saved, not the display name — two students with the same name on one shared
/// device used to share, and overwrite, each other's unlocks. Guests share one "Guest" slot.
///
/// Progress saved under the old name keys is carried over to the ID key the first time it is
/// read, then removed so it cannot leak into another student who happens to have that name.
/// </summary>
public static class ProgressUser
{
    private const string GUEST_KEY = "Guest";
    private const string ID_PREFIX = "Student_";

    public static string Key
    {
        get
        {
            if (IsGuest)
                return GUEST_KEY;

            string id = PlayerPrefs.GetString("StudentId", "");
            return string.IsNullOrEmpty(id) ? LegacyKey : ID_PREFIX + id;
        }
    }

    private static bool IsGuest => PlayerPrefs.GetString("IsGuest", "false") == "true";

    // How progress was keyed before: the student's display name
    private static string LegacyKey => PlayerPrefs.GetString("StudentName", "User");

    /// <summary>
    /// Moves a string saved under the old name key to the ID key, if the ID key has nothing yet.
    /// </summary>
    public static void CarryOverString(string suffix)
    {
        string from, to;
        if (!NeedsCarryOver(suffix, out from, out to))
            return;

        PlayerPrefs.SetString(to, PlayerPrefs.GetString(from, ""));
        PlayerPrefs.DeleteKey(from);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Moves an int saved under the old name key to the ID key, if the ID key has nothing yet.
    /// </summary>
    public static void CarryOverInt(string suffix)
    {
        string from, to;
        if (!NeedsCarryOver(suffix, out from, out to))
            return;

        PlayerPrefs.SetInt(to, PlayerPrefs.GetInt(from, 0));
        PlayerPrefs.DeleteKey(from);
        PlayerPrefs.Save();
    }

    private static bool NeedsCarryOver(string suffix, out string from, out string to)
    {
        from = LegacyKey + suffix;
        to = Key + suffix;

        // Guests were always "Guest", and with no ID the key already is the name
        if (IsGuest || from == to)
            return false;

        return PlayerPrefs.HasKey(from) && !PlayerPrefs.HasKey(to);
    }
}

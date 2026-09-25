using UnityEngine;

/// <summary>
/// Which difficulties a player has unlocked. Beginner is always open; Intermediate opens
/// with a drill score of <see cref="INTERMEDIATE_UNLOCK_SCORE"/> or better on Beginner, and
/// Advanced with <see cref="ADVANCED_UNLOCK_SCORE"/> or better on Intermediate.
///
/// Stored per user in PlayerPrefs, keyed the same way as the Journal. Only offline play is
/// gated: in a teacher session the teacher picks the difficulty, though a good score there
/// still unlocks the next one for offline practice.
/// </summary>
public static class DifficultyProgress
{
    public const string BEGINNER = "beginner";
    public const string INTERMEDIATE = "intermediate";
    public const string ADVANCED = "advanced";

    public const int INTERMEDIATE_UNLOCK_SCORE = 70;
    public const int ADVANCED_UNLOCK_SCORE = 85;

    private const string UNLOCKED_SUFFIX = "_DifficultyUnlocked_";
    // Unlocks the player has already watched open on the difficulty panel
    private const string SEEN_SUFFIX = "_DifficultyUnlockSeen_";

    /// <summary>Raised whenever a difficulty is locked or unlocked, so open menus can redraw.</summary>
    public static event System.Action Changed;

    private static string UserKey
    {
        get
        {
            bool isGuest = PlayerPrefs.GetString("IsGuest", "false") == "true";
            return isGuest ? "Guest" : PlayerPrefs.GetString("StudentName", "User");
        }
    }

    public static bool IsUnlocked(string difficulty)
    {
        string key = (difficulty ?? "").ToLowerInvariant();
        if (key != INTERMEDIATE && key != ADVANCED)
            return true;

        return PlayerPrefs.GetInt(UserKey + UNLOCKED_SUFFIX + key, 0) == 1;
    }

    /// <summary>
    /// Unlocks the next difficulty if a finished drill scored high enough. Returns the
    /// difficulty this run newly unlocked, or null if nothing changed.
    /// </summary>
    public static string RecordScore(string difficulty, int finalScore)
    {
        string key = (difficulty ?? "").ToLowerInvariant();

        if (key == BEGINNER && finalScore >= INTERMEDIATE_UNLOCK_SCORE && !IsUnlocked(INTERMEDIATE))
        {
            SetUnlocked(INTERMEDIATE, true);
            return INTERMEDIATE;
        }

        if (key == INTERMEDIATE && finalScore >= ADVANCED_UNLOCK_SCORE && !IsUnlocked(ADVANCED))
        {
            SetUnlocked(ADVANCED, true);
            return ADVANCED;
        }

        return null;
    }

    /// <summary>
    /// What it takes to open <paramref name="difficulty"/>, for the difficulty panel.
    /// Empty for Beginner, which is never locked.
    /// </summary>
    public static string Requirement(string difficulty)
    {
        switch ((difficulty ?? "").ToLowerInvariant())
        {
            case INTERMEDIATE: return $"Score {INTERMEDIATE_UNLOCK_SCORE} or higher on Beginner to unlock.";
            case ADVANCED: return $"Score {ADVANCED_UNLOCK_SCORE} or higher on Intermediate to unlock.";
            default: return string.Empty;
        }
    }

    /// <summary>Sets the lock directly. Used by unlocking itself and by the debug picker.</summary>
    public static void SetUnlocked(string difficulty, bool unlocked)
    {
        string key = (difficulty ?? "").ToLowerInvariant();
        if (key != INTERMEDIATE && key != ADVANCED)
            return;

        PlayerPrefs.SetInt(UserKey + UNLOCKED_SUFFIX + key, unlocked ? 1 : 0);

        // Locking it again means the unlock is worth showing again when it's next earned
        if (!unlocked)
            PlayerPrefs.DeleteKey(UserKey + SEEN_SUFFIX + key);

        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    /// <summary>True once the unlock animation for <paramref name="difficulty"/> has played.</summary>
    public static bool IsUnlockSeen(string difficulty) =>
        PlayerPrefs.GetInt(UserKey + SEEN_SUFFIX + (difficulty ?? "").ToLowerInvariant(), 0) == 1;

    public static void MarkUnlockSeen(string difficulty)
    {
        PlayerPrefs.SetInt(UserKey + SEEN_SUFFIX + (difficulty ?? "").ToLowerInvariant(), 1);
        PlayerPrefs.Save();
    }
}

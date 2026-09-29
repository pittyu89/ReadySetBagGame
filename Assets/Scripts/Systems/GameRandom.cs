using System;
using System.Collections.Generic;

/// <summary>
/// The random draws that decide how a drill plays out: which questions are asked and in what
/// order, and where items are hidden on the harder difficulties.
///
/// Kept apart from UnityEngine.Random on purpose. That generator is shared with everything in
/// the game and its packages, any of which can reseed it, and runs were coming out with the
/// same question order every time. This one is seeded fresh from a GUID once per launch and
/// used by nothing else, so every drill gets its own draw.
/// </summary>
public static class GameRandom
{
    private static readonly Random rng = new Random(Guid.NewGuid().GetHashCode());

    /// <summary>A whole number from <paramref name="min"/> up to, but not including, <paramref name="max"/>.</summary>
    public static int Range(int min, int max)
    {
        return rng.Next(min, max);
    }

    /// <summary>Puts the list in a random order, every order equally likely (Fisher-Yates).</summary>
    public static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

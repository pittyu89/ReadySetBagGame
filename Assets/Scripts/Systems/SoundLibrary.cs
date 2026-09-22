using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The game's one list of sound effects and how each one is played.
///
/// Scripts keep their own AudioClip fields and call <see cref="SoundManager.Sfx"/> as
/// before; the manager looks the clip up here for its volume, whether its pitch may be
/// varied, and whether it may be cut off. So tuning a sound is one edit in this asset,
/// however many scripts play it, and a clip missing from the list still plays - at full
/// volume, at its real pitch, like any other.
/// </summary>
[CreateAssetMenu(fileName = "SoundLibrary", menuName = "ReadySetBag/Sound Library")]
public class SoundLibrary : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Nudge the pitch a little on every play so repeats don't sound identical. " +
                 "Good for doors, bags and item drops; wrong for jingles and UI clicks, " +
                 "which sound out of tune when shifted.")]
        public bool varyPitch;
        [Tooltip("Never cut this sound off to make room for another, e.g. a results fanfare.")]
        public bool protect;
    }

    [SerializeField] private Entry[] entries = new Entry[0];

    private Dictionary<AudioClip, Entry> lookup;

    /// <summary>The settings for a clip, or null if it isn't listed.</summary>
    public Entry Find(AudioClip clip)
    {
        if (clip == null)
            return null;

        if (lookup == null)
        {
            lookup = new Dictionary<AudioClip, Entry>();
            foreach (Entry entry in entries)
                if (entry != null && entry.clip != null)
                    lookup[entry.clip] = entry;
        }

        lookup.TryGetValue(clip, out Entry found);
        return found;
    }

    private void OnValidate()
    {
        // Rebuilt on next use, so edits in the Inspector apply during play
        lookup = null;
    }
}

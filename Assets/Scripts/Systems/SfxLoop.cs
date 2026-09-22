using UnityEngine;

/// <summary>
/// A sound effect that loops for as long as something keeps happening — water pouring, a
/// pencil on paper, a finger rubbing mud — and fades away once it stops.
///
/// The owner calls <see cref="Hold"/> on every frame, or every input event, that the action is
/// going on. The sound carries on for a moment past the last call and then fades out, so
/// nothing ever has to switch it off: a minigame the clock tears down mid-pour falls silent by
/// itself instead of leaving water running under the next question. It also fades while the
/// game is paused.
///
/// Routed to the SFX bus, with its level from the <see cref="SoundLibrary"/>, like any other
/// sound effect.
/// </summary>
public class SfxLoop : MonoBehaviour
{
    private AudioSource source;
    private float sustain;
    private float fadeIn;
    private float fadeOut;

    private float lastHeld = float.NegativeInfinity;
    private float level;

    /// <summary>
    /// Adds a loop for <paramref name="clip"/> to <paramref name="host"/>. Null when there is
    /// no clip, so an unassigned sound simply never plays.
    /// </summary>
    /// <param name="sustain">How long it keeps going after the last Hold, in seconds. Covers
    /// the gaps between drag events, which don't arrive every frame.</param>
    public static SfxLoop Create(GameObject host, AudioClip clip, float sustain = 0.15f,
                                 float fadeIn = 0.05f, float fadeOut = 0.2f)
    {
        if (host == null || clip == null)
            return null;

        SfxLoop loop = host.AddComponent<SfxLoop>();
        loop.sustain = sustain;
        loop.fadeIn = Mathf.Max(0.001f, fadeIn);
        loop.fadeOut = Mathf.Max(0.001f, fadeOut);

        loop.source = host.AddComponent<AudioSource>();
        loop.source.clip = clip;
        loop.source.loop = true;
        loop.source.playOnAwake = false;
        loop.source.spatialBlend = 0f;
        loop.source.volume = 0f;

        if (SoundManager.Instance != null)
            loop.source.outputAudioMixerGroup = SoundManager.Instance.SFXGroup;

        return loop;
    }

    /// <summary>Keeps the sound going for now. Call it while the action lasts.</summary>
    public void Hold()
    {
        lastHeld = Time.unscaledTime;
    }

    private void Update()
    {
        if (source == null)
            return;

        bool on = Time.timeScale > 0f && Time.unscaledTime - lastHeld <= sustain;
        float rate = on ? 1f / fadeIn : 1f / fadeOut;
        level = Mathf.MoveTowards(level, on ? 1f : 0f, rate * Time.unscaledDeltaTime);

        if (level > 0f && !source.isPlaying)
        {
            // From a random point, so repeated strokes don't all start on the same scrape
            source.time = Random.Range(0f, source.clip.length * 0.9f);
            source.Play();
        }
        else if (level <= 0f && source.isPlaying)
        {
            source.Stop();
        }

        float baseVolume = SoundManager.Instance != null
            ? SoundManager.Instance.GetSfxSourceVolume(source.clip)
            : 1f;
        source.volume = level * baseVolume;
    }

    private void OnDisable()
    {
        level = 0f;
        lastHeld = float.NegativeInfinity;

        if (source != null)
            source.Stop();
    }
}

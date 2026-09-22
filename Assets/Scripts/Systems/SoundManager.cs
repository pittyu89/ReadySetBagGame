using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

/// <summary>
/// Manages all audio in the game: music, SFX, and UI sounds.
/// Persists across scenes as a singleton.
/// Integrates with options panel volume sliders.
///
/// Volume model: sliders store a raw 0..1 position, which is converted to an
/// amplitude through <see cref="volumeCurveExponent"/> before it reaches any
/// AudioSource or mixer. Amplitude is linear but hearing is not, so a raw slider
/// at 0.5 sounds like roughly 3/4 volume; the curve is what makes the midpoint
/// read as "half as loud". The conversion happens in exactly one place so music,
/// pooled SFX and externally registered sources all share the same response.
///
/// If a mixer is assigned, buses own the volume and every AudioSource stays at
/// amplitude 1 (fades aside). Without a mixer the same amplitude is written to
/// each source directly, so the system behaves identically either way.
///
/// The manager lives in a prefab under Resources and creates itself before the first
/// scene loads, so every scene has sound - including one opened straight in the Editor.
/// </summary>
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    // Resources path of the prefab the manager is created from
    private const string PREFAB_PATH = "SoundManager";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null)
            return;

        SoundManager prefab = Resources.Load<SoundManager>(PREFAB_PATH);
        if (prefab == null)
        {
            Debug.LogWarning("[SoundManager] No SoundManager prefab in Resources - the game will be silent.");
            return;
        }

        Instantiate(prefab).name = prefab.name;
    }

    /// <summary>
    /// Plays a one-shot SFX with its settings from the <see cref="SoundLibrary"/>.
    /// Safe to call with a null clip or before the manager exists, so callers need no checks.
    /// </summary>
    public static void Sfx(AudioClip clip)
    {
        if (clip != null && Instance != null)
            Instance.PlaySFX(clip);
    }

    /// <summary>
    /// Plays one of several takes of the same sound, never the same one twice running when
    /// there is a choice, so a repeated action doesn't sound like a stuck recording.
    /// </summary>
    public static void Sfx(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0 || Instance == null)
            return;

        int pick = Random.Range(0, clips.Length);
        if (clips.Length > 1 && clips[pick] == Instance.lastVariantPlayed)
            pick = (pick + 1 + Random.Range(0, clips.Length - 1)) % clips.Length;

        Instance.lastVariantPlayed = clips[pick];
        Instance.PlaySFX(clips[pick]);
    }

    // The take Sfx(AudioClip[]) played last, so the next call can pick a different one
    private AudioClip lastVariantPlayed;

    [Header("Sound Library")]
    [Tooltip("Per-sound volume, pitch variation and protection. Clips not listed play at " +
             "full volume and their real pitch.")]
    [SerializeField] private SoundLibrary library;

    [Header("Audio Mixer (optional)")]
    [Tooltip("Assign to route audio through mixer buses. Leave empty to fall back to per-AudioSource volume.")]
    [SerializeField] private AudioMixer mixer;
    [SerializeField] private AudioMixerGroup musicGroup;
    [SerializeField] private AudioMixerGroup sfxGroup;

    [Header("Audio Sources")]
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioSource sfxSource;

    [Header("SFX Voices")]
    [Tooltip("Pooled voices so overlapping SFX can each carry their own pitch.")]
    [SerializeField] private int sfxVoiceCount = 8;

    [Tooltip("Random pitch spread for sounds the library marks Vary Pitch, so repeats don't " +
             "machine-gun. 0 disables it.")]
    [Range(0f, 0.5f)]
    [SerializeField] private float sfxPitchVariation = 0.08f;

    [Header("Music")]
    [Tooltip("Seconds to crossfade when switching tracks. 0 is a hard cut.")]
    [SerializeField] private float defaultCrossfadeDuration = 1f;
    [Tooltip("Seconds StopMusic() fades out over when no length is given. 0 is a hard cut.")]
    [SerializeField] private float defaultStopFadeDuration = 0.6f;

    [Header("Volume Ranges")]
    [SerializeField] private float minVolume = 0f;
    [SerializeField] private float maxVolume = 1f;

    [Tooltip("Slider-to-amplitude exponent. 1 is raw linear amplitude, 2 approximates perceived loudness.")]
    [Range(1f, 3f)]
    [SerializeField] private float volumeCurveExponent = 2f;

    // Exposed mixer parameter names. These must match the parameters exposed on
    // the mixer asset, otherwise SetFloat silently no-ops.
    private const string MASTER_MIXER_PARAM = "MasterVolume";
    private const string MUSIC_MIXER_PARAM = "MusicVolume";
    private const string SFX_MIXER_PARAM = "SFXVolume";

    private const string MASTER_VOLUME_KEY = "MasterVolume";
    private const string MUSIC_VOLUME_KEY = "MusicVolume";
    private const string SFX_VOLUME_KEY = "SFXVolume";
    private const string MUTED_KEY = "AudioMuted";

    // Raw 0..1 slider positions, not amplitudes.
    private float masterVolume = 1f;
    private float musicVolume = 1f;
    private float sfxVolume = 1f;
    private bool isMuted;

    // Set whenever a volume actually changes so SaveVolumeSettings can skip
    // pointless PlayerPrefs writes.
    private bool volumeSettingsDirty;

    // Two music sources so one track can fade out while the next fades in.
    private AudioSource musicA;
    private AudioSource musicB;
    private AudioSource activeMusic;
    private float fadeA = 1f;
    private float fadeB;
    private Coroutine musicFadeRoutine;

    private readonly List<AudioSource> sfxVoices = new List<AudioSource>();
    // Parallel to sfxVoices: true while that voice plays a sound that must not be cut off
    private readonly List<bool> voiceProtected = new List<bool>();
    private int nextVoiceIndex;

    // AudioSources owned by other scripts (looping footsteps, the timer tick)
    // that still need to track the SFX volume. They are pushed to on change
    // rather than polling every frame.
    private readonly List<AudioSource> registeredSfxSources = new List<AudioSource>();

    /// <summary>Mixer group SFX should route to, or null if no mixer is assigned.</summary>
    public AudioMixerGroup SFXGroup => sfxGroup;

    /// <summary>Mixer group music should route to, or null if no mixer is assigned.</summary>
    public AudioMixerGroup MusicGroup => musicGroup;

    /// <summary>Amplitude (not slider position) currently applied to music.</summary>
    public float MusicAmplitude => isMuted ? 0f : ToAmplitude(masterVolume) * ToAmplitude(musicVolume);

    /// <summary>Amplitude (not slider position) currently applied to SFX.</summary>
    public float SFXAmplitude => isMuted ? 0f : ToAmplitude(masterVolume) * ToAmplitude(sfxVolume);

    private void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        CreateAudioSourcesIfNeeded();

        // Load saved volumes
        LoadVolumeSettings();
    }

    private void CreateAudioSourcesIfNeeded()
    {
        if (musicSource == null)
        {
            musicSource = CreateChildSource("MusicSource");
            musicSource.loop = true;
        }

        musicSource.spatialBlend = 0f; // 2D audio
        musicSource.playOnAwake = false;
        musicSource.outputAudioMixerGroup = musicGroup;
        musicA = musicSource;

        // Second deck, used only as the crossfade partner.
        musicB = CreateChildSource("MusicSource (Crossfade)");
        musicB.loop = musicA.loop;
        musicB.outputAudioMixerGroup = musicGroup;

        activeMusic = musicA;
        fadeA = 1f;
        fadeB = 0f;

        if (sfxSource == null)
        {
            sfxSource = CreateChildSource("SFXSource");
        }

        sfxSource.spatialBlend = 0f; // 2D audio
        sfxSource.playOnAwake = false;
        sfxSource.outputAudioMixerGroup = sfxGroup;

        // The serialized source doubles as the first pooled voice so an existing
        // Inspector assignment is not orphaned.
        sfxVoices.Clear();
        sfxVoices.Add(sfxSource);
        for (int i = sfxVoices.Count; i < Mathf.Max(1, sfxVoiceCount); i++)
        {
            AudioSource voice = CreateChildSource("SFXVoice " + i);
            voice.outputAudioMixerGroup = sfxGroup;
            sfxVoices.Add(voice);
        }

        voiceProtected.Clear();
        for (int i = 0; i < sfxVoices.Count; i++)
            voiceProtected.Add(false);
    }

    private AudioSource CreateChildSource(string sourceName)
    {
        GameObject obj = new GameObject(sourceName);
        obj.transform.SetParent(transform);
        AudioSource source = obj.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f; // 2D audio
        return source;
    }

    /// <summary>
    /// Converts a raw 0..1 slider position into an amplitude.
    /// </summary>
    private float ToAmplitude(float slider01)
    {
        return Mathf.Pow(Mathf.Clamp01(slider01), volumeCurveExponent);
    }

    private static float LinearToDecibels(float amplitude)
    {
        // -80 dB is the mixer's floor and is treated as silence.
        return amplitude <= 0.0001f ? -80f : Mathf.Log10(amplitude) * 20f;
    }

    #region Slider registration

    /// <summary>
    /// Registers volume sliders with this sound manager.
    /// Call this from the OptionsPanel when it opens.
    /// </summary>
    public void RegisterVolumeSliders(Slider musicSlider, Slider sfxSlider)
    {
        RegisterVolumeSliders(musicSlider, sfxSlider, null, null);
    }

    /// <summary>
    /// Registers volume sliders and the optional master slider / mute toggle.
    /// Safe to call repeatedly: previous listeners are cleared first, so
    /// reopening the options panel does not stack duplicate callbacks.
    /// </summary>
    public void RegisterVolumeSliders(Slider musicSlider, Slider sfxSlider, Slider masterSlider, Toggle muteToggle)
    {
        BindSlider(masterSlider, masterVolume, SetMasterVolume);
        BindSlider(musicSlider, musicVolume, SetMusicVolume);
        BindSlider(sfxSlider, sfxVolume, SetSFXVolume);

        if (muteToggle != null)
        {
            muteToggle.onValueChanged.RemoveAllListeners();
            muteToggle.SetIsOnWithoutNotify(isMuted);
            muteToggle.onValueChanged.AddListener(SetMuted);
        }
    }

    private void BindSlider(Slider slider, float value, UnityEngine.Events.UnityAction<float> onChanged)
    {
        if (slider == null)
            return;

        // Clearing first keeps a reopened panel from stacking a second copy of
        // this callback on top of the one it added last time.
        slider.onValueChanged.RemoveAllListeners();
        slider.minValue = minVolume;
        slider.maxValue = maxVolume;
        slider.SetValueWithoutNotify(value);
        slider.onValueChanged.AddListener(onChanged);
    }

    #endregion

    #region Volume control

    /// <summary>
    /// Set master volume (called by slider).
    /// </summary>
    public void SetMasterVolume(float volume)
    {
        masterVolume = Mathf.Clamp01(volume);
        volumeSettingsDirty = true;
        ApplyAllVolumes();
    }

    /// <summary>
    /// Set music volume (called by slider).
    /// </summary>
    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        volumeSettingsDirty = true;
        ApplyAllVolumes();
    }

    /// <summary>
    /// Set SFX volume (called by slider).
    /// </summary>
    public void SetSFXVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
        volumeSettingsDirty = true;
        ApplyAllVolumes();
    }

    /// <summary>
    /// Mute or unmute all audio without disturbing the slider positions.
    /// </summary>
    public void SetMuted(bool muted)
    {
        isMuted = muted;
        volumeSettingsDirty = true;
        ApplyAllVolumes();
    }

    /// <summary>Flips the mute state. Handy for a single mute button.</summary>
    public void ToggleMute()
    {
        SetMuted(!isMuted);
    }

    public bool IsMuted()
    {
        return isMuted;
    }

    /// <summary>Raw 0..1 master slider position.</summary>
    public float GetMasterVolume()
    {
        return masterVolume;
    }

    /// <summary>Raw 0..1 music slider position.</summary>
    public float GetMusicVolume()
    {
        return musicVolume;
    }

    /// <summary>
    /// Raw 0..1 SFX slider position. For an AudioSource volume use
    /// <see cref="SFXAmplitude"/>, or just register the source via
    /// <see cref="RegisterSFXSource"/> and let it be driven automatically.
    /// </summary>
    public float GetSFXVolume()
    {
        return sfxVolume;
    }

    private void ApplyAllVolumes()
    {
        if (mixer != null)
        {
            mixer.SetFloat(MASTER_MIXER_PARAM, LinearToDecibels(isMuted ? 0f : ToAmplitude(masterVolume)));
            mixer.SetFloat(MUSIC_MIXER_PARAM, LinearToDecibels(ToAmplitude(musicVolume)));
            mixer.SetFloat(SFX_MIXER_PARAM, LinearToDecibels(ToAmplitude(sfxVolume)));
        }

        ApplyMusicVolumes();
        ApplySFXVolumes();
    }

    private void ApplyMusicVolumes()
    {
        // With a mixer group the bus owns the level, so the source only carries
        // the crossfade envelope.
        float amp = musicGroup != null ? 1f : MusicAmplitude;

        if (musicA != null)
            musicA.volume = amp * fadeA;
        if (musicB != null)
            musicB.volume = amp * fadeB;
    }

    private void ApplySFXVolumes()
    {
        float amp = sfxGroup != null ? 1f : SFXAmplitude;

        // With a bus the pooled voices only carry their per-clip trim, set when they
        // start, so leave them be rather than flattening a playing sound's trim to 1
        if (sfxGroup == null)
        {
            for (int i = 0; i < sfxVoices.Count; i++)
            {
                if (sfxVoices[i] != null)
                    sfxVoices[i].volume = amp;
            }
        }

        // Externally owned sources are pushed to here, on change only.
        for (int i = registeredSfxSources.Count - 1; i >= 0; i--)
        {
            AudioSource source = registeredSfxSources[i];
            if (source == null)
            {
                registeredSfxSources.RemoveAt(i);
                continue;
            }

            source.volume = amp;
        }
    }

    #endregion

    #region External SFX sources

    /// <summary>
    /// Hands an AudioSource owned by another script over to the SFX bus.
    /// The source is routed to the SFX mixer group when one exists, and its
    /// volume is kept current from then on, so callers never need to poll
    /// <see cref="GetSFXVolume"/> in Update.
    /// </summary>
    public void RegisterSFXSource(AudioSource source)
    {
        if (source == null || registeredSfxSources.Contains(source))
            return;

        registeredSfxSources.Add(source);

        if (sfxGroup != null)
            source.outputAudioMixerGroup = sfxGroup;

        source.volume = sfxGroup != null ? 1f : SFXAmplitude;
    }

    /// <summary>
    /// Stops tracking a source registered with <see cref="RegisterSFXSource"/>.
    /// Call from OnDestroy so the manager does not hold a dead reference.
    /// </summary>
    public void UnregisterSFXSource(AudioSource source)
    {
        if (source == null)
            return;

        registeredSfxSources.Remove(source);
    }

    /// <summary>
    /// The volume an SFX source of its own should sit at to play <paramref name="clip"/>:
    /// its library volume, times the user's SFX level when there is no mixer bus to carry it.
    /// For sources that also fade themselves, such as <see cref="SfxLoop"/>, and so can't
    /// be handed to <see cref="RegisterSFXSource"/>.
    /// </summary>
    public float GetSfxSourceVolume(AudioClip clip)
    {
        SoundLibrary.Entry entry = library != null ? library.Find(clip) : null;
        float clipVolume = entry != null ? entry.volume : 1f;
        return (sfxGroup != null ? 1f : SFXAmplitude) * clipVolume;
    }

    #endregion

    #region Music

    /// <summary>
    /// Play background music, crossfading from whatever is currently playing.
    /// Re-requesting the track that is already playing is ignored, so reloading
    /// a scene no longer restarts the music from the beginning.
    /// </summary>
    public void PlayMusic(AudioClip musicClip, bool loop = true)
    {
        PlayMusic(musicClip, loop, defaultCrossfadeDuration);
    }

    /// <summary>
    /// Play background music with an explicit crossfade length.
    /// Pass 0 for a hard cut.
    /// </summary>
    public void PlayMusic(AudioClip musicClip, bool loop, float fadeDuration)
    {
        PlayMusic(musicClip, loop, fadeDuration, false);
    }

    /// <summary>
    /// Play background music. With <paramref name="resume"/> set, a track that was playing
    /// earlier picks up where it was left rather than from the top — for swapping to a
    /// minigame's track and back without the house music restarting its intro every time.
    /// </summary>
    public void PlayMusic(AudioClip musicClip, bool loop, float fadeDuration, bool resume)
    {
        if (musicClip == null || activeMusic == null)
            return;

        // Already playing this track: leave it alone rather than restarting it.
        if (activeMusic.clip == musicClip && activeMusic.isPlaying)
        {
            activeMusic.loop = loop;
            return;
        }

        AudioSource next = activeMusic == musicA ? musicB : musicA;
        AudioSource previous = activeMusic;

        if (musicFadeRoutine != null)
        {
            StopCoroutine(musicFadeRoutine);
            musicFadeRoutine = null;
        }

        RememberPosition(previous);

        next.clip = musicClip;
        next.loop = loop;
        SetFade(next, 0f);
        ApplyMusicVolumes();
        next.Play();

        if (resume && resumePositions.TryGetValue(musicClip, out float position)
            && position < musicClip.length)
            next.time = position;

        activeMusic = next;

        if (fadeDuration <= 0f || !previous.isPlaying)
        {
            SetFade(previous, 0f);
            SetFade(next, 1f);
            ApplyMusicVolumes();
            previous.Stop();
            previous.clip = null;
            return;
        }

        musicFadeRoutine = StartCoroutine(CrossfadeRoutine(previous, next, fadeDuration));
    }

    /// <summary>
    /// Stop music with the default short fade, so it never cuts off mid-note.
    /// The manager outlives scene loads, so the fade finishes even if the caller
    /// changes scene straight after.
    /// </summary>
    public void StopMusic()
    {
        StopMusic(defaultStopFadeDuration);
    }

    /// <summary>
    /// Stop music, optionally fading it out first.
    /// </summary>
    public void StopMusic(float fadeDuration)
    {
        if (musicFadeRoutine != null)
        {
            StopCoroutine(musicFadeRoutine);
            musicFadeRoutine = null;
        }

        // A fade needs a coroutine, which can't start while the manager itself is
        // being torn down (quitting, or a caller's OnDestroy on the way out)
        if (fadeDuration <= 0f || !isActiveAndEnabled)
        {
            StopSourceImmediate(musicA, ref fadeA);
            StopSourceImmediate(musicB, ref fadeB);
            ApplyMusicVolumes();
            return;
        }

        musicFadeRoutine = StartCoroutine(FadeOutRoutine(fadeDuration));
    }

    /// <summary>True while a music track is playing or fading.</summary>
    public bool IsMusicPlaying()
    {
        return activeMusic != null && activeMusic.isPlaying;
    }

    /// <summary>The music clip currently playing, or null.</summary>
    public AudioClip GetCurrentMusic()
    {
        return activeMusic != null ? activeMusic.clip : null;
    }

    // Where each track was when it was last faded out, for PlayMusic's resume
    private readonly Dictionary<AudioClip, float> resumePositions = new Dictionary<AudioClip, float>();

    /// <summary>
    /// Notes where a track is as it is being replaced. Taken at the start of the crossfade,
    /// so a resumed track comes back on the phrase the player last heard rather than one
    /// that played out under the fade.
    /// </summary>
    private void RememberPosition(AudioSource source)
    {
        // A stopped deck has had its clip cleared, so any clip still on one is a live track
        if (source != null && source.clip != null)
            resumePositions[source.clip] = source.time;
    }

    private void StopSourceImmediate(AudioSource source, ref float fade)
    {
        fade = 0f;
        if (source == null)
            return;

        source.Stop();
        source.clip = null;
    }

    private void SetFade(AudioSource source, float value)
    {
        if (source == musicA)
            fadeA = value;
        else if (source == musicB)
            fadeB = value;
    }

    private float GetFade(AudioSource source)
    {
        if (source == musicA)
            return fadeA;
        if (source == musicB)
            return fadeB;
        return 0f;
    }

    private IEnumerator CrossfadeRoutine(AudioSource from, AudioSource to, float duration)
    {
        float fromStart = GetFade(from);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            // Unscaled so a crossfade still runs while the game is paused.
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            SetFade(from, Mathf.Lerp(fromStart, 0f, t));
            SetFade(to, t);
            ApplyMusicVolumes();

            yield return null;
        }

        SetFade(from, 0f);
        SetFade(to, 1f);
        ApplyMusicVolumes();

        if (from != null)
        {
            from.Stop();
            from.clip = null;
        }

        musicFadeRoutine = null;
    }

    private IEnumerator FadeOutRoutine(float duration)
    {
        float startA = fadeA;
        float startB = fadeB;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            fadeA = Mathf.Lerp(startA, 0f, t);
            fadeB = Mathf.Lerp(startB, 0f, t);
            ApplyMusicVolumes();

            yield return null;
        }

        StopSourceImmediate(musicA, ref fadeA);
        StopSourceImmediate(musicB, ref fadeB);
        ApplyMusicVolumes();

        musicFadeRoutine = null;
    }

    #endregion

    #region SFX

    /// <summary>
    /// Play a one-shot SFX with its settings from the <see cref="SoundLibrary"/>.
    /// </summary>
    public void PlaySFX(AudioClip sfxClip)
    {
        PlaySFX(sfxClip, 1f);
    }

    /// <summary>
    /// Play a one-shot SFX at a relative volume, on top of its library volume.
    /// </summary>
    public void PlaySFX(AudioClip sfxClip, float volumeScale)
    {
        SoundLibrary.Entry entry = library != null ? library.Find(sfxClip) : null;
        PlaySFX(sfxClip, volumeScale, entry != null && entry.varyPitch);
    }

    /// <summary>
    /// Play a one-shot SFX, choosing explicitly whether its pitch is varied.
    /// Each shot takes its own pooled voice, so overlapping sounds can carry
    /// their own pitch. Volume and protection still come from the library.
    /// </summary>
    public void PlaySFX(AudioClip sfxClip, float volumeScale, bool varyPitch)
    {
        if (sfxClip == null)
            return;

        SoundLibrary.Entry entry = library != null ? library.Find(sfxClip) : null;

        int index = GetFreeVoice();
        if (index < 0)
            return;

        AudioSource voice = sfxVoices[index];

        // The bus (or ApplySFXVolumes) already carries the user's SFX level;
        // the library volume and volumeScale are per-clip trims on top of it.
        float baseAmp = sfxGroup != null ? 1f : SFXAmplitude;
        float clipVolume = entry != null ? entry.volume : 1f;

        voice.clip = sfxClip;
        voice.volume = baseAmp * clipVolume * Mathf.Clamp01(volumeScale);
        voice.pitch = varyPitch && sfxPitchVariation > 0f
            ? 1f + Random.Range(-sfxPitchVariation, sfxPitchVariation)
            : 1f;
        voice.Play();

        voiceProtected[index] = entry != null && entry.protect;
    }

    /// <summary>
    /// Index of the voice the next SFX should use: a silent one if there is one,
    /// otherwise the unprotected voice nearest the end of its sound, so what gets cut
    /// is the tail of something nearly over rather than a fanfare. -1 if every voice
    /// is playing a protected sound.
    /// </summary>
    private int GetFreeVoice()
    {
        if (sfxVoices.Count == 0)
            return -1;

        for (int i = 0; i < sfxVoices.Count; i++)
        {
            int index = (nextVoiceIndex + i) % sfxVoices.Count;
            AudioSource voice = sfxVoices[index];
            if (voice != null && !voice.isPlaying)
            {
                nextVoiceIndex = (index + 1) % sfxVoices.Count;
                return index;
            }
        }

        int best = -1;
        float bestProgress = -1f;
        for (int i = 0; i < sfxVoices.Count; i++)
        {
            AudioSource voice = sfxVoices[i];
            if (voice == null || voiceProtected[i])
                continue;

            float progress = voice.clip != null && voice.clip.length > 0f
                ? voice.time / voice.clip.length
                : 1f;
            if (progress > bestProgress)
            {
                bestProgress = progress;
                best = i;
            }
        }

        return best;
    }

    #endregion

    #region Persistence

    /// <summary>
    /// Save volume settings to PlayerPrefs.
    /// The setters deliberately do not write, because a slider drag fires them
    /// every frame; call this once the player is done, such as when the options
    /// panel closes.
    /// </summary>
    public void SaveVolumeSettings()
    {
        if (!volumeSettingsDirty)
            return;

        PlayerPrefs.SetFloat(MASTER_VOLUME_KEY, masterVolume);
        PlayerPrefs.SetFloat(MUSIC_VOLUME_KEY, musicVolume);
        PlayerPrefs.SetFloat(SFX_VOLUME_KEY, sfxVolume);
        PlayerPrefs.SetInt(MUTED_KEY, isMuted ? 1 : 0);
        PlayerPrefs.Save();

        volumeSettingsDirty = false;
    }

    /// <summary>
    /// Load volume settings from PlayerPrefs.
    /// </summary>
    private void LoadVolumeSettings()
    {
        masterVolume = PlayerPrefs.GetFloat(MASTER_VOLUME_KEY, maxVolume);
        musicVolume = PlayerPrefs.GetFloat(MUSIC_VOLUME_KEY, maxVolume);
        sfxVolume = PlayerPrefs.GetFloat(SFX_VOLUME_KEY, maxVolume);
        isMuted = PlayerPrefs.GetInt(MUTED_KEY, 0) == 1;

        volumeSettingsDirty = false;
        ApplyAllVolumes();
    }

    private void OnApplicationPause(bool paused)
    {
        // OnDestroy is not guaranteed to run when a mobile app is backgrounded,
        // so this is the reliable save point on device.
        if (paused)
            SaveVolumeSettings();
    }

    private void OnApplicationQuit()
    {
        SaveVolumeSettings();
    }

    private void OnDestroy()
    {
        if (Instance != this)
            return;

        SaveVolumeSettings();
        Instance = null;
    }

    #endregion
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// Manages video preparation and playback across scenes. Persists via DontDestroyOnLoad
/// just like SoundManager, so videos prepared in TitleScreen are ready to play instantly
/// when later scenes load — no decode delay, no black frames.
///
/// Usage:
///   VideoManager.Instance.PrepareVideo("MainMenuBG", clip, rt);   // call early
///   VideoManager.Instance.PlayVideo("MainMenuBG");                 // instant later
/// </summary>
public class VideoManager : MonoBehaviour
{
    public static VideoManager Instance { get; private set; }

    // Video keys — use these constants everywhere to avoid typos
    public const string MAIN_MENU_BG = "MainMenuBG";
    public const string BEGINNER_PREVIEW = "BeginnerPreview";
    public const string INTERMEDIATE_PREVIEW = "IntermediatePreview";
    public const string ADVANCED_PREVIEW = "AdvancedPreview";

    [Header("Main Menu Background")]
    [SerializeField] private VideoClip mainMenuBGClip;
    [SerializeField] private RenderTexture mainMenuBGRT;

    [Header("Difficulty Previews")]
    [SerializeField] private VideoClip beginnerPreviewClip;
    [SerializeField] private VideoClip intermediatePreviewClip;
    [SerializeField] private VideoClip advancedPreviewClip;
    [SerializeField] private RenderTexture difficultyRT;

    private readonly Dictionary<string, VideoPlayer> players = new Dictionary<string, VideoPlayer>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Prepare all videos immediately so they're ready by the time the player needs them
        if (mainMenuBGClip != null && mainMenuBGRT != null)
            PrepareVideo(MAIN_MENU_BG, mainMenuBGClip, mainMenuBGRT);

        if (difficultyRT != null)
        {
            if (beginnerPreviewClip != null)
                PrepareVideo(BEGINNER_PREVIEW, beginnerPreviewClip, difficultyRT);
            if (intermediatePreviewClip != null)
                PrepareVideo(INTERMEDIATE_PREVIEW, intermediatePreviewClip, difficultyRT);
            if (advancedPreviewClip != null)
                PrepareVideo(ADVANCED_PREVIEW, advancedPreviewClip, difficultyRT);
        }
    }

    /// <summary>
    /// Prepares a video so it's ready to play instantly later. If a video with the same key
    /// is already prepared, this does nothing.
    /// </summary>
    public void PrepareVideo(string key, VideoClip clip, RenderTexture targetTexture, bool loop = true)
    {
        if (clip == null || targetTexture == null) return;

        if (players.ContainsKey(key))
            return;

        var playerObj = new GameObject("VideoPlayer_" + key);
        playerObj.transform.SetParent(transform);

        var vp = playerObj.AddComponent<VideoPlayer>();
        vp.clip = clip;
        vp.renderMode = VideoRenderMode.RenderTexture;
        vp.targetTexture = targetTexture;
        vp.playOnAwake = false;
        vp.isLooping = loop;
        vp.audioOutputMode = VideoAudioOutputMode.None;
        vp.skipOnDrop = true;
        vp.waitForFirstFrame = true;

        players[key] = vp;

        vp.Prepare();
    }

    /// <summary>
    /// Returns true if the video is prepared and ready to play instantly.
    /// </summary>
    public bool IsReady(string key)
    {
        return players.ContainsKey(key) && players[key] != null && players[key].isPrepared;
    }

    /// <summary>
    /// Returns the VideoPlayer for a given key, or null if not found.
    /// </summary>
    public VideoPlayer GetPlayer(string key)
    {
        VideoPlayer vp;
        players.TryGetValue(key, out vp);
        return vp;
    }

    /// <summary>
    /// Plays a previously prepared video. If not yet prepared, starts preparation and plays
    /// as soon as ready.
    /// </summary>
    public void PlayVideo(string key)
    {
        VideoPlayer vp;
        if (!players.TryGetValue(key, out vp) || vp == null) return;

        if (vp.isPrepared)
        {
            vp.Play();
        }
        else
        {
            vp.prepareCompleted += OnPreparedAutoPlay;
            if (!vp.isPrepared)
                vp.Prepare();
        }
    }

    /// <summary>
    /// Stops a video.
    /// </summary>
    public void StopVideo(string key)
    {
        VideoPlayer vp;
        if (players.TryGetValue(key, out vp) && vp != null)
            vp.Stop();
    }

    /// <summary>
    /// Stops and removes a video, freeing its resources.
    /// </summary>
    public void ReleaseVideo(string key)
    {
        VideoPlayer vp;
        if (players.TryGetValue(key, out vp))
        {
            if (vp != null)
            {
                vp.Stop();
                Destroy(vp.gameObject);
            }
            players.Remove(key);
        }
    }

    /// <summary>
    /// Stops and removes all videos.
    /// </summary>
    public void ReleaseAll()
    {
        foreach (var kvp in players)
        {
            if (kvp.Value != null)
            {
                kvp.Value.Stop();
                Destroy(kvp.Value.gameObject);
            }
        }
        players.Clear();
    }

    private void OnPreparedAutoPlay(VideoPlayer vp)
    {
        vp.prepareCompleted -= OnPreparedAutoPlay;
        vp.Play();
    }
}

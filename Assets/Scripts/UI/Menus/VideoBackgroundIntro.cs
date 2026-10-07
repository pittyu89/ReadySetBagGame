using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Handles the MainScene background video using the persistent VideoManager.
/// BGM starts on scene load; the intro animation waits for the scene transition to start
/// uncovering the screen, and the transition waits (briefly) for the video's first frame.
/// </summary>
[DefaultExecutionOrder(-100)]
public class VideoBackgroundIntro : MonoBehaviour
{
    public const string VIDEO_KEY = VideoManager.MAIN_MENU_BG;

    [Header("Background Video")]
    [SerializeField] private RawImage backgroundSurface;
    [SerializeField] private RenderTexture targetRT;

    [Header("Intro")]
    [SerializeField] private MainMenuIntroAnimator introAnimator;
    [SerializeField] private MainMenuManager uiManager;

    private bool blockingReveal;

    private void Awake()
    {
        // Force camera to solid black so the skybox never flashes
        foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
        }

        // Clear the RenderTexture to black
        if (targetRT != null)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = targetRT;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = previous;
        }

        // Hide the surface until the video is playing
        SetAlpha(backgroundSurface, 0f);

        // Keep the scene transition covering the screen until the video shows a frame, so the
        // bands reveal the finished background instead of an empty one
        if (MenuTransition.IsCovering)
        {
            MenuTransition.BlockReveal();
            blockingReveal = true;
        }
    }

    private void Start()
    {
        // Start BGM immediately — don't wait for the video
        if (uiManager != null)
            uiManager.PlayMainMenuBGM();

        StartCoroutine(PlayIntroWhenRevealed());

        // Start the video in the background
        StartCoroutine(StartVideo());
    }

    private void OnDestroy()
    {
        ReleaseReveal();

        // The player outlives the scene. Left running it decodes for nothing during the game,
        // and on Android it can lose its decoder to the game's own videos and stay frozen on
        // the way back, so it is stopped here and started fresh by the next visit.
        if (VideoManager.Instance != null)
            VideoManager.Instance.StopVideo(VIDEO_KEY);
    }

    // Android can pause the decoder while the app is in the background
    private void OnApplicationPause(bool paused)
    {
        if (paused || VideoManager.Instance == null)
            return;

        VideoPlayer vp = VideoManager.Instance.GetPlayer(VIDEO_KEY);
        if (vp != null && !vp.isPlaying)
            vp.Play();
    }

    private IEnumerator PlayIntroWhenRevealed()
    {
        // The intro would otherwise play out behind the transition
        while (MenuTransition.IsCovering)
            yield return null;

        if (introAnimator != null)
            introAnimator.PlayIntro();
    }

    private void ReleaseReveal()
    {
        if (!blockingReveal)
            return;

        blockingReveal = false;
        MenuTransition.UnblockReveal();
    }

    private IEnumerator StartVideo()
    {
        if (VideoManager.Instance == null)
        {
            var vmObj = new GameObject("VideoManager");
            vmObj.AddComponent<VideoManager>();
        }

        var vm = VideoManager.Instance;
        var vp = vm.GetPlayer(VIDEO_KEY);

        if (vp == null)
        {
            var sceneVP = GetComponent<VideoPlayer>();
            if (sceneVP != null && sceneVP.clip != null && targetRT != null)
            {
                vm.PrepareVideo(VIDEO_KEY, sceneVP.clip, targetRT);
                vp = vm.GetPlayer(VIDEO_KEY);
                sceneVP.enabled = false;
            }
        }

        if (vp == null)
        {
            SetAlpha(backgroundSurface, 1f);
            ReleaseReveal();
            yield break;
        }

        if (targetRT != null)
            vp.targetTexture = targetRT;

        // Stopped when the menu was last left, so it usually needs preparing again. Capped so a
        // player that never gets ready can't keep the transition covering the screen.
        if (!vp.isPrepared)
            vp.Prepare();
        for (float waited = 0f; !vp.isPrepared && waited < 5f; waited += Time.unscaledDeltaTime)
            yield return null;

        // The player can already be past frame 1 from before this scene loaded, so wait for a
        // frame drawn after Awake cleared the texture rather than trusting vp.frame
        bool frameDrawn = false;
        VideoPlayer.FrameReadyEventHandler onFrame = (source, index) => frameDrawn = true;
        vp.sendFrameReadyEvents = true;
        vp.frameReady += onFrame;

        vp.Play();

        // Capped so a stalled player can't leave the background hidden
        for (float waited = 0f; !frameDrawn && waited < 2f; waited += Time.unscaledDeltaTime)
            yield return null;

        vp.frameReady -= onFrame;
        vp.sendFrameReadyEvents = false;

        SetAlpha(backgroundSurface, 1f);
        ReleaseReveal();

        // Nothing came through: start the player over rather than leave a still background
        if (!frameDrawn)
        {
            vp.Stop();
            vp.Play();
        }
    }

    private static void SetAlpha(RawImage image, float alpha)
    {
        if (image == null) return;
        Color c = image.color;
        c.a = alpha;
        image.color = c;
    }
}

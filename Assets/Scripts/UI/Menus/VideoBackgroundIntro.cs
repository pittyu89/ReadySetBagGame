using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Handles the MainScene background video using the persistent VideoManager.
/// The intro animations and BGM are independent — they start immediately on scene load.
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
    }

    private void Start()
    {
        // Start BGM and UI animations immediately — don't wait for the video
        if (uiManager != null)
            uiManager.PlayMainMenuBGM();

        if (introAnimator != null)
            introAnimator.PlayIntro();

        // Start the video in the background
        StartCoroutine(StartVideo());
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
            yield break;
        }

        if (targetRT != null)
            vp.targetTexture = targetRT;

        while (!vp.isPrepared)
            yield return null;

        vp.Play();

        while (vp.frame < 1)
            yield return null;

        SetAlpha(backgroundSurface, 1f);
    }

    private static void SetAlpha(RawImage image, float alpha)
    {
        if (image == null) return;
        Color c = image.color;
        c.a = alpha;
        image.color = c;
    }
}

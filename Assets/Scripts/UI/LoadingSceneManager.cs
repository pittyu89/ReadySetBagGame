using UnityEngine;
using UnityEngine.Video;
using UnityEngine.SceneManagement;

public class LoadingSceneManager : MonoBehaviour
{
    [SerializeField] private VideoPlayer loadingVideoPlayer;

    private AsyncOperation asyncLoad;
    private bool videoFinished;
    private bool sceneReady;

    private void Awake()
    {
        // Clear the VideoPlayer's target RenderTexture to black so the
        // full-screen RawImage never shows an uninitialised (white) frame.
        if (loadingVideoPlayer != null && loadingVideoPlayer.targetTexture != null)
        {
            RenderTexture rt = loadingVideoPlayer.targetTexture;
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = prev;
        }
    }

    private void Start()
    {
        // Start loading GameScene in the background immediately
        asyncLoad = SceneManager.LoadSceneAsync("GameScene");
        asyncLoad.allowSceneActivation = false;

        if (loadingVideoPlayer != null)
        {
            loadingVideoPlayer.loopPointReached += OnVideoEnd;
            loadingVideoPlayer.Play();
        }
        else
        {
            // No video — activate as soon as the scene is ready
            videoFinished = true;
        }
    }

    private void Update()
    {
        if (asyncLoad == null) return;

        // AsyncOperation.progress stops at 0.9 when allowSceneActivation is false;
        // the last 10% is the activation itself.
        if (asyncLoad.progress >= 0.9f)
        {
            sceneReady = true;
        }

        // Activate the scene once both conditions are met
        if (sceneReady && videoFinished)
        {
            asyncLoad.allowSceneActivation = true;
        }
    }

    private void OnVideoEnd(VideoPlayer vp)
    {
        videoFinished = true;

        // If the scene was already done loading, activate right away
        if (sceneReady)
        {
            asyncLoad.allowSceneActivation = true;
        }
    }
}

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

/// <summary>
/// Global scene navigator singleton. Persists across scenes via DontDestroyOnLoad.
/// Any script can call SceneNavigator.Instance.GoToMainScene() etc. without needing
/// a direct reference.
/// </summary>
public class SceneNavigator : MonoBehaviour
{
    public static SceneNavigator Instance { get; private set; }

    [Header("Video Pre-loading")]
    [Tooltip("The MainMenuBGVideo clip to prepare before navigating to MainScene.")]
    [SerializeField] private VideoClip mainMenuBGClip;
    [SerializeField] private RenderTexture mainMenuBGRT;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Navigate to the appropriate scene based on login status and internet connectivity.
    /// If logged in and has internet (or is guest), goes to MainScene. If not, goes to LoginScene.
    /// </summary>
    public void NavigateToGameScene()
    {
        if (StudentLoginManager.IsLoggedIn())
        {
            bool isGuest = PlayerPrefs.GetString("IsGuest", "false") == "true";
            bool hasInternet = Application.internetReachability != NetworkReachability.NotReachable;

            if (!hasInternet && !isGuest)
            {
                StudentLoginManager.Logout();
                GoToLoginScene();
            }
            else
            {
                GoToMainScene();
            }
        }
        else
        {
            GoToLoginScene();
        }
    }

    /// <summary>
    /// Load MainScene. Prepares the background video first so it plays instantly.
    /// </summary>
    public void GoToMainScene()
    {
        PrepareMainMenuVideo();
        SceneManager.LoadScene("MainScene");
    }

    /// <summary>
    /// Load LoginScene.
    /// </summary>
    public void GoToLoginScene()
    {
        SceneManager.LoadScene("LoginScene");
    }

    /// <summary>
    /// Load GameScene behind the loading screen transition.
    /// </summary>
    public void GoToGameScene()
    {
        LoadingScreen.LoadScene("GameScene");
    }

    private void PrepareMainMenuVideo()
    {
        if (VideoManager.Instance == null || mainMenuBGClip == null || mainMenuBGRT == null)
            return;

        VideoManager.Instance.PrepareVideo(
            VideoBackgroundIntro.VIDEO_KEY, mainMenuBGClip, mainMenuBGRT);
    }
}

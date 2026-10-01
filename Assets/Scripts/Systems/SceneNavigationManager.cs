using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

/// <summary>
/// Global scene navigator singleton. Persists across scenes via DontDestroyOnLoad.
/// Any script can call SceneNavigationManager.Instance.GoToMainScene() etc. without needing
/// a direct reference.
/// </summary>
public class SceneNavigationManager : MonoBehaviour
{
    public static SceneNavigationManager Instance { get; private set; }

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
    /// <paramref name="transition"/> off cuts straight to it, as the title screen does.
    /// </summary>
    public void NavigateToGameScene(bool transition = true)
    {
        if (StudentLoginManager.IsLoggedIn())
        {
            bool isGuest = PlayerPrefs.GetString("IsGuest", "false") == "true";
            bool hasInternet = Application.internetReachability != NetworkReachability.NotReachable;

            if (!hasInternet && !isGuest)
            {
                StudentLoginManager.Logout();
                GoToLoginScene(transition);
            }
            else
            {
                GoToMainScene(transition);
            }
        }
        else
        {
            GoToLoginScene(transition);
        }
    }

    /// <summary>
    /// Load MainScene behind the menu transition, or with a plain cut. Prepares the background video first so it plays instantly.
    /// </summary>
    public void GoToMainScene(bool transition = true)
    {
        PrepareMainMenuVideo();
        LoadMenu("MainScene", transition);
    }

    /// <summary>
    /// Load LoginScene behind the menu transition, or with a plain cut.
    /// </summary>
    public void GoToLoginScene(bool transition = true)
    {
        LoadMenu("LoginScene", transition);
    }

    private static void LoadMenu(string sceneName, bool transition)
    {
        if (transition)
            MenuTransition.LoadScene(sceneName);
        else
            SceneManager.LoadScene(sceneName);
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

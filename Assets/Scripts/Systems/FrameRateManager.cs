using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Runs the game at 60 fps, dropping to 30 on screens that sit still - the menus, the pause
/// menu, the Journal, the quiz between minigames and the results - to save battery and keep
/// classroom phones cool over back-to-back drills. Those screens go back to 60 while a finger is
/// on the screen and for a moment after, while a menu animation is playing (see
/// <see cref="KeepSmooth"/>), and as one of them opens, so taps, drags and the transitions they
/// start stay smooth.
/// </summary>
public class FrameRateManager : MonoBehaviour
{
    private const int SMOOTH_FPS = 60;
    private const int STILL_FPS = 30;

    // How long a screen stays at full rate after the last touch, a KeepSmooth call or a screen
    // opening. Covers the popups, page flips and answer pops a tap sets off.
    private const float SMOOTH_HOLD = 1.5f;

    // Menu scenes are still screens all the way through
    private static readonly HashSet<string> stillScenes = new HashSet<string>
    {
        "TitleScene", "LoginScene", "MainScene",
    };

    private static FrameRateManager instance;

    // Panels in the game scene that are currently up and holding still
    private static readonly HashSet<Object> stillScreens = new HashSet<Object>();

    private static float smoothUntil;
    private bool inStillScene;
    private int appliedRate = -1;

    /// <summary>
    /// Marks <paramref name="owner"/>'s screen as up and still (or no longer). Safe to call every
    /// frame. A screen whose owner is destroyed drops out on its own.
    /// </summary>
    public static void SetStillScreen(Object owner, bool still)
    {
        if (owner == null)
            return;

        if (still)
        {
            // Full rate as it opens, for its entrance
            if (stillScreens.Add(owner))
                KeepSmooth();
        }
        else
        {
            stillScreens.Remove(owner);
        }
    }

    /// <summary>
    /// Holds full frame rate for a moment. Animations that can play with no finger down - the
    /// scene transitions and the main menu intro - call this every frame they step.
    /// </summary>
    public static void KeepSmooth()
    {
        smoothUntil = Time.unscaledTime + SMOOTH_HOLD;
    }

    private void Awake()
    {
        // This object survives scene loads, so a second copy arriving with a newly loaded scene
        // would stack up permanently - one more DontDestroyOnLoad object per scene change, each
        // re-applying the same settings. The first one wins and later arrivals delete themselves.
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;

        // On Android - this project's build target - QualitySettings.vSyncCount is ignored and
        // this is what actually governs the frame cap. On a desktop build the quality tiers set
        // vSyncCount (Performant 0, Balanced/High Fidelity 1), and where that is non-zero it
        // takes precedence and this cap has no effect.
        Apply(SMOOTH_FPS);

        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);

        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single)
            return;

        inStillScene = stillScenes.Contains(scene.name);

        // Whatever the new scene opens with plays at full rate
        KeepSmooth();
    }

    private void LateUpdate()
    {
        if (Input.touchCount > 0 || Input.GetMouseButton(0))
            KeepSmooth();

        Apply(IsStill() && Time.unscaledTime >= smoothUntil ? STILL_FPS : SMOOTH_FPS);
    }

    private bool IsStill()
    {
        if (inStillScene)
            return true;

        stillScreens.RemoveWhere(owner => owner == null);
        return stillScreens.Count > 0;
    }

    private void Apply(int rate)
    {
        if (rate == appliedRate)
            return;

        appliedRate = rate;
        Application.targetFrameRate = rate;
    }
}

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class PauseManager : MonoBehaviour
{
    [SerializeField] private Button pauseButton;
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button exitButton;
    [SerializeField] private Image timeStillRunningImage;

    [Header("Sub-Panels")]
    [SerializeField] private Button optionButton;
    [SerializeField] private Button htpButton;
    [SerializeField] private Button aboutButton;
    [SerializeField] private GameObject optionsPanel;
    [SerializeField] private GameObject htpPanel;
    [SerializeField] private GameObject aboutPanel;
    [SerializeField] private Button optionsPanelCloseButton;
    [SerializeField] private Button htpPanelCloseButton;
    [SerializeField] private Button aboutPanelCloseButton;

    private GameTimer timerScript;
    private bool isPaused = false;

    /// <summary>True while the pause menu is up. The onboarding steps aside for it.</summary>
    public bool IsPaused => isPaused;
    public Button PauseButton => pauseButton;

    // Whether the round clock was counting when the game was paused. Continue only restarts
    // it if so: pausing before the round starts, or once the quiz has stopped the clock,
    // must not set it going again.
    private bool timerWasRunning = false;

    /// <summary>Whether Continue will set the round clock going again.</summary>
    public bool TimerWasRunning => timerWasRunning;

    /// <summary>
    /// Opens the pause menu as if the pause button were tapped. A drill resumed after a
    /// replayed practice comes back here, where the player left it.
    /// </summary>
    public void OpenPauseMenu() => OnPauseClicked();

    private void Start()
    {
        // Find the Timer script
        timerScript = FindFirstObjectByType<GameTimer>();

        // Setup pause button
        if (pauseButton != null)
            pauseButton.onClick.AddListener(OnPauseClicked);

        // Setup pause panel buttons
        if (continueButton != null)
            continueButton.onClick.AddListener(OnContinueClicked);

        if (restartButton != null)
            restartButton.onClick.AddListener(OnRestartClicked);

        if (exitButton != null)
            exitButton.onClick.AddListener(OnExitClicked);

        // Setup sub-panel buttons
        if (optionButton != null)
            optionButton.onClick.AddListener(() => ShowSubPanel(optionsPanel));

        if (htpButton != null)
            htpButton.onClick.AddListener(() => ShowSubPanel(htpPanel));

        if (aboutButton != null)
            aboutButton.onClick.AddListener(() => ShowSubPanel(aboutPanel));

        // Setup sub-panel close buttons
        if (optionsPanelCloseButton != null)
            optionsPanelCloseButton.onClick.AddListener(() => HideSubPanel(optionsPanel));

        if (htpPanelCloseButton != null)
            htpPanelCloseButton.onClick.AddListener(() => HideSubPanel(htpPanel));

        if (aboutPanelCloseButton != null)
            aboutPanelCloseButton.onClick.AddListener(() => HideSubPanel(aboutPanel));

        // Hide pause panel initially
        if (pausePanel != null)
            pausePanel.SetActive(false);

        // Hide all sub-panels initially
        if (optionsPanel != null)
            optionsPanel.SetActive(false);
        if (htpPanel != null)
            htpPanel.SetActive(false);
        if (aboutPanel != null)
            aboutPanel.SetActive(false);

        // Check if we're in teacher session mode
        UpdateTimeStillRunningVisibility();
    }

    private void OnPauseClicked()
    {
        if (isPaused)
            return;

        isPaused = true;

        // Show pause panel
        if (pausePanel != null)
            pausePanel.SetActive(true);

        FrameRateManager.SetStillScreen(this, true);

        // Only pause time in offline mode
        if (!IsTeacherSession())
        {
            // Pause timer BEFORE setting timeScale to 0
            timerWasRunning = timerScript != null && timerScript.IsRunning;
            if (timerWasRunning)
            {
                timerScript.PauseTimer();
            }
            Time.timeScale = 0f;
        }
    }

    // A notification or a switch to another app sends the game to the background mid-round.
    // Coming back to the clock already counting is unfair, so it comes back paused instead -
    // offline only, where pausing actually stops the clock. Only while the round clock is
    // running and the pause button is on offer: the quiz, minigames, the splash and anything
    // else that has frozen the game already handle their own state.
    private void OnApplicationPause(bool paused)
    {
        if (!paused || isPaused || IsTeacherSession())
            return;

        if (timerScript == null || !timerScript.IsRunning || Time.timeScale == 0f)
            return;

        if (pauseButton == null || !pauseButton.isActiveAndEnabled || !pauseButton.interactable)
            return;

        OnPauseClicked();
    }

    private void OnContinueClicked()
    {
        if (!isPaused)
            return;

        isPaused = false;

        // Hide pause panel
        if (pausePanel != null)
            pausePanel.SetActive(false);

        FrameRateManager.SetStillScreen(this, false);

        // Only resume time in offline mode
        if (!IsTeacherSession())
        {
            Time.timeScale = 1f;
            if (timerWasRunning && timerScript != null)
            {
                timerScript.ResumeTimer();
            }
            timerWasRunning = false;
        }
    }

    private void OnRestartClicked()
    {
        // Only allow restart in offline mode
        if (IsTeacherSession())
        {
            return;
        }

        // Resume time before restarting
        Time.timeScale = 1f;

        // Reload the current scene
        LoadingScreen.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void OnExitClicked()
    {
        // A teacher session is one drill per student: say what leaving does before it happens
        if (IsTeacherSession())
        {
            QuizManager quiz = FindFirstObjectByType<QuizManager>(FindObjectsInactive.Include);
            bool quizStarted = quiz != null && quiz.HasStarted;

            OnboardingManager.Confirm("LEAVE THE DRILL?",
                quizStarted
                    ? "You can rejoin with the same code and carry on at this question, but " +
                      "<color=#FF4343>its timer keeps running</color> while you're away. A " +
                      "minigame you leave starts over with only the time it had left."
                    : "You can rejoin with the same code and carry on where you left off, but " +
                      "<color=#FF4343>the timer keeps running</color> while you're away.",
                "LEAVE", "STAY", ExitToMenu);
            return;
        }

        ExitToMenu();
    }

    private void ExitToMenu()
    {
        // Saved while the session is still known; clearing it below forgets which one it was
        OnboardingManager.SaveSessionDrill(now: true);

        // Stop music before exiting
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.StopMusic();
        }

        // Resume time before exiting
        Time.timeScale = 1f;

        // Clear SessionCode to ensure offline mode on return
        PlayerPrefs.DeleteKey("SessionCode");
        PlayerPrefs.Save();

        // Load MainScene
        SceneNavigationManager.Instance.GoToMainScene();
    }

    private void UpdateTimeStillRunningVisibility()
    {
        bool isTeacherSession = IsTeacherSession();

        // Show TimeStillRunning image only in teacher session mode
        if (timeStillRunningImage != null)
        {
            timeStillRunningImage.gameObject.SetActive(isTeacherSession);
        }

        // A teacher session is one run per student - its result goes to the dashboard - so
        // restarting is for offline practice only
        if (restartButton != null)
        {
            restartButton.gameObject.SetActive(!isTeacherSession);
        }
    }

    private bool IsTeacherSession()
    {
        string sessionCode = PlayerPrefs.GetString("SessionCode", "");
        return !string.IsNullOrEmpty(sessionCode);
    }

    // The pause buttons stay on screen behind a sub-panel. Its full-screen dimmed backdrop
    // catches taps, so they can't be pressed until the sub-panel is closed.
    private void ShowSubPanel(GameObject subPanel)
    {
        PopupPanelTransition.Show(subPanel);
    }

    private void HideSubPanel(GameObject subPanel)
    {
        PopupPanelTransition.Hide(subPanel);
    }

    private void OnDestroy()
    {
        // Ensure time is resumed if this object is destroyed
        if (isPaused)
        {
            Time.timeScale = 1f;
        }
    }
}

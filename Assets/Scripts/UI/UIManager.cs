using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using TMPro;

public class UIManager : MonoBehaviour
{
    [Header("Left Menu Panels")]
    [SerializeField] private GameObject optionsPanel;
    [SerializeField] private GameObject howToPlayPanel;
    [SerializeField] private GameObject aboutPanel;
    [SerializeField] private GameObject switchPanel;

    [Header("User Display")]
    [SerializeField] private TextMeshProUGUI userDisplayName;
    [SerializeField] private TextMeshProUGUI studentIdText;
    [SerializeField] private Image statusIcon;
    [SerializeField] private GameObject playerCard;
    [SerializeField] private Image avatarImage;
    [SerializeField] private Sprite femaleAvatarSprite;
    [SerializeField] private Sprite maleAvatarSprite;

    [Header("Main Menu Buttons")]
    [SerializeField] private Button optionsButton;
    [SerializeField] private Button howToPlayMenuButton;
    [SerializeField] private Button aboutButton;
    [SerializeField] private Button switchButton;       // Customize button
    [SerializeField] private Button exitButton;
    [SerializeField] private Button logoutButton;          // Inside Options panel
    [SerializeField] private Button optionsPanelCloseButton;
    [SerializeField] private Button howToPlayPanelCloseButton;
    [SerializeField] private Button aboutPanelCloseButton;

    [Header("Play Menu Panels")]
    [SerializeField] private GameObject teacherSessionPanel;
    [SerializeField] private GameObject offlineModePanel;
    [SerializeField] private GameObject joinRoomPanel;
    [SerializeField] private GameObject difficultyPanel;
    [Tooltip("The menu's left sidebar - hidden while the full-screen play menu panels are open.")]
    [SerializeField] private GameObject leftPanelBackground;

    [Header("Play Menu Buttons")]
    [SerializeField] private Button playButton;         // Main PLAY button
    [SerializeField] private Button teacherSessionPlayButton;
    [SerializeField] private Button offlineModePlayButton;  // Offline Mode PLAY button
    [SerializeField] private Button gameModeNextButton;     // NEXT button - appears once a game mode is selected
    [SerializeField] private GameObject teacherSessionDescription;
    [SerializeField] private GameObject offlineModeDescription;
    [SerializeField] private Button startGameButton;    // START button on difficulty panel
    [SerializeField] private Button backArrowButton;
    [SerializeField] private Button difficultyBackButton;   // BACK button on difficulty panel

    [Header("Audio")]
    [SerializeField] private AudioClip mainMenuBGM;
    [SerializeField] private AudioClip buttonClickAudio;

    private const string SELECTED_CHARACTER_SUFFIX = "_SelectedCharacter";

    private enum GameMode { None, TeacherSession, OfflineMode }

    private GameObject currentLeftMenuPanel;
    private Stack<string> navigationStack = new Stack<string>();
    private GameMode selectedGameMode = GameMode.None;

    void Start()
    {
        // Hide all panels initially
        HideAllPanels();

        // Initialize the user display name
        UpdateUserDisplayName();
        RefreshAvatar();
        if (optionsButton != null)
            optionsButton.onClick.AddListener(() => { PlayButtonAudio(); ToggleLeftMenuPanel(optionsPanel, "options"); });
        
        if (howToPlayMenuButton != null)
            howToPlayMenuButton.onClick.AddListener(() => { PlayButtonAudio(); ToggleLeftMenuPanel(howToPlayPanel, "howToPlay"); });
        
        if (aboutButton != null)
            aboutButton.onClick.AddListener(() => { PlayButtonAudio(); ToggleLeftMenuPanel(aboutPanel, "about"); });

        if (switchButton != null)
            switchButton.onClick.AddListener(() => { PlayButtonAudio(); ToggleLeftMenuPanel(switchPanel, "switch"); });

        if (exitButton != null)
            exitButton.onClick.AddListener(OnExitClicked);

        if (optionsPanelCloseButton != null)
            optionsPanelCloseButton.onClick.AddListener(CloseLeftMenuPanel);

        if (howToPlayPanelCloseButton != null)
            howToPlayPanelCloseButton.onClick.AddListener(CloseLeftMenuPanel);

        if (aboutPanelCloseButton != null)
            aboutPanelCloseButton.onClick.AddListener(CloseLeftMenuPanel);

        if (logoutButton != null)
            logoutButton.onClick.AddListener(OnLogoutClicked);

        // Setup play menu button listeners
        if (playButton != null)
            playButton.onClick.AddListener(() => { PlayButtonAudio(); PlayMainMenuOutro(ShowPlayMenu); });

        // Tapping a game mode card only selects it (tapping it again deselects it) - NEXT opens it
        if (teacherSessionPlayButton != null)
            teacherSessionPlayButton.onClick.AddListener(() => ToggleGameMode(GameMode.TeacherSession));

        if (offlineModePlayButton != null)
            offlineModePlayButton.onClick.AddListener(() => ToggleGameMode(GameMode.OfflineMode));

        if (gameModeNextButton != null)
            gameModeNextButton.onClick.AddListener(OnGameModeNextClicked);

        if (startGameButton != null)
            // No outro here - the difficulty panel stays as it is for the loading wipe to sweep over
            startGameButton.onClick.AddListener(() => { PlayButtonAudio(); StartGame(); });

        if (difficultyBackButton != null)
            difficultyBackButton.onClick.AddListener(() => { PlayButtonAudio(); PlayDifficultyOutro(GoBack); });

        if (backArrowButton != null)
            backArrowButton.onClick.AddListener(OnBackPressed);

        // BGM is started by VideoBackgroundIntro.
    }

    /// <summary>
    /// Starts the main menu background music. Called by VideoBackgroundIntro.
    /// </summary>
    public void PlayMainMenuBGM()
    {
        if (mainMenuBGM != null && SoundManager.Instance != null)
            SoundManager.Instance.PlayMusic(mainMenuBGM);
    }

    void OnDestroy()
    {
        // Stop music when leaving main menu
        if (SoundManager.Instance != null)
            SoundManager.Instance.StopMusic();
    }

    void HideAllPanels()
    {
        optionsPanel.SetActive(false);
        howToPlayPanel.SetActive(false);
        aboutPanel.SetActive(false);
        switchPanel.SetActive(false);
        teacherSessionPanel.SetActive(false);
        offlineModePanel.SetActive(false);
        joinRoomPanel.SetActive(false);
        difficultyPanel.SetActive(false);
        SelectGameMode(GameMode.None, false);
    }

    private void ToggleGameMode(GameMode mode)
    {
        SelectGameMode(selectedGameMode == mode ? GameMode.None : mode, true);
    }

    /// <summary>
    /// Highlights the chosen game mode card, shows only its description, and shows NEXT
    /// while a mode is selected. <see cref="GameMode.None"/> clears the selection.
    /// </summary>
    private void SelectGameMode(GameMode mode, bool animate)
    {
        selectedGameMode = mode;

        SetGameModeCardSelected(teacherSessionPlayButton, teacherSessionDescription, mode == GameMode.TeacherSession, animate);
        SetGameModeCardSelected(offlineModePlayButton, offlineModeDescription, mode == GameMode.OfflineMode, animate);
        SetNextButtonVisible(mode != GameMode.None, animate);
    }

    private void SetNextButtonVisible(bool visible, bool animate)
    {
        if (gameModeNextButton == null)
            return;

        GameObject next = gameModeNextButton.gameObject;
        UIScreenTransition transition = next.GetComponent<UIScreenTransition>();

        if (visible)
        {
            if (!next.activeSelf)
                next.SetActive(true);           // slides in on enable
            else if (nextButtonHiding && transition != null)
                transition.PlayIn();            // reselected while it was sliding out
            nextButtonHiding = false;
        }
        else if (next.activeSelf && !nextButtonHiding)
        {
            if (animate && transition != null && transition.isActiveAndEnabled)
            {
                nextButtonHiding = true;
                transition.PlayOut(() =>
                {
                    // PlayIn cancels this callback if a card is reselected mid-slide
                    nextButtonHiding = false;
                    next.SetActive(false);
                });
            }
            else
            {
                next.SetActive(false);
            }
        }
        else if (!animate)
        {
            nextButtonHiding = false;
            next.SetActive(false);
        }
    }

    private bool nextButtonHiding;

    private const float DescriptionAnimDuration = 0.25f;
    private const float DescriptionAnimRise = 16f;
    private readonly Dictionary<GameObject, Vector2> descriptionHomes = new Dictionary<GameObject, Vector2>();
    private readonly Dictionary<GameObject, Coroutine> descriptionAnims = new Dictionary<GameObject, Coroutine>();

    /// <summary>
    /// Fades a card description in while rising into place, or fades it back down and hides it.
    /// </summary>
    private void SetDescriptionVisible(GameObject description, bool visible, bool animate)
    {
        RectTransform rect = (RectTransform)description.transform;
        if (!descriptionHomes.ContainsKey(description))
            descriptionHomes[description] = rect.anchoredPosition;

        CanvasGroup group = description.GetComponent<CanvasGroup>();
        if (group == null)
            group = description.AddComponent<CanvasGroup>();
        // Clicks go to the card underneath
        group.blocksRaycasts = false;

        Coroutine running;
        if (descriptionAnims.TryGetValue(description, out running) && running != null)
            StopCoroutine(running);
        descriptionAnims[description] = null;

        if (!animate || !isActiveAndEnabled || !description.transform.parent.gameObject.activeInHierarchy)
        {
            group.alpha = visible ? 1f : 0f;
            rect.anchoredPosition = descriptionHomes[description];
            description.SetActive(visible);
            return;
        }

        if (!visible && !description.activeSelf)
            return;

        if (visible && !description.activeSelf)
        {
            group.alpha = 0f;
            description.SetActive(true);
        }

        descriptionAnims[description] = StartCoroutine(AnimateDescription(description, rect, group, visible));
    }

    private System.Collections.IEnumerator AnimateDescription(GameObject description, RectTransform rect, CanvasGroup group, bool visible)
    {
        Vector2 home = descriptionHomes[description];
        // Continue from wherever an interrupted animation left off
        float progress = visible ? group.alpha : 1f - group.alpha;

        while (progress < 1f)
        {
            float step = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            progress = Mathf.Min(progress + step / DescriptionAnimDuration, 1f);

            float shown = visible ? progress : 1f - progress;
            float eased = 1f - Mathf.Pow(1f - shown, 3f);
            group.alpha = shown;
            rect.anchoredPosition = home + Vector2.down * DescriptionAnimRise * (1f - eased);
            yield return null;
        }

        if (!visible)
        {
            description.SetActive(false);
            rect.anchoredPosition = home;
        }

        descriptionAnims[description] = null;
    }

    private void SetGameModeCardSelected(Button card, GameObject description, bool selected, bool animate)
    {
        if (description != null)
            SetDescriptionVisible(description, selected, animate);

        if (card == null)
            return;

        // The card's red fill is revealed by its tint: see-through unless selected or pressed
        Color shown = Color.white;
        Color hidden = new Color(1f, 1f, 1f, 0f);

        ColorBlock colors = card.colors;
        colors.normalColor = selected ? shown : hidden;
        colors.highlightedColor = selected ? shown : hidden;
        colors.selectedColor = selected ? shown : hidden;
        colors.pressedColor = shown;
        card.colors = colors;
    }

    private void OnGameModeNextClicked()
    {
        if (selectedGameMode == GameMode.None)
            return;

        if (selectedGameMode == GameMode.TeacherSession)
            // BACK stays on screen between game mode and join room, so only the cards and NEXT leave
            PlayScreenOutro(ShowJoinRoomPanel, teacherSessionPanel, offlineModePanel, gameModeNextButton.gameObject);
        else
            // BACK doesn't animate here: the difficulty panel has its own BACK in the same spot
            PlayScreenOutro(ShowDifficultyPanel, teacherSessionPanel, offlineModePanel, gameModeNextButton.gameObject);
    }

    private void ToggleLeftMenuPanel(GameObject panel, string panelName)
    {
        // If the panel is already open, close it
        if (currentLeftMenuPanel == panel)
        {
            PopupPanelTransition.Hide(panel);
            currentLeftMenuPanel = null;
        }
        // If another panel is open, close it and open the new one
        else
        {
            if (currentLeftMenuPanel != null)
            {
                PopupPanelTransition.Hide(currentLeftMenuPanel);
            }

            PopupPanelTransition.Show(panel);
            currentLeftMenuPanel = panel;
        }
    }

    private void CloseLeftMenuPanel()
    {
        if (currentLeftMenuPanel != null)
        {
            PopupPanelTransition.Hide(currentLeftMenuPanel);
            currentLeftMenuPanel = null;
        }
    }

    private void ShowPlayMenu()
    {
        navigationStack.Clear();
        navigationStack.Push("mainMenu");
        
        HideAllPanels();
        
        if (playerCard != null)
            playerCard.SetActive(false);
        SetMainMenuButtonsVisible(false);

        // The game mode screen is full width - no sidebar behind it
        if (leftPanelBackground != null)
            leftPanelBackground.SetActive(false);

        teacherSessionPanel.SetActive(true);
        offlineModePanel.SetActive(true);
        backArrowButton.gameObject.SetActive(true);
        navigationStack.Push("playMenu");
    }

    /// <summary>
    /// Reverses the main menu intro (menu items and player card) before leaving the main menu.
    /// </summary>
    private void PlayMainMenuOutro(System.Action onComplete)
    {
        MainMenuIntroAnimator introAnimator = GetComponent<MainMenuIntroAnimator>();

        if (introAnimator != null)
            introAnimator.PlayOutro(onComplete);
        else
            onComplete();
    }

    private bool screenTransitioning;

    /// <summary>
    /// Plays the leave-animation of every given screen that has a UIScreenTransition, then runs
    /// <paramref name="onComplete"/> once all of them have finished. Taps are ignored meanwhile.
    /// </summary>
    private void PlayScreenOutro(System.Action onComplete, params GameObject[] screens)
    {
        if (screenTransitioning)
            return;

        var transitions = new List<UIScreenTransition>();
        foreach (GameObject screen in screens)
        {
            UIScreenTransition transition = screen != null ? screen.GetComponent<UIScreenTransition>() : null;
            if (transition != null && transition.isActiveAndEnabled)
                transitions.Add(transition);
        }

        if (transitions.Count == 0)
        {
            onComplete();
            return;
        }

        screenTransitioning = true;
        int pending = transitions.Count;

        foreach (UIScreenTransition transition in transitions)
        {
            transition.PlayOut(() =>
            {
                pending--;
                if (pending > 0)
                    return;

                screenTransitioning = false;
                onComplete();
            });
        }
    }

    /// <summary>
    /// The shared BACK button: animates the current play menu screen out before going back.
    /// </summary>
    private void OnBackPressed()
    {
        string currentScreen = navigationStack.Count > 0 ? navigationStack.Peek() : "";

        if (currentScreen == "playMenu")
            PlayScreenOutro(GoBack, teacherSessionPanel, offlineModePanel, backArrowButton.gameObject, gameModeNextButton != null ? gameModeNextButton.gameObject : null);
        else if (currentScreen == "joinRoom")
            PlayScreenOutro(GoBack, joinRoomPanel);
        else
            GoBack();
    }

    private void ShowJoinRoomPanel()
    {
        navigationStack.Push("joinRoom");
        teacherSessionPanel.SetActive(false);
        offlineModePanel.SetActive(false);
        SelectGameMode(GameMode.None, false);
        joinRoomPanel.SetActive(true);
    }

    private void GoBack()
    {
        if (navigationStack.Count == 0)
            return;

        string currentScreen = navigationStack.Pop();

        if (currentScreen == "playMenu")
        {
            // Return to main menu
            HideAllPanels();
            if (playerCard != null)
                playerCard.SetActive(true);
            SetMainMenuButtonsVisible(true);
            backArrowButton.gameObject.SetActive(false);
            if (leftPanelBackground != null)
                leftPanelBackground.SetActive(true);

            // PLAY's outro left the menu and card hidden - bring them back in
            MainMenuIntroAnimator introAnimator = GetComponent<MainMenuIntroAnimator>();
            if (introAnimator != null)
                introAnimator.ReplayIntro();
        }
        else if (currentScreen == "joinRoom")
        {
            // Return to play menu (Teacher Session and Offline Mode)
            HideAllPanels();
            teacherSessionPanel.SetActive(true);
            offlineModePanel.SetActive(true);
        }
        else if (currentScreen == "difficulty")
        {
            // Return to play menu (Teacher Session and Offline Mode)
            HideAllPanels();
            teacherSessionPanel.SetActive(true);
            offlineModePanel.SetActive(true);

            // Coming back from the difficulty panel, whose BACK sat in the same spot, so this
            // BACK simply reappears instead of sliding in
            UIScreenTransition backTransition = backArrowButton.GetComponent<UIScreenTransition>();
            if (backTransition != null)
                backTransition.SkipNextPlayIn();
            backArrowButton.gameObject.SetActive(true);
        }
    }

    private void ShowDifficultyPanel()
    {
        navigationStack.Push("difficulty");
        HideAllPanels();
        // The difficulty panel has its own BACK button
        backArrowButton.gameObject.SetActive(false);
        if (leftPanelBackground != null)
            leftPanelBackground.SetActive(false);
        difficultyPanel.SetActive(true);
    }

    /// <summary>
    /// Lets the difficulty panel slide its bases out before leaving it.
    /// </summary>
    private void PlayDifficultyOutro(System.Action onComplete)
    {
        DifficultyPanelManager manager = difficultyPanel != null
            ? difficultyPanel.GetComponent<DifficultyPanelManager>()
            : null;

        if (manager != null && manager.isActiveAndEnabled)
            manager.PlayOutro(onComplete);
        else
            onComplete();
    }

    private void StartGame()
    {
        // Clear SessionCode for offline mode (don't treat it as a teacher session)
        PlayerPrefs.DeleteKey("SessionCode");
        PlayerPrefs.Save();

        LoadingScreen.LoadScene("GameScene");
    }

    // Reset the current left menu panel tracking (called when panels close)
    public void ResetLeftMenuPanel()
    {
        currentLeftMenuPanel = null;
    }

    private void OnExitClicked()
    {
        PlayButtonAudio();
        Application.Quit();

        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #endif
    }

    private void OnLogoutClicked()
    {
        PlayButtonAudio();
        StudentLoginManager.Logout();
        SceneManager.LoadScene("LoginScene");
    }

    private void SetMainMenuButtonsVisible(bool visible)
    {
        if (playButton != null) playButton.gameObject.SetActive(visible);
        if (optionsButton != null) optionsButton.gameObject.SetActive(visible);
        if (switchButton != null) switchButton.gameObject.SetActive(visible);
        if (exitButton != null) exitButton.gameObject.SetActive(visible);
        if (howToPlayMenuButton != null) howToPlayMenuButton.gameObject.SetActive(visible);
        if (aboutButton != null) aboutButton.gameObject.SetActive(visible);
    }

    private void PlayButtonAudio()
    {
        if (buttonClickAudio != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(buttonClickAudio);
    }

    /// <summary>
    /// Shows the avatar of the character the current user picked in the Customize panel.
    /// Public so SwitchPanelManager can refresh the card as soon as a new pick is confirmed.
    /// </summary>
    public void RefreshAvatar()
    {
        if (avatarImage == null)
            return;

        bool isGuest = PlayerPrefs.GetString("IsGuest", "false") == "true";
        string userName = isGuest ? "Guest" : PlayerPrefs.GetString("StudentName", "User");
        string selectedCharacter = PlayerPrefs.GetString(userName + SELECTED_CHARACTER_SUFFIX, "Female");

        Sprite avatar = selectedCharacter == "Male" ? maleAvatarSprite : femaleAvatarSprite;
        if (avatar != null)
            avatarImage.sprite = avatar;

        // Keep the empty photo frame showing if a sprite hasn't been assigned.
        avatarImage.enabled = avatar != null;
    }

    private void UpdateUserDisplayName()
    {
        if (userDisplayName == null)
            return;

        bool isGuest = PlayerPrefs.GetString("IsGuest", "false") == "true";

        if (isGuest)
        {
            userDisplayName.text = "Guest";
            if (studentIdText != null)
                studentIdText.text = "ID#: N/A";
            if (statusIcon != null)
                statusIcon.color = new Color(0.502f, 0.502f, 0.502f, 1f); // #808080
        }
        else
        {
            string studentName = PlayerPrefs.GetString("StudentName", "User");
            string studentId = PlayerPrefs.GetString("StudentUsername", "");
            userDisplayName.text = studentName;
            if (studentIdText != null)
                studentIdText.text = "ID#: " + (string.IsNullOrEmpty(studentId) ? "N/A" : studentId.ToUpper());
            if (statusIcon != null)
                statusIcon.color = new Color(0.357f, 0.682f, 0.235f, 1f); // #5BAE3C
        }
    }
}

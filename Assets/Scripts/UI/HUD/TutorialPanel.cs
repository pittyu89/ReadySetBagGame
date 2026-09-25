using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TutorialPanel : MonoBehaviour
{
    [SerializeField] private GameObject tutorialPanel;
    [SerializeField] private TextMeshProUGUI tutorialTitleText;
    [SerializeField] private GameObject tutorialTitle;
    [SerializeField] private TextMeshProUGUI tutorialDescription;
    [SerializeField] private Image tutorialImage;
    [SerializeField] private Button leftButton;
    [SerializeField] private Button rightButton;
    [SerializeField] private Button skipButton;
    [SerializeField] private Button startButton;

    // Optional: shown when the tutorial is dismissed. Left unassigned on the
    // pause menu's How-To-Play panel, which just closes without a splash.
    [SerializeField] private GameObject readySetBagOverlay;

    [Tooltip("Starts the round timer the moment this tutorial is dismissed. Only the " +
             "tutorial that opens the round sets this - the pause menu's How-To-Play " +
             "panel leaves it off so reading the help never starts the clock.")]
    [SerializeField] private bool startsGameTimer = false;

    private int currentTutorialIndex = 0;

    [SerializeField] private string[] titles = new string[] { };
    [SerializeField] private string[] descriptions = new string[] { };
    [SerializeField] private Sprite[] images = new Sprite[] { };
    [SerializeField] private float minTitleWidth = 217f;
    [SerializeField] private float maxTitleWidth = 240f;
    [SerializeField] private float titlePadding = 40f;

    void Start()
    {
        if (skipButton != null)
            skipButton.onClick.AddListener(CloseTutorial);

        if (leftButton != null)
            leftButton.onClick.AddListener(NavigateLeft);

        if (rightButton != null)
            rightButton.onClick.AddListener(NavigateRight);

        if (startButton != null)
            startButton.onClick.AddListener(CloseTutorial);

        UpdateDisplay();

        // Hide start button initially
        if (startButton != null)
            startButton.gameObject.SetActive(false);

        if (tutorialPanel != null)
            tutorialPanel.SetActive(true);
    }

    private void OnEnable()
    {
        // Reset tutorial to first page when opened
        currentTutorialIndex = 0;
        UpdateDisplay();
    }

    private void NavigateLeft()
    {
        if (currentTutorialIndex > 0)
        {
            currentTutorialIndex--;
            UpdateDisplay();
        }
    }

    private void NavigateRight()
    {
        if (currentTutorialIndex < titles.Length - 1)
        {
            currentTutorialIndex++;
            UpdateDisplay();
        }
    }

    private void UpdateDisplay()
    {
        if (titles.Length == 0)
            return;

        if (tutorialTitleText != null)
            tutorialTitleText.text = titles[currentTutorialIndex];

        if (tutorialDescription != null && currentTutorialIndex < descriptions.Length)
            tutorialDescription.text = descriptions[currentTutorialIndex];

        if (tutorialImage != null && currentTutorialIndex < images.Length && images[currentTutorialIndex] != null)
            tutorialImage.sprite = images[currentTutorialIndex];

        // The title pill hugs its text within the design widths; long titles
        // shrink their font (TMP auto-size) instead of stretching the pill
        if (tutorialTitle != null && tutorialTitleText != null)
        {
            RectTransform titleRect = tutorialTitle.GetComponent<RectTransform>();
            Vector2 sizeDelta = titleRect.sizeDelta;
            float preferred = tutorialTitleText.GetPreferredValues(titles[currentTutorialIndex]).x + titlePadding * 2f;
            sizeDelta.x = Mathf.Clamp(preferred, minTitleWidth, maxTitleWidth);
            titleRect.sizeDelta = sizeDelta;
        }

        // Hide left button at start
        if (leftButton != null)
            leftButton.gameObject.SetActive(currentTutorialIndex > 0);

        // Hide right button at end
        if (rightButton != null)
            rightButton.gameObject.SetActive(currentTutorialIndex < titles.Length - 1);

        // Skip is only offered on the first page
        if (skipButton != null)
            skipButton.gameObject.SetActive(currentTutorialIndex == 0);

        // Show start button only at the end
        if (startButton != null)
            startButton.gameObject.SetActive(currentTutorialIndex == titles.Length - 1);
    }

    private void CloseTutorial()
    {
        // Activate the splash before hiding the panel — the overlay owns its
        // own timing, so it keeps running once this panel is deactivated.
        ReadySetBagOverlay splash = null;

        if (readySetBagOverlay != null)
        {
            splash = readySetBagOverlay.GetComponent<ReadySetBagOverlay>();

            // Subscribed before the SetActive, since that is what starts the splash
            if (splash != null)
                splash.Finished += OnSplashFinished;

            readySetBagOverlay.SetActive(true);
        }

        if (tutorialPanel != null)
            tutorialPanel.SetActive(false);

        // The clock waits for the splash to clear, so the two-odd seconds of
        // "READY-SET-BAG!!" are not counted against a player who cannot see the room
        // yet. With no splash to wait on there is nothing to cover, so it starts now.
        if (splash == null)
            StartGameTimer();
    }

    private void OnSplashFinished()
    {
        ReadySetBagOverlay splash = readySetBagOverlay != null
            ? readySetBagOverlay.GetComponent<ReadySetBagOverlay>()
            : null;

        // One start per dismissal — the splash can be shown again on a replay
        if (splash != null)
            splash.Finished -= OnSplashFinished;

        StartGameTimer();
    }

    /// <summary>
    /// Puts the round clock in motion once the tutorial and its splash are both out of
    /// the way, so the run is timed from the first moment the player can actually move.
    /// </summary>
    private void StartGameTimer()
    {
        if (!startsGameTimer)
            return;

        GameTimer timer = FindFirstObjectByType<GameTimer>();
        if (timer != null)
            timer.StartTimer();
    }
}

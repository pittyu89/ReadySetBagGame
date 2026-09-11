using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TutorialManager : MonoBehaviour
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
    [SerializeField] private float[] tutorialTitleWidths = new float[] { 200, 250, 280, 560, 360, 220, 420, 200 };

    private float originalTutorialTitleY = 0f;

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

        if (tutorialDescription != null)
            tutorialDescription.text = descriptions[currentTutorialIndex];

        if (tutorialImage != null && images[currentTutorialIndex] != null)
            tutorialImage.sprite = images[currentTutorialIndex];

        // Adjust tutorialTitle anchor for 6th tutorial (index 5)
        if (tutorialTitle != null)
        {
            RectTransform titleRect = tutorialTitle.GetComponent<RectTransform>();
            
            // Set anchor and pivot to bottom center on page 6, top center otherwise
            if (currentTutorialIndex == 5)
            {
                titleRect.anchorMin = new Vector2(0.5f, 0f);
                titleRect.anchorMax = new Vector2(0.5f, 0f);
                titleRect.pivot = new Vector2(0.5f, 0f);
            }
            else
            {
                titleRect.anchorMin = new Vector2(0.5f, 1f);
                titleRect.anchorMax = new Vector2(0.5f, 1f);
                titleRect.pivot = new Vector2(0.5f, 1f);
            }

            // Set width based on page
            if (currentTutorialIndex < tutorialTitleWidths.Length)
            {
                Vector2 sizeDelta = titleRect.sizeDelta;
                sizeDelta.x = tutorialTitleWidths[currentTutorialIndex];
                titleRect.sizeDelta = sizeDelta;
            }
        }

        // Hide left button at start
        if (leftButton != null)
            leftButton.gameObject.SetActive(currentTutorialIndex > 0);

        // Hide right button at end
        if (rightButton != null)
            rightButton.gameObject.SetActive(currentTutorialIndex < titles.Length - 1);

        // Hide skip button at end
        if (skipButton != null)
            skipButton.gameObject.SetActive(currentTutorialIndex < titles.Length - 1);

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

        Timer timer = FindObjectOfType<Timer>();
        if (timer != null)
            timer.StartTimer();
    }
}

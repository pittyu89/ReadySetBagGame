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

    [Tooltip("Starts the guided practice run over. Hidden in teacher sessions.")]
    [SerializeField] private Button replayPracticeButton;

    // The How-to-Play slideshow, behind the How-to-Play buttons on the main menu and the pause
    // menu. It used to open every round as well; first-time players now get the guided practice
    // run instead (OnboardingManager), which also owns the round's READY-SET-BAG splash.

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

        if (replayPracticeButton != null)
            replayPracticeButton.onClick.AddListener(OnboardingManager.ReplayPractice);

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

        // Checked each time it opens: the same panel sits in the pause menu of a teacher session
        if (replayPracticeButton != null)
            replayPracticeButton.gameObject.SetActive(OnboardingManager.CanReplayPractice);
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
        if (tutorialPanel != null)
            tutorialPanel.SetActive(false);
    }
}

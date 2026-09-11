using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

/// <summary>
/// Handles the teacher session mode results panel.
/// Displays quiz score, remaining time, and difficulty.
/// </summary>
public class TeacherSessionResultsPanel : MonoBehaviour
{
    [Header("Results Display")]
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI timeText;
    [SerializeField] private TextMeshProUGUI difficultyText;

    [Header("Buttons")]
    [SerializeField] private Button tryAgainButton;
    [SerializeField] private Button menuButton;

    private void Start()
    {
        // Setup button listeners
        if (tryAgainButton != null)
            tryAgainButton.onClick.AddListener(OnTryAgainClicked);

        if (menuButton != null)
            menuButton.onClick.AddListener(OnMenuClicked);
    }

    /// <summary>
    /// Display the quiz results on this panel.
    /// </summary>
    public void DisplayResults(int score, int totalQuestions, float remainingTime, string difficulty,
                               DrillScore.Result drill)
    {
        // The drill grade out of 100, not the raw answer count
        if (scoreText != null)
        {
            scoreText.text = $"{drill.FinalScore}/100";
        }

        // Display time in MM:SS format
        if (timeText != null)
        {
            int minutes = Mathf.FloorToInt(remainingTime / 60f);
            int seconds = Mathf.FloorToInt(remainingTime % 60f);
            string timeDisplay = string.Format("{0:00}:{1:00}", minutes, seconds);
            timeText.text = timeDisplay;
        }

        // Display difficulty (capitalize first letter)
        if (difficultyText != null)
        {
            string displayDifficulty = difficulty.Length > 0 
                ? char.ToUpper(difficulty[0]) + difficulty.Substring(1) 
                : "Unknown";
            difficultyText.text = displayDifficulty;
        }
    }

    private void OnTryAgainClicked()
    {
        // Reload the current scene to restart the game
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void OnMenuClicked()
    {
        // Load the main menu scene
        SceneNavigator.Instance.GoToMainScene();
    }
}

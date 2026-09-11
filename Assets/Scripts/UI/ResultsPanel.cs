using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Linq;
using Firebase.Firestore;
using Firebase.Extensions;

/// <summary>
/// Results panel manager for both offline/guest and teacher session modes.
/// References both panel GameObjects and displays the appropriate one.
/// </summary>
public class ResultsPanel : MonoBehaviour
{
    [Header("Panel References")]
    [SerializeField] private GameObject offlinePanel;
    [SerializeField] private GameObject teacherPanel;

    [Header("Offline Mode")]
    [SerializeField] private TextMeshProUGUI offlineScoreText;
    [SerializeField] private TextMeshProUGUI offlineTimeText;
    [SerializeField] private TextMeshProUGUI offlineDifficultyText;
    [Tooltip("Skill stage badge for the drill grade. Optional.")]
    [SerializeField] private TextMeshProUGUI offlineBadgeText;
    [Tooltip("Where the 100 points came from. Optional.")]
    [SerializeField] private TextMeshProUGUI offlineBreakdownText;
    [SerializeField] private Button tryAgainButton;
    [SerializeField] private Button menuButton;

    [Header("Teacher Session Mode")]
    [SerializeField] private TextMeshProUGUI teacherScoreText;
    [SerializeField] private TextMeshProUGUI teacherTimeText;
    [SerializeField] private TextMeshProUGUI teacherDifficultyText;
    [Tooltip("Skill stage badge for the drill grade. Optional.")]
    [SerializeField] private TextMeshProUGUI teacherBadgeText;
    [Tooltip("Where the 100 points came from. Optional.")]
    [SerializeField] private TextMeshProUGUI teacherBreakdownText;

    private Coroutine sessionCheckCoroutine;
    private FirebaseFirestore db;
    private ListenerRegistration sessionListener;

    private void Start()
    {
        // Setup button listeners
        if (tryAgainButton != null)
            tryAgainButton.onClick.AddListener(OnTryAgainClicked);

        if (menuButton != null)
            menuButton.onClick.AddListener(OnMenuClicked);
    }

    private void OnDestroy()
    {
        // Stop monitoring session status if it's running
        if (sessionCheckCoroutine != null)
        {
            StopCoroutine(sessionCheckCoroutine);
        }

        // Unsubscribe from Firestore listener
        if (sessionListener != null)
        {
            sessionListener.Stop();
            sessionListener = null;
        }
    }

    /// <summary>
    /// Display the quiz results on the appropriate panel based on mode.
    /// </summary>
    /// <summary>
    /// The three lines that make up the 100, plus anything that was taken off — so a low
    /// grade says which part of the drill went wrong rather than just being a number.
    /// </summary>
    private static string BuildBreakdown(int score, int totalQuestions, DrillScore.Result drill)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"Packing {drill.PackingPercent * 100f:0}%  ({drill.PackingPoints:0.0}/{DrillScore.PACKING_WEIGHT:0} pts)   {drill.EssentialsPacked}/{drill.EssentialsTarget} essentials");
        sb.AppendLine($"Scenarios {drill.QuizPercent * 100f:0}%  ({drill.QuizPoints:0.0}/{DrillScore.QUIZ_WEIGHT:0} pts)   {score}/{totalQuestions} correct");

        if (drill.SpeedGatePassed)
            sb.AppendLine($"Time {drill.TimePercent * 100f:0}%  ({drill.TimePoints:0.0}/{DrillScore.TIME_WEIGHT:0} pts)");
        else
            sb.AppendLine($"Time 0 pts  (needs Packing and Scenarios both at {DrillScore.SPEED_GATE:0}%)");

        if (drill.JunkCount > 0)
            sb.AppendLine($"-{drill.JunkCount * DrillScore.JUNK_PENALTY} for {drill.JunkCount} unnecessary item(s)");

        if (drill.OverWeight)
            sb.AppendLine($"-{DrillScore.OVERWEIGHT_PENALTY} for going over the weight limit");

        return sb.ToString().TrimEnd();
    }

    public void DisplayResults(int score, int totalQuestions, float remainingTime, string difficulty,
                               DrillScore.Result drill)
    {
        // Determine mode
        string sessionCode = PlayerPrefs.GetString("SessionCode", "");
        bool isTeacherSession = !string.IsNullOrEmpty(sessionCode);

        // Format time as MM:SS
        int minutes = Mathf.FloorToInt(remainingTime / 60f);
        int seconds = Mathf.FloorToInt(remainingTime % 60f);
        string timeDisplay = string.Format("{0:00}:{1:00}", minutes, seconds);

        // Format difficulty (capitalize first letter)
        string displayDifficulty = difficulty.Length > 0 
            ? char.ToUpper(difficulty[0]) + difficulty.Substring(1) 
            : "Unknown";

        if (isTeacherSession)
        {
            // Show teacher panel, hide offline panel
            if (teacherPanel != null)
                teacherPanel.SetActive(true);
            if (offlinePanel != null)
                offlinePanel.SetActive(false);

            // Display results for teacher session mode
            if (teacherScoreText != null)
            {
                teacherScoreText.text = $"{drill.FinalScore}/100";
            }

            if (teacherBadgeText != null)
                teacherBadgeText.text = drill.Badge;

            if (teacherBreakdownText != null)
                teacherBreakdownText.text = BuildBreakdown(score, totalQuestions, drill);

            if (teacherTimeText != null)
            {
                teacherTimeText.text = timeDisplay;
            }

            if (teacherDifficultyText != null)
            {
                teacherDifficultyText.text = displayDifficulty;
            }
        }
        else
        {
            // Show offline panel, hide teacher panel
            if (offlinePanel != null)
                offlinePanel.SetActive(true);
            if (teacherPanel != null)
                teacherPanel.SetActive(false);

            // Display results for offline mode
            if (offlineScoreText != null)
            {
                offlineScoreText.text = $"{drill.FinalScore}/100";
            }

            if (offlineBadgeText != null)
                offlineBadgeText.text = drill.Badge;

            if (offlineBreakdownText != null)
                offlineBreakdownText.text = BuildBreakdown(score, totalQuestions, drill);

            if (offlineTimeText != null)
            {
                offlineTimeText.text = timeDisplay;
            }

            if (offlineDifficultyText != null)
            {
                offlineDifficultyText.text = displayDifficulty;
            }
        }
        {
            if (sessionCheckCoroutine != null)
                StopCoroutine(sessionCheckCoroutine);
            
            sessionCheckCoroutine = StartCoroutine(MonitorSessionStatus());
        }
    }

    /// <summary>
    /// Continuously monitor if the teacher session is still active via Firestore.
    /// If teacher stops the session (status = 'ended'), redirect student to MainScene.
    /// </summary>
    private IEnumerator MonitorSessionStatus()
    {
        if (db == null)
            db = FirebaseFirestore.DefaultInstance;

        string sessionCode = PlayerPrefs.GetString("SessionCode", "");
        
        if (string.IsNullOrEmpty(sessionCode))
        {
            yield break;
        }

        try
        {
            // Subscribe to Firestore listener for the session
            sessionListener = db.Collection("sessions")
                .WhereEqualTo("sessionCode", sessionCode)
                .Limit(1)
                .Listen(snapshot =>
                {
                    try
                    {
                        if (snapshot.Count > 0)
                        {
                            DocumentSnapshot doc = snapshot.Documents.First();
                            string status = doc.GetValue<string>("status");
                            
                            if (status == "ended")
                            {
                                // Stop the listener
                                if (sessionListener != null)
                                {
                                    sessionListener.Stop();
                                    sessionListener = null;
                                }
                                
                                // Redirect to MainScene
                                SceneNavigator.Instance.GoToMainScene();
                            }
                        }
                        else
                        {
                        }
                    }
                    catch (System.Exception e)
                    {
                    }
                });
        }
        catch (System.Exception e)
        {
        }

        // Wait indefinitely (the listener will handle the detection)
        while (true)
        {
            yield return new WaitForSeconds(5f);
        }
    }

    private void OnTryAgainClicked()
    {
        // Reload the current scene to restart the game
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void OnMenuClicked()
    {
        // Clear SessionCode when returning to menu (so offline mode doesn't think we're in a teacher session)
        PlayerPrefs.DeleteKey("SessionCode");
        PlayerPrefs.Save();
        
        // Load the main menu scene
        SceneNavigator.Instance.GoToMainScene();
    }
}

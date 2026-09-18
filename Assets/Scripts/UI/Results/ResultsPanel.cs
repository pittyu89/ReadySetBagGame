using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Firebase.Firestore;

/// <summary>
/// Results panel manager for both offline/guest and teacher session modes.
/// References both panel GameObjects and displays the appropriate one. The teacher session
/// panel also shows the session's live leaderboard: a podium for the top three and a
/// scrolling list for everyone else.
/// </summary>
public class ResultsPanel : MonoBehaviour
{
    /// <summary>The widgets both panels share: who played, the run's numbers, and the buttons.</summary>
    [Serializable]
    public class ResultView
    {
        public TextMeshProUGUI playerNameText;
        [Tooltip("The player's place on the leaderboard, next to their name. Optional.")]
        public TextMeshProUGUI playerRankText;
        public TextMeshProUGUI scoreText;
        public TextMeshProUGUI timeText;
        public TextMeshProUGUI difficultyText;
        public Button tryAgainButton;
        public Button nextDifficultyButton;
        public Button menuButton;
    }

    [Serializable]
    public class PodiumSlot
    {
        public GameObject root;
        public TextMeshProUGUI nameText;
        public TextMeshProUGUI scoreText;
        public TextMeshProUGUI timeText;
    }

    [Header("Panel References")]
    [SerializeField] private GameObject offlinePanel;
    [SerializeField] private GameObject teacherPanel;

    [Header("Offline Mode")]
    [SerializeField] private ResultView offlineView = new ResultView();

    [Header("Teacher Session Mode")]
    [SerializeField] private ResultView teacherView = new ResultView();

    [Header("Leaderboard")]
    [Tooltip("1st, 2nd and 3rd place, in that order.")]
    [SerializeField] private PodiumSlot[] podium = new PodiumSlot[3];
    [SerializeField] private RectTransform leaderboardRows;
    [Tooltip("Row copied for 4th place and below. Children named Rank, Name, Time and Points.")]
    [SerializeField] private GameObject leaderboardRowTemplate;

    private static readonly string[] DIFFICULTY_ORDER = { "beginner", "intermediate", "advanced" };
    private const string DIFFICULTY_PREF = "SessionDifficulty";

    private Coroutine sessionCheckCoroutine;
    private FirebaseFirestore db;
    private ListenerRegistration sessionListener;
    private ListenerRegistration leaderboardListener;

    private string difficultyKey;
    private float totalTime;
    private readonly List<GameObject> spawnedRows = new List<GameObject>();

    private struct Entry
    {
        public string StudentId;
        public string Name;
        public int Score;
        public float TimeLeft;
    }

    private void Start()
    {
        foreach (ResultView view in new[] { offlineView, teacherView })
        {
            if (view.tryAgainButton != null)
                view.tryAgainButton.onClick.AddListener(OnTryAgainClicked);

            if (view.nextDifficultyButton != null)
                view.nextDifficultyButton.onClick.AddListener(OnNextDifficultyClicked);

            if (view.menuButton != null)
                view.menuButton.onClick.AddListener(OnMenuClicked);
        }

        if (leaderboardRowTemplate != null)
            leaderboardRowTemplate.SetActive(false);
    }

    private void OnDestroy()
    {
        // Stop monitoring session status if it's running
        if (sessionCheckCoroutine != null)
        {
            StopCoroutine(sessionCheckCoroutine);
        }

        // Unsubscribe from Firestore listeners
        if (sessionListener != null)
        {
            sessionListener.Stop();
            sessionListener = null;
        }

        if (leaderboardListener != null)
        {
            leaderboardListener.Stop();
            leaderboardListener = null;
        }
    }

    /// <summary>
    /// Display the run's results on the panel for the current mode.
    /// </summary>
    public void DisplayResults(int score, int totalQuestions, float remainingTime, float totalTime,
                               string difficulty, DrillScore.Result drill)
    {
        bool isTeacherSession = !string.IsNullOrEmpty(PlayerPrefs.GetString("SessionCode", ""));

        difficultyKey = (difficulty ?? "").ToLowerInvariant();
        this.totalTime = totalTime;

        if (teacherPanel != null)
            teacherPanel.SetActive(isTeacherSession);
        if (offlinePanel != null)
            offlinePanel.SetActive(!isTeacherSession);

        ResultView view = isTeacherSession ? teacherView : offlineView;

        SetText(view.playerNameText, PlayerDisplayName().ToUpperInvariant());
        SetText(view.scoreText, $"{drill.FinalScore}/100");
        SetText(view.timeText, FormatTime(remainingTime, true));
        SetText(view.difficultyText, difficultyKey.ToUpperInvariant());

        // A teacher session is one run per student, so only MENU is offered there.
        // NEXT DIFFICULTY has nowhere to go after Advanced.
        SetButtonVisible(view.tryAgainButton, !isTeacherSession);
        SetButtonVisible(view.nextDifficultyButton, !isTeacherSession && NextDifficulty() != null);
        SetButtonVisible(view.menuButton, true);

        if (isTeacherSession)
        {
            Entry me = new Entry
            {
                StudentId = PlayerPrefs.GetString("StudentId", ""),
                Name = PlayerDisplayName(),
                Score = drill.FinalScore,
                TimeLeft = remainingTime
            };

            // Show at least this run straight away; the listener fills in the class
            ShowLeaderboard(new List<Entry> { me }, me);
            ListenToLeaderboard(me);

            if (sessionCheckCoroutine != null)
                StopCoroutine(sessionCheckCoroutine);

            sessionCheckCoroutine = StartCoroutine(MonitorSessionStatus());
        }
        else
        {
            SetText(view.playerRankText, "");
        }
    }

    private static string PlayerDisplayName()
    {
        bool isGuest = PlayerPrefs.GetString("IsGuest", "false") == "true";
        string name = PlayerPrefs.GetString("StudentName", "");
        return isGuest || string.IsNullOrEmpty(name) ? "Guest" : name;
    }

    // ---- Leaderboard ----------------------------------------------------------------------

    /// <summary>
    /// Follows every result sent for this session, so classmates who finish later still
    /// appear. The rules only allow this for students who joined the session; if the read is
    /// refused, the board simply keeps showing this player's own run.
    /// </summary>
    private void ListenToLeaderboard(Entry me)
    {
        string sessionId = PlayerPrefs.GetString(SessionResultUploader.SESSION_ID_KEY, "");
        if (string.IsNullOrEmpty(sessionId))
            return;

        if (db == null)
            db = FirebaseFirestore.DefaultInstance;

        if (leaderboardListener != null)
            leaderboardListener.Stop();

        try
        {
            leaderboardListener = db.Collection("sessionResults")
                .WhereEqualTo("sessionId", sessionId)
                .Listen(snapshot =>
                {
                    try
                    {
                        var entries = new List<Entry>();
                        foreach (DocumentSnapshot doc in snapshot.Documents)
                        {
                            string studentId = doc.TryGetValue("studentId", out string id) ? id : doc.Id;

                            // This run's own numbers are more precise than what was uploaded
                            if (!string.IsNullOrEmpty(me.StudentId) && studentId == me.StudentId)
                                continue;

                            doc.TryGetValue("studentName", out string name);
                            doc.TryGetValue("score", out long score);

                            // Exact time left when the result has it; results from older builds
                            // only have whole seconds taken
                            float timeLeft;
                            if (doc.TryGetValue("timeLeft", out double exactTimeLeft))
                                timeLeft = (float)exactTimeLeft;
                            else
                            {
                                doc.TryGetValue("completionTime", out long completionTime);
                                timeLeft = totalTime - completionTime;
                            }

                            entries.Add(new Entry
                            {
                                StudentId = studentId,
                                Name = string.IsNullOrEmpty(name) ? "Student" : name,
                                Score = (int)score,
                                TimeLeft = Mathf.Max(0f, timeLeft)
                            });
                        }

                        entries.Add(me);
                        ShowLeaderboard(entries, me);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Couldn't read the leaderboard: {e.Message}");
                    }
                });
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Couldn't load the leaderboard: {e.Message}");
        }
    }

    private void ShowLeaderboard(List<Entry> entries, Entry me)
    {
        // Best score first; a tie goes to whoever had more time left
        List<Entry> ranked = entries
            .OrderByDescending(e => e.Score)
            .ThenByDescending(e => e.TimeLeft)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int myIndex = ranked.FindIndex(e => e.StudentId == me.StudentId && e.Name == me.Name);
        SetText(teacherView.playerRankText, myIndex >= 0 ? (myIndex + 1).ToString() : "");

        for (int i = 0; i < podium.Length; i++)
        {
            PodiumSlot slot = podium[i];
            if (slot == null || slot.root == null)
                continue;

            bool filled = i < ranked.Count;
            slot.root.SetActive(filled);
            if (!filled)
                continue;

            SetText(slot.nameText, ranked[i].Name.ToUpperInvariant());
            SetText(slot.scoreText, $"{ranked[i].Score}/100");
            SetText(slot.timeText, FormatTime(ranked[i].TimeLeft, false));
        }

        foreach (GameObject row in spawnedRows)
            Destroy(row);
        spawnedRows.Clear();

        if (leaderboardRows == null || leaderboardRowTemplate == null)
            return;

        for (int i = podium.Length; i < ranked.Count; i++)
        {
            GameObject row = Instantiate(leaderboardRowTemplate, leaderboardRows);
            row.name = "Row " + (i + 1);
            row.SetActive(true);
            spawnedRows.Add(row);

            SetChildText(row, "Rank", (i + 1).ToString());
            SetChildText(row, "Name", ranked[i].Name.ToUpperInvariant());
            SetChildText(row, "Time", FormatTime(ranked[i].TimeLeft, true));
            SetChildText(row, "Points", $"{ranked[i].Score}/100");
        }
    }

    // ---- Formatting helpers ---------------------------------------------------------------

    /// <summary>Minutes, seconds and hundredths: "05:34.45", or "5:34.45" without the padding.</summary>
    private static string FormatTime(float seconds, bool padMinutes)
    {
        seconds = Mathf.Max(0f, seconds);
        int hundredths = Mathf.FloorToInt(seconds * 100f);
        int minutes = hundredths / 6000;
        int secs = hundredths / 100 % 60;
        int cents = hundredths % 100;
        return padMinutes
            ? $"{minutes:00}:{secs:00}.{cents:00}"
            : $"{minutes}:{secs:00}.{cents:00}";
    }

    private static void SetText(TextMeshProUGUI label, string text)
    {
        if (label != null)
            label.text = text;
    }

    private static void SetChildText(GameObject row, string child, string text)
    {
        Transform t = row.transform.Find(child);
        if (t != null)
            SetText(t.GetComponent<TextMeshProUGUI>(), text);
    }

    private static void SetButtonVisible(Button button, bool visible)
    {
        if (button != null)
            button.gameObject.SetActive(visible);
    }

    private string NextDifficulty()
    {
        int index = Array.IndexOf(DIFFICULTY_ORDER, difficultyKey);
        return index >= 0 && index < DIFFICULTY_ORDER.Length - 1 ? DIFFICULTY_ORDER[index + 1] : null;
    }

    // ---- Session status -------------------------------------------------------------------

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
                                SceneNavigationManager.Instance.GoToMainScene();
                            }
                        }
                    }
                    catch (Exception)
                    {
                    }
                });
        }
        catch (Exception)
        {
        }

        // Wait indefinitely (the listener will handle the detection)
        while (true)
        {
            yield return new WaitForSeconds(5f);
        }
    }

    // ---- Buttons ------------------------------------------------------------------------

    private void OnTryAgainClicked()
    {
        // Reload the current scene to restart the game
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void OnNextDifficultyClicked()
    {
        string next = NextDifficulty();
        if (next == null)
            return;

        // GameDifficultyApplier reads this when the scene loads; the go-bag choice carries over
        PlayerPrefs.SetString(DIFFICULTY_PREF, next);
        PlayerPrefs.Save();

        LoadingScreen.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void OnMenuClicked()
    {
        // Clear SessionCode when returning to menu (so offline mode doesn't think we're in a teacher session)
        PlayerPrefs.DeleteKey("SessionCode");
        PlayerPrefs.Save();

        // Load the main menu scene
        SceneNavigationManager.Instance.GoToMainScene();
    }
}

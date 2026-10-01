using UnityEngine;
using Firebase.Firestore;
using Firebase.Extensions;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;

public class JoinSessionPanel : MonoBehaviour
{
    [SerializeField] private TMP_InputField sessionCodeInput;
    [SerializeField] private UnityEngine.UI.Button joinButton;
    [Tooltip("Error / info messages (session not found, guests, ...).")]
    [SerializeField] private TextMeshProUGUI waitingForTeacherText;
    [SerializeField] private AudioClip joinSessionAudio;

    [Header("Join States")]
    [Tooltip("Shown before joining: the Enter Code title and the code field.")]
    [SerializeField] private GameObject enterCodeGroup;
    [Tooltip("Shown after joining: the Waiting For Teacher title.")]
    [SerializeField] private GameObject waitingGroup;
    [SerializeField] private TextMeshProUGUI joinButtonText;
    [SerializeField] private string joinLabel = "JOIN";
    [SerializeField] private string joinedLabel = "JOINED";

    [Header("Waiting Character")]
    [Tooltip("Idle chibis above the Waiting For Teacher title; only the selected character is shown.")]
    [SerializeField] private GameObject femaleWaitingCharacter;
    [SerializeField] private GameObject maleWaitingCharacter;

    private const string SELECTED_CHARACTER_SUFFIX = "_SelectedCharacter";

    private FirebaseFirestore db;
    private string studentId;
    private string studentUsername;
    private string currentSessionCode;
    private string currentSessionId;
    private ListenerRegistration sessionListener;
    private bool isJoined = false;

    private void Start()
    {
        db = FirebaseFirestore.DefaultInstance;
        SetJoinedState(false);

        // Check if user is a guest
        bool isGuest = PlayerPrefs.GetString("IsGuest", "false") == "true";

        // Get student info from login
        studentId = PlayerPrefs.GetString("StudentId", "");
        studentUsername = PlayerPrefs.GetString("StudentUsername", "");

        if (isGuest)
        {
            SetStatusText("Guests cannot join teacher sessions.");
            joinButton.interactable = false;
            sessionCodeInput.interactable = false;
            return;
        }

        if (string.IsNullOrEmpty(studentId))
        {
            joinButton.interactable = false;
            return;
        }

        if (joinButton != null)
            joinButton.onClick.AddListener(OnJoinButtonClicked);
    }

    private void OnJoinButtonClicked()
    {
        if (isJoined)
        {
            return;
        }

        string code = sessionCodeInput.text.Trim().ToUpper();

        if (string.IsNullOrEmpty(code) || code.Length != 5)
        {
            SetStatusText("Invalid session code.");
            return;
        }

        JoinSession(code);
    }

    private async void JoinSession(string sessionCode)
    {
        try
        {
            joinButton.interactable = false;

            // Clear any previous status message
            if (waitingForTeacherText != null)
            {
                waitingForTeacherText.gameObject.SetActive(false);
            }

            // Find session by code
            var query = db.Collection("sessions")
                .WhereEqualTo("sessionCode", sessionCode);

            var snapshot = await query.GetSnapshotAsync();

            var documents = snapshot.Documents.ToList();

            if (documents.Count == 0)
            {
                joinButton.interactable = true;
                SetStatusText("Session not found.");
                return;
            }

            var sessionDoc = documents[0];
            currentSessionCode = sessionCode;
            currentSessionId = sessionDoc.Id;

            var sessionData = sessionDoc.ToDictionary();

            // Check if session is waiting (not yet started)
            string status = sessionData.ContainsKey("status") ? sessionData["status"].ToString() : "waiting";

            if (status == "ended")
            {
                joinButton.interactable = true;
                SetStatusText("Session already ended.");
                return;
            }

            // One drill per student: rejoining carries on a drill left unfinished - found on the
            // server, so from any device - but not one that has reached its results
            if (await SessionDrillStore.PrepareAsync(currentSessionId) == SessionDrillStore.JoinState.Over)
            {
                joinButton.interactable = true;
                SetStatusText("You've already played this session's drill.");
                return;
            }

            // Add student to session, unless a rejoin finds them already on the list: every
            // join used to add another entry, so the teacher saw the same student twice
            if (!IsAlreadyListed(sessionData))
                await AddPlayerToSession(currentSessionId);

            // Play join session audio
            SoundManager.Sfx(joinSessionAudio);

            // Listen for session changes (difficulty, status)
            ListenToSession(currentSessionId);

            isJoined = true;
            joinButton.interactable = false;
            sessionCodeInput.interactable = false;
            SetJoinedState(true);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Joining session {sessionCode} failed: {ex.Message}");
            joinButton.interactable = true;
            SetStatusText("Something went wrong.");
        }
    }

    /// <summary>
    /// Swaps the panel between "Enter Code" (field + JOIN) and "Waiting For Teacher..." (JOINED).
    /// </summary>
    private void SetJoinedState(bool joined)
    {
        if (enterCodeGroup != null)
            enterCodeGroup.SetActive(!joined);

        if (waitingGroup != null)
            waitingGroup.SetActive(joined);

        if (joinButtonText != null)
            joinButtonText.text = joined ? joinedLabel : joinLabel;

        if (joined && waitingForTeacherText != null)
            waitingForTeacherText.gameObject.SetActive(false);

        if (joined)
            ShowSelectedWaitingCharacter();
    }

    private void ShowSelectedWaitingCharacter()
    {
        bool isGuest = PlayerPrefs.GetString("IsGuest", "false") == "true";
        string userName = isGuest ? "Guest" : PlayerPrefs.GetString("StudentName", "User");
        bool isMale = PlayerPrefs.GetString(userName + SELECTED_CHARACTER_SUFFIX, "Female") == "Male";

        if (femaleWaitingCharacter != null)
            femaleWaitingCharacter.SetActive(!isMale);

        if (maleWaitingCharacter != null)
            maleWaitingCharacter.SetActive(isMale);

        StartWalking(isMale ? maleWaitingCharacter : femaleWaitingCharacter);
    }

    // The chibi walks towards the camera on the spot while it waits. Its controller is shared
    // with the other menu chibis, which stay idle, so the walk is switched on here rather than
    // made the default. Kept through the panel closing and reopening, since an animator
    // otherwise resets its parameters - and drops back to idle - each time it is re-enabled.
    private static void StartWalking(GameObject character)
    {
        Animator animator = character != null ? character.GetComponent<Animator>() : null;
        if (animator == null)
            return;

        animator.keepAnimatorStateOnDisable = true;
        animator.SetBool("isWalking", true);
    }

    private void SetStatusText(string message)
    {
        if (waitingForTeacherText != null)
        {
            waitingForTeacherText.text = message;
            waitingForTeacherText.gameObject.SetActive(true);
        }
    }

    /// <summary>Whether this student is already in the session's player list.</summary>
    private bool IsAlreadyListed(Dictionary<string, object> sessionData)
    {
        object players;
        if (!sessionData.TryGetValue("playersList", out players) || !(players is List<object> list))
            return false;

        foreach (object entry in list)
        {
            if (entry is Dictionary<string, object> player
                && player.TryGetValue("studentId", out object id)
                && id != null && id.ToString() == studentId)
                return true;
        }
        return false;
    }

    private async System.Threading.Tasks.Task AddPlayerToSession(string sessionId)
    {
        try
        {
            var sessionRef = db.Collection("sessions").Document(sessionId);
            string uid = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser?.UserId ?? "";

            // Create player data. "uid" is what the Firestore rules check: a student may only
            // add an entry for themselves.
            var playerData = new Dictionary<string, object>
            {
                { "studentId", studentId },
                { "username", studentUsername },
                { "uid", uid },
                { "joinedAt", System.DateTime.UtcNow }
            };

            // Add to playersList array using ArrayUnion - pass as array element.
            // playerUids mirrors the uids as a plain list: the rules use it to let everyone who
            // joined read the session's results for the leaderboard.
            var updateData = new Dictionary<string, object>
            {
                { "playersList", FieldValue.ArrayUnion(new object[] { playerData }) },
                { "playerUids", FieldValue.ArrayUnion(new object[] { uid }) }
            };

            await sessionRef.UpdateAsync(updateData);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Joining session {sessionId} failed: {ex.Message}");
            throw;
        }
    }

    private void ListenToSession(string sessionId)
    {
        // Remove old listener
        if (sessionListener != null)
        {
            sessionListener.Stop();
        }

        // Listen for real-time updates
        sessionListener = db.Collection("sessions").Document(sessionId)
            .Listen(snapshot =>
            {
                if (snapshot.Exists)
                {
                    var data = snapshot.ToDictionary();
                    string status = data.ContainsKey("status") ? data["status"].ToString() : "?";

                    // Update joined count
                    if (data.ContainsKey("playersList"))
                    {
                        var playersList = data["playersList"] as List<object>;
                        int joinedCount = playersList != null ? playersList.Count : 0;
                    }

                    // Check if session started
                    if (data.ContainsKey("status"))
                    {
                        if (status == "active")
                        {
                            // Get difficulty
                            string difficulty = data.ContainsKey("difficulty") ? 
                                data["difficulty"].ToString() : "beginner";

                            // Save to PlayerPrefs for GameScene. The session id and its teacher
                            // go into the result the game sends when the drill ends.
                            PlayerPrefs.SetString("SessionDifficulty", difficulty);
                            PlayerPrefs.SetString("SessionCode", currentSessionCode);
                            PlayerPrefs.SetString(SessionResultUploader.SESSION_ID_KEY, sessionId);
                            PlayerPrefs.SetString(SessionResultUploader.SESSION_TEACHER_KEY,
                                data.ContainsKey("teacherId") ? data["teacherId"].ToString() : "");

                            // The go-bag the teacher picked for the class (sessions made
                            // before the picker existed use the standard bag)
                            string bagType = data.ContainsKey("bagType") ? data["bagType"].ToString() : "standard";
                            PlayerPrefs.SetInt(DifficultyPanel.SESSION_GO_BAG_KEY, GoBagIndexFor(bagType));
                            PlayerPrefs.Save();

                            // Stop listening
                            if (sessionListener != null)
                            {
                                sessionListener.Stop();
                            }

                            // Load GameScene
                            Invoke("LoadGameScene", 1f);
                        }
                        else if (status == "ended")
                        {
                            StopListening();
                        }
                    }
                }
            });
    }

    /// <summary>The dashboard's bagType names, in the difficulty panel's bag order.</summary>
    private static int GoBagIndexFor(string bagType)
    {
        switch ((bagType ?? "").ToLowerInvariant())
        {
            case "small": return 1;
            case "medium": return 2;
            default: return 0;
        }
    }

    private void LoadGameScene()
    {
        LoadingScreen.LoadScene("GameScene");
    }

    private void StopListening()
    {
        if (sessionListener != null)
        {
            sessionListener.Stop();
            sessionListener = null;
        }

        isJoined = false;
        if (joinButton != null)
            joinButton.interactable = true;
        if (sessionCodeInput != null)
            sessionCodeInput.interactable = true;
        SetJoinedState(false);
    }

    private void OnDestroy()
    {
        StopListening();
    }
}

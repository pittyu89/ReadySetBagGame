using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Firestore;
using UnityEngine;

/// <summary>
/// Sends a finished teacher-session drill to Firestore as a "sessionResults" document, which is
/// what the teacher and admin dashboards report on. Offline practice is never sent.
///
/// One result per student per session: the document id is "{sessionId}_{studentId}" and the
/// Firestore rules refuse a second write to it, so the first finished run is the one that
/// counts. The fields and their types have to match the rules' isValidResult check exactly -
/// change one side and the other rejects every result.
///
/// A result is kept on the device until Firestore has confirmed it. Firestore only retries a
/// write while the game is running, so a crash, or the app being closed while offline, used
/// to lose the result for good; now the main menu sends whatever is still waiting
/// (<see cref="ResendPending"/>).
/// </summary>
public static class SessionResultUploader
{
    public const string SESSION_ID_KEY = "SessionId";
    public const string SESSION_TEACHER_KEY = "SessionTeacherId";

    private const string COLLECTION = "sessionResults";
    private const string SUBMITTED_PREFIX = "SubmittedResult_";
    private const string PENDING_KEY = "PendingSessionResults";

    // A result the rules keep refusing (say the fields changed under it) is given up after
    // this many launches, rather than tried forever
    private const int MAX_ATTEMPTS = 10;

    /// <summary>Everything a result document holds, saved until Firestore has it.</summary>
    [Serializable]
    private class PendingResult
    {
        public string docId;
        public string sessionId;
        public string sessionCode;
        public string teacherId;
        public string studentId;
        public string studentUid;
        public string studentName;
        public string section;
        public int score;
        public int completionTime;
        public double timeLeft;
        public string stage;
        public int essentials;
        public int essentialsMax;
        public int errors;
        public string difficulty;
        public int attempts;

        // When the drill finished, by the server's clock (the attempt's finishedAt), as UTC
        // ticks. Sent with the result so a late upload is dated by when it was played.
        public bool hasFinishedAt;
        public long finishedAtTicks;
    }

    [Serializable]
    private class PendingList
    {
        public List<PendingResult> items = new List<PendingResult>();
    }

    // Results being sent right now, so a resend never races the first attempt
    private static readonly HashSet<string> inFlight = new HashSet<string>();

    public static bool IsTeacherSession =>
        !string.IsNullOrEmpty(PlayerPrefs.GetString("SessionCode", "")) &&
        !string.IsNullOrEmpty(PlayerPrefs.GetString(SESSION_ID_KEY, ""));

    /// <summary>
    /// Fire-and-forget: the drill is already over and the results panel doesn't wait on this.
    /// The student's attempt is marked finished (<see cref="SessionDrillStore.FinishAsync"/>),
    /// so the session is over for them. The result is saved on the device before anything is
    /// sent, so it survives the game closing mid-upload and goes out from the main menu next
    /// time.
    /// </summary>
    public static async void Submit(DrillScore.Result drill, int correctAnswers, int totalQuestions,
                                    float remainingTime, float totalTime, string difficulty)
    {
        if (!IsTeacherSession)
            return;

        FirebaseUser user = FirebaseAuth.DefaultInstance.CurrentUser;
        string studentId = PlayerPrefs.GetString("StudentId", "");
        if (user == null || string.IsNullOrEmpty(studentId))
        {
            Debug.LogWarning("Not sending the drill result: no signed-in student.");
            await SessionDrillStore.FinishAsync();
            return;
        }

        string sessionId = PlayerPrefs.GetString(SESSION_ID_KEY, "");
        string docId = sessionId + "_" + studentId;

        // The rules would refuse a repeat anyway; this just avoids the pointless round trip
        if (PlayerPrefs.GetInt(SUBMITTED_PREFIX + docId, 0) == 1)
        {
            await SessionDrillStore.FinishAsync();
            return;
        }

        int wrongAnswers = Mathf.Max(0, totalQuestions - correctAnswers);
        PendingResult result = new PendingResult
        {
            docId = docId,
            sessionId = sessionId,
            sessionCode = PlayerPrefs.GetString("SessionCode", ""),
            teacherId = PlayerPrefs.GetString(SESSION_TEACHER_KEY, ""),
            studentId = studentId,
            studentUid = user.UserId,
            studentName = PlayerPrefs.GetString("StudentName", ""),
            section = PlayerPrefs.GetString("StudentSection", ""),
            score = drill.FinalScore,
            completionTime = Mathf.Clamp(Mathf.RoundToInt(totalTime - remainingTime), 0, 7200),
            // Exact time left (to the hundredth) for the in-game leaderboard; completionTime
            // stays whole seconds for the dashboards
            timeLeft = Math.Round(Mathf.Clamp(remainingTime, 0f, 7200f), 2),
            stage = StageFor(drill.FinalScore),
            essentials = drill.EssentialsPacked,
            essentialsMax = Mathf.Max(1, drill.EssentialsTarget),
            // Everything that cost points: unnecessary items and wrong scenario answers
            errors = drill.JunkCount + wrongAnswers,
            difficulty = (difficulty ?? "").ToLowerInvariant()
        };

        // Saved before the first await, so nothing after this point can lose it
        SavePending(result);

        // Finished before the result goes, so the result can carry the server's finish time,
        // and so the rules still take it if the upload itself ends up after the session
        DateTime? finishedAt = await SessionDrillStore.FinishAsync();
        if (finishedAt.HasValue)
        {
            result.hasFinishedAt = true;
            result.finishedAtTicks = finishedAt.Value.Ticks;
            SavePending(result);
        }

        await Send(result, checkExisting: false);
    }

    /// <summary>
    /// Sends any result a previous game didn't get through, for the student signed in now.
    /// Called from the main menu, which every launch passes through once signed in.
    /// </summary>
    public static async void ResendPending()
    {
        FirebaseUser user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null)
            return;

        PendingList pending = LoadPending();
        foreach (PendingResult result in pending.items.ToArray())
        {
            // Another student's, left on a shared device: theirs to send when they sign in,
            // since the rules only take a result from the student it belongs to
            if (result.studentUid != user.UserId)
                continue;

            if (PlayerPrefs.GetInt(SUBMITTED_PREFIX + result.docId, 0) == 1)
            {
                RemovePending(result.docId);
                continue;
            }

            await Send(result, checkExisting: true);
        }
    }

    /// <summary>
    /// Writes one result. <paramref name="checkExisting"/> is for resends: the first attempt may
    /// have reached Firestore before the game closed, and the rules refuse a second write, so
    /// a result that is already there only needs forgetting.
    /// </summary>
    private static async Task Send(PendingResult result, bool checkExisting)
    {
        if (!inFlight.Add(result.docId))
            return;

        try
        {
            FirebaseFirestore db = FirebaseFirestore.DefaultInstance;
            DocumentReference doc = db.Collection(COLLECTION).Document(result.docId);

            if (checkExisting)
            {
                result.attempts++;
                if (result.attempts > MAX_ATTEMPTS)
                {
                    Debug.LogWarning($"Giving up on the drill result {result.docId} after {MAX_ATTEMPTS} tries.");
                    RemovePending(result.docId);
                    return;
                }
                SavePending(result);

                if (await AlreadySent(doc))
                {
                    MarkSent(result.docId);
                    return;
                }
            }

            // Saved at login; students still signed in from an older build won't have it yet
            if (string.IsNullOrEmpty(result.section))
                await FillInStudent(db, result);

            // A result whose finish couldn't be recorded when it was played may find it on
            // its attempt now
            if (!result.hasFinishedAt)
            {
                DateTime? finishedAt = await SessionDrillStore.ReadFinishedAtAsync(result.docId);
                if (finishedAt.HasValue)
                {
                    result.hasFinishedAt = true;
                    result.finishedAtTicks = finishedAt.Value.Ticks;
                }
            }

            var data = new Dictionary<string, object>
            {
                { "sessionId", result.sessionId },
                { "sessionCode", result.sessionCode },
                { "teacherId", result.teacherId },
                { "studentId", result.studentId },
                { "studentUid", result.studentUid },
                { "studentName", result.studentName ?? "" },
                { "section", result.section ?? "" },
                { "score", result.score },
                { "completionTime", result.completionTime },
                { "timeLeft", result.timeLeft },
                // Teacher sessions allow a single run, so this is always the first attempt
                { "attempts", 1 },
                { "stage", result.stage },
                { "essentials", result.essentials },
                { "essentialsMax", result.essentialsMax },
                { "errors", result.errors },
                { "difficulty", result.difficulty },
                { "createdAt", FieldValue.ServerTimestamp },
                { "updatedAt", FieldValue.ServerTimestamp }
            };

            // Must match the attempt's own finishedAt exactly, or the rules refuse the result
            if (result.hasFinishedAt)
                data["finishedAt"] = Timestamp.FromDateTime(new DateTime(result.finishedAtTicks, DateTimeKind.Utc));

            await doc.SetAsync(data);

            MarkSent(result.docId);
            Debug.Log($"Drill result sent to the dashboard ({result.score}/100).");
        }
        catch (Exception ex)
        {
            // Stays saved on the device; the main menu tries again next launch
            Debug.LogWarning($"Couldn't send the drill result to the dashboard: {ex.Message}");
        }
        finally
        {
            inFlight.Remove(result.docId);
        }
    }

    private static async Task<bool> AlreadySent(DocumentReference doc)
    {
        try
        {
            DocumentSnapshot existing = await doc.GetSnapshotAsync(Source.Server);
            return existing.Exists;
        }
        catch (Exception)
        {
            // Can't tell (offline, or not allowed to read it): try the write, which the rules
            // refuse if it is already there
            return false;
        }
    }

    private static async Task FillInStudent(FirebaseFirestore db, PendingResult result)
    {
        DocumentSnapshot student = await db.Collection("students").Document(result.studentId).GetSnapshotAsync();
        if (!student.Exists)
            return;

        string section;
        student.TryGetValue("section", out section);
        result.section = section ?? "";
        PlayerPrefs.SetString("StudentSection", result.section);

        if (string.IsNullOrEmpty(result.studentName))
        {
            string studentName;
            student.TryGetValue("displayName", out studentName);
            result.studentName = studentName ?? "";
        }
    }

    private static void MarkSent(string docId)
    {
        PlayerPrefs.SetInt(SUBMITTED_PREFIX + docId, 1);
        RemovePending(docId);
    }

    // ----------------------------------------------------------------- saved on the device

    private static PendingList LoadPending()
    {
        string json = PlayerPrefs.GetString(PENDING_KEY, "");
        if (string.IsNullOrEmpty(json))
            return new PendingList();

        try
        {
            return JsonUtility.FromJson<PendingList>(json) ?? new PendingList();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Couldn't read the saved drill results: {ex.Message}");
            return new PendingList();
        }
    }

    private static void SavePending(PendingResult result)
    {
        PendingList pending = LoadPending();
        pending.items.RemoveAll(r => r.docId == result.docId);
        pending.items.Add(result);
        PlayerPrefs.SetString(PENDING_KEY, JsonUtility.ToJson(pending));
        PlayerPrefs.Save();
    }

    private static void RemovePending(string docId)
    {
        PendingList pending = LoadPending();
        pending.items.RemoveAll(r => r.docId == docId);
        if (pending.items.Count == 0)
            PlayerPrefs.DeleteKey(PENDING_KEY);
        else
            PlayerPrefs.SetString(PENDING_KEY, JsonUtility.ToJson(pending));
        PlayerPrefs.Save();
    }

    /// <summary>
    /// The dashboards' learning-stage names, using the same cut-offs as the badge the student
    /// sees on the results panel.
    /// </summary>
    private static string StageFor(int finalScore)
    {
        if (finalScore >= DrillScore.BADGE_MASTER_MIN) return "Autonomous";
        if (finalScore >= DrillScore.BADGE_PROFICIENT_MIN) return "Associative";
        return "Cognitive";
    }
}

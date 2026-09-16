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
/// </summary>
public static class SessionResultUploader
{
    public const string SESSION_ID_KEY = "SessionId";
    public const string SESSION_TEACHER_KEY = "SessionTeacherId";

    private const string COLLECTION = "sessionResults";
    private const string SUBMITTED_PREFIX = "SubmittedResult_";

    public static bool IsTeacherSession =>
        !string.IsNullOrEmpty(PlayerPrefs.GetString("SessionCode", "")) &&
        !string.IsNullOrEmpty(PlayerPrefs.GetString(SESSION_ID_KEY, ""));

    /// <summary>
    /// Fire-and-forget: the drill is already over and the results panel doesn't wait on this.
    /// If the device is offline, Firestore holds the write and sends it once it reconnects
    /// (while the game is still running).
    /// </summary>
    public static async void Submit(DrillScore.Result drill, int correctAnswers, int totalQuestions,
                                    float remainingTime, float totalTime, string difficulty)
    {
        if (!IsTeacherSession)
            return;

        try
        {
            await SubmitAsync(drill, correctAnswers, totalQuestions, remainingTime, totalTime, difficulty);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Couldn't send the drill result to the dashboard: {ex.Message}");
        }
    }

    private static async Task SubmitAsync(DrillScore.Result drill, int correctAnswers, int totalQuestions,
                                          float remainingTime, float totalTime, string difficulty)
    {
        FirebaseUser user = FirebaseAuth.DefaultInstance.CurrentUser;
        string studentId = PlayerPrefs.GetString("StudentId", "");
        if (user == null || string.IsNullOrEmpty(studentId))
        {
            Debug.LogWarning("Not sending the drill result: no signed-in student.");
            return;
        }

        string sessionId = PlayerPrefs.GetString(SESSION_ID_KEY, "");
        string docId = sessionId + "_" + studentId;

        // The rules would refuse a repeat anyway; this just avoids the pointless round trip
        string submittedKey = SUBMITTED_PREFIX + docId;
        if (PlayerPrefs.GetInt(submittedKey, 0) == 1)
            return;

        FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

        // Saved at login; students still signed in from an older build won't have it yet
        string section = PlayerPrefs.GetString("StudentSection", "");
        string studentName = PlayerPrefs.GetString("StudentName", "");
        if (string.IsNullOrEmpty(section))
        {
            DocumentSnapshot student = await db.Collection("students").Document(studentId).GetSnapshotAsync();
            if (student.Exists)
            {
                student.TryGetValue("section", out section);
                if (string.IsNullOrEmpty(studentName))
                    student.TryGetValue("displayName", out studentName);

                PlayerPrefs.SetString("StudentSection", section ?? "");
            }
        }

        int completionTime = Mathf.Clamp(Mathf.RoundToInt(totalTime - remainingTime), 0, 7200);
        int wrongAnswers = Mathf.Max(0, totalQuestions - correctAnswers);

        var data = new Dictionary<string, object>
        {
            { "sessionId", sessionId },
            { "sessionCode", PlayerPrefs.GetString("SessionCode", "") },
            { "teacherId", PlayerPrefs.GetString(SESSION_TEACHER_KEY, "") },
            { "studentId", studentId },
            { "studentUid", user.UserId },
            { "studentName", studentName ?? "" },
            { "section", section ?? "" },
            { "score", drill.FinalScore },
            { "completionTime", completionTime },
            // Teacher sessions allow a single run, so this is always the first attempt
            { "attempts", 1 },
            { "stage", StageFor(drill.FinalScore) },
            { "essentials", drill.EssentialsPacked },
            { "essentialsMax", Mathf.Max(1, drill.EssentialsTarget) },
            // Everything that cost points: unnecessary items, wrong scenario answers, going over weight
            { "errors", drill.JunkCount + wrongAnswers + (drill.OverWeight ? 1 : 0) },
            { "difficulty", (difficulty ?? "").ToLowerInvariant() },
            { "createdAt", FieldValue.ServerTimestamp },
            { "updatedAt", FieldValue.ServerTimestamp }
        };

        await db.Collection(COLLECTION).Document(docId).SetAsync(data);

        PlayerPrefs.SetInt(submittedKey, 1);
        PlayerPrefs.Save();
        Debug.Log($"Drill result sent to the dashboard ({drill.FinalScore}/100).");
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

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Firestore;
using UnityEngine;

/// <summary>
/// A teacher session's one drill per student, saved so leaving is not a free restart.
///
/// While the student plays, their drill (<see cref="DrillSnapshot"/>), packing or in the quiz,
/// is saved to Firestore as their attempt, "sessionAttempts/{sessionId}_{studentId}", and to
/// the device as well. Rejoining the session carries on from it - on any device, since the
/// attempt is on the server - with the time they were away taken off whichever clock was
/// running. That time is measured by the server's clock (the gap between two server-stamped
/// saves), so changing the device's clock gains nothing. Once the results are up the attempt
/// is marked finished and the session refuses them from then on.
///
/// The device's copy is the fallback for when the server can't be reached: it is saved more
/// often, and read with the device's clock. See UNITY_FIRESTORE_READY.md in the website repo
/// for what the security rules accept.
/// </summary>
public static class SessionDrillStore
{
    private const string DRILL_PREFIX = "SessionDrill_";
    private const string OVER_PREFIX = "SessionDrillOver_";
    private const string COLLECTION = "sessionAttempts";

    // The device copy is saved every few seconds; the server's less often, to keep a class's
    // writes down (about four a minute per student). Leaving, the game going to the background
    // and an answer being scored save to the server straight away.
    private const float SERVER_SAVE_INTERVAL = 15f;

    public enum JoinState
    {
        /// <summary>No drill of theirs yet: the session starts them fresh.</summary>
        Fresh,
        /// <summary>A drill to carry on; <see cref="TakePrepared"/> hands it to the game.</summary>
        Resume,
        /// <summary>Their drill has reached its results: the session is over for them.</summary>
        Over
    }

    // What the join found for the session it was prepared for
    private static string preparedKey;
    private static DrillSnapshot prepared;

    // Whether the attempt exists on the server yet, so a save knows to create or update it.
    // Null until known.
    private static bool? attemptOnServer;
    private static string attemptKey;
    private static float lastServerSave = float.NegativeInfinity;
    private static bool serverSaveInFlight;

    // ----------------------------------------------------------------- joining

    /// <summary>
    /// Looks for this student's drill in a session they are joining. Asks the server first:
    /// a drill left on another device is found there, and the time away is read off the
    /// server's clock. Falls back to the device's own copy when the server can't be reached.
    /// </summary>
    public static async Task<JoinState> PrepareAsync(string sessionId)
    {
        string key = KeyFor(sessionId);
        preparedKey = key;
        prepared = null;
        attemptKey = key;
        attemptOnServer = null;

        if (key == null)
            return JoinState.Fresh;

        try
        {
            DocumentReference doc = FirebaseFirestore.DefaultInstance.Collection(COLLECTION).Document(key);
            DocumentSnapshot before = await doc.GetSnapshotAsync(Source.Server);

            if (!before.Exists)
            {
                attemptOnServer = false;
                // The first save may never have reached the server: use the device's copy if any
                return PrepareFromDevice(key);
            }

            attemptOnServer = true;

            if (before.GetValue<string>("status") == "finished")
            {
                MarkOverOnDevice(key);
                return JoinState.Over;
            }

            // Stamp the attempt with the server's time now, and read it back: the gap since
            // its last save is how long the student was away, by the server's clock alone
            Timestamp lastSaved = before.GetValue<Timestamp>("savedAt");
            await doc.UpdateAsync(new Dictionary<string, object> { { "savedAt", FieldValue.ServerTimestamp } });
            DocumentSnapshot after = await doc.GetSnapshotAsync(Source.Server);
            Timestamp now = after.GetValue<Timestamp>("savedAt");

            DrillSnapshot saved = DrillSnapshot.FromJson(before.GetValue<string>("snapshot"));
            if (saved == null)
                return PrepareFromDevice(key);

            double away = (now.ToDateTime() - lastSaved.ToDateTime()).TotalSeconds;
            saved.TakeOffTimeAway((float)Math.Max(0.0, away));
            prepared = saved;
            return JoinState.Resume;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Couldn't check the saved drill on the server, using this device's: {ex.Message}");
            return PrepareFromDevice(key);
        }
    }

    private static JoinState PrepareFromDevice(string key)
    {
        if (PlayerPrefs.GetInt(OVER_PREFIX + key, 0) == 1)
            return JoinState.Over;

        DrillSnapshot saved = DrillSnapshot.FromJson(PlayerPrefs.GetString(DRILL_PREFIX + key, ""));
        if (saved == null)
            return JoinState.Fresh;

        // Only the device's clock to go by here
        saved.TakeOffTimeAway();
        prepared = saved;
        return JoinState.Resume;
    }

    /// <summary>
    /// The drill to carry on in the current session, once, or null to start fresh. Prepared by
    /// the join; a game scene loaded without one (from the editor) reads the device's copy.
    /// </summary>
    public static DrillSnapshot TakePrepared()
    {
        string key = CurrentKey();
        if (key == null)
            return null;

        if (preparedKey != key)
        {
            preparedKey = key;
            attemptKey = key;
            attemptOnServer = null;
            PrepareFromDevice(key);
        }

        DrillSnapshot taken = prepared;
        prepared = null;
        return taken;
    }

    // ----------------------------------------------------------------- saving

    /// <summary>
    /// Saves the drill of the current session: to the device every time, to the server every
    /// <see cref="SERVER_SAVE_INTERVAL"/> seconds, or at once with <paramref name="now"/>.
    /// Does nothing once the drill is over.
    /// </summary>
    public static void Save(DrillSnapshot snapshot, bool now = false)
    {
        string key = CurrentKey();
        if (key == null || snapshot == null || PlayerPrefs.GetInt(OVER_PREFIX + key, 0) == 1)
            return;

        string json = snapshot.ToJson();
        PlayerPrefs.SetString(DRILL_PREFIX + key, json);
        PlayerPrefs.Save();

        if (now || Time.realtimeSinceStartup - lastServerSave >= SERVER_SAVE_INTERVAL)
            SaveToServer(key, json);
    }

    private static async void SaveToServer(string key, string json)
    {
        if (serverSaveInFlight)
            return;

        serverSaveInFlight = true;
        lastServerSave = Time.realtimeSinceStartup;
        AttemptOwner owner = AttemptOwner.Current();

        try
        {
            if (attemptKey != key)
            {
                attemptKey = key;
                attemptOnServer = null;
            }

            DocumentReference doc = FirebaseFirestore.DefaultInstance.Collection(COLLECTION).Document(key);

            if (attemptOnServer == true)
            {
                await doc.UpdateAsync(new Dictionary<string, object>
                {
                    { "snapshot", json },
                    { "savedAt", FieldValue.ServerTimestamp }
                });
            }
            else
            {
                await CreateAttempt(doc, json, owner);
            }
        }
        catch (Exception ex)
        {
            // Unknown again: the next save checks which it should be. The device copy stands.
            attemptOnServer = null;
            Debug.LogWarning($"Couldn't save the drill to the server: {ex.Message}");
        }
        finally
        {
            serverSaveInFlight = false;
        }
    }

    /// <summary>
    /// Whose attempt it is, read while the session is still in PlayerPrefs: leaving the results
    /// screen clears it, and a save may still be on its way when that happens.
    /// </summary>
    private struct AttemptOwner
    {
        public string sessionId;
        public string studentId;
        public string teacherId;

        public static AttemptOwner Current() => new AttemptOwner
        {
            sessionId = PlayerPrefs.GetString(SessionResultUploader.SESSION_ID_KEY, ""),
            studentId = PlayerPrefs.GetString("StudentId", ""),
            teacherId = PlayerPrefs.GetString(SessionResultUploader.SESSION_TEACHER_KEY, "")
        };
    }

    /// <summary>Starts the attempt on the server, unless it turns out to be there already.</summary>
    private static async Task CreateAttempt(DocumentReference doc, string json, AttemptOwner owner)
    {
        if (attemptOnServer == null)
        {
            DocumentSnapshot existing = await doc.GetSnapshotAsync(Source.Server);
            if (existing.Exists)
            {
                attemptOnServer = true;
                await doc.UpdateAsync(new Dictionary<string, object>
                {
                    { "snapshot", json },
                    { "savedAt", FieldValue.ServerTimestamp }
                });
                return;
            }
        }

        FirebaseUser user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null)
            return;

        await doc.SetAsync(new Dictionary<string, object>
        {
            { "sessionId", owner.sessionId },
            { "studentId", owner.studentId },
            { "studentUid", user.UserId },
            { "teacherId", owner.teacherId },
            { "status", "playing" },
            { "snapshot", json },
            { "startedAt", FieldValue.ServerTimestamp },
            { "savedAt", FieldValue.ServerTimestamp }
        });
        attemptOnServer = true;
    }

    // ----------------------------------------------------------------- finishing

    /// <summary>
    /// The results are up: from here the drill can't be resumed or played again. Marks the
    /// attempt finished on the server and returns the server's finish time, which the result
    /// carries so the dashboards date it by when the drill was played - or null if the server
    /// couldn't be told. Call before anything the session may have forgotten.
    /// </summary>
    public static async Task<DateTime?> FinishAsync()
    {
        string key = CurrentKey();
        if (key == null)
            return null;

        MarkOverOnDevice(key);
        AttemptOwner owner = AttemptOwner.Current();

        try
        {
            DocumentReference doc = FirebaseFirestore.DefaultInstance.Collection(COLLECTION).Document(key);
            DocumentSnapshot existing = await doc.GetSnapshotAsync(Source.Server);

            // A drill played wholly offline never reached the server: start it, then finish it
            if (!existing.Exists)
            {
                attemptOnServer = false;
                await CreateAttempt(doc, "{}", owner);
            }
            else if (existing.GetValue<string>("status") == "finished")
            {
                return existing.GetValue<Timestamp>("finishedAt").ToDateTime();
            }

            await doc.UpdateAsync(new Dictionary<string, object>
            {
                { "status", "finished" },
                { "finishedAt", FieldValue.ServerTimestamp },
                { "savedAt", FieldValue.ServerTimestamp }
            });

            DocumentSnapshot finished = await doc.GetSnapshotAsync(Source.Server);
            return finished.GetValue<Timestamp>("finishedAt").ToDateTime();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Couldn't mark the drill finished on the server: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// The finish time the server recorded for a drill, for a result sent late; null if its
    /// attempt isn't finished (or can't be read).
    /// </summary>
    public static async Task<DateTime?> ReadFinishedAtAsync(string attemptId)
    {
        try
        {
            DocumentSnapshot attempt = await FirebaseFirestore.DefaultInstance
                .Collection(COLLECTION).Document(attemptId).GetSnapshotAsync(Source.Server);

            if (attempt.Exists && attempt.GetValue<string>("status") == "finished")
                return attempt.GetValue<Timestamp>("finishedAt").ToDateTime();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Couldn't read the drill's finish time: {ex.Message}");
        }
        return null;
    }

    private static void MarkOverOnDevice(string key)
    {
        PlayerPrefs.SetInt(OVER_PREFIX + key, 1);
        PlayerPrefs.DeleteKey(DRILL_PREFIX + key);
        PlayerPrefs.Save();
    }

    // ----------------------------------------------------------------- keys

    private static string CurrentKey()
    {
        if (!SessionResultUploader.IsTeacherSession)
            return null;
        return KeyFor(PlayerPrefs.GetString(SessionResultUploader.SESSION_ID_KEY, ""));
    }

    // The same pair the dashboard result is filed under
    private static string KeyFor(string sessionId)
    {
        string studentId = PlayerPrefs.GetString("StudentId", "");
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(studentId))
            return null;
        return sessionId + "_" + studentId;
    }
}

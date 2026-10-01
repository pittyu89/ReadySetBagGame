using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Starts the round, and on a player's first game turns it into a guided practice run first.
///
/// THE PRACTICE RUN
///
/// A strict, step-by-step walkthrough of everything the drill asks of the player, played in the
/// real house with the real systems: walking, turning the camera, the timer and pause button,
/// finding and picking up the go-bag, the bag button, opening furniture, reading an item,
/// opening a pocket, packing, the weight limit, unpacking, the weight meter, the Journal, the
/// exit door, a quiz question and its minigame. Each step waits until the player has actually
/// done it, and only what the step is teaching can be used - other items will not drag, other
/// furniture will not open, the door stays shut and the HUD buttons stay locked until their turn.
///
/// Nothing in it is timed or scored: the round clock never starts, the quiz asks the one
/// question the practice set up, and nothing reaches the Journal or the teacher dashboard.
/// When it is over the scene reloads into a normal drill and the player is marked as done, per
/// player profile, so it plays once. It can be skipped (SKIP TUTORIAL, with a confirmation)
/// and played again from the How-to-Play panel (<see cref="ReplayPractice"/>). A replay never
/// starts a drill of its own: finished or skipped, it goes back to where it was opened from -
/// the main menu, or the drill it was opened from mid-game (<see cref="DrillSnapshot"/>),
/// restored with its pause menu up, packing or at the quiz question it had reached.
///
/// THE NORMAL ROUND
///
/// Opens with the READY-SET-BAG splash and starts the clock when it clears. This used to hang
/// off the start-of-round tutorial slideshow, which now only lives behind How-to-Play.
///
/// In a teacher session the drill is kept saved on the device while the student packs
/// (<see cref="SessionDrillStore"/>), and a student who rejoins carries on from it, less the
/// time they were away, instead of starting over.
///
/// Other scripts ask the static gates here (<see cref="StorageAllowed"/>, <see cref="DragAllowed"/>,
/// <see cref="FinishDoorAllowed"/>, <see cref="IsPracticeRun"/>); outside a practice run every
/// gate is open, so a scene without this component plays exactly as before.
/// </summary>
public partial class OnboardingManager : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("Font for the coach cards. Falls back to the font of any text on the game's canvas.")]
    [SerializeField] private TMP_FontAsset font;
    [Tooltip("Button sprite for the coach cards; the finish prompt's green button.")]
    [SerializeField] private Sprite buttonSprite;

    [Header("Normal Round")]
    [Tooltip("The READY-SET-BAG splash. The round clock starts when it clears.")]
    [SerializeField] private ReadySetBagOverlay readySetBagOverlay;

    [Header("Practice Run")]
    [Tooltip("The item the practice packs and answers the quiz with. Its question and minigame " +
             "are the ones the practice plays.")]
    [SerializeField] private string practiceItemName = "Whistle";
    [Tooltip("A low-value item packed and then taken back out, to teach weight and unpacking.")]
    [SerializeField] private string extraItemName = "Speaker";
    [Tooltip("How far the player must walk before the movement step counts.")]
    [SerializeField] private float walkDistance = 4f;
    [Tooltip("How far the camera must be turned, in degrees, before the camera step counts.")]
    [SerializeField] private float orbitDegrees = 90f;

    private const string DONE_SUFFIX = "_OnboardingDone";
    private const int TOTAL_STEPS = 32;
    private const string GAME_SCENE = "GameScene";

    private static OnboardingManager active;
    private static bool replayRequested;
    // The drill a replay was opened from, waiting for the replay to end
    private static DrillSnapshot pendingResume;

    private bool practice;
    // Replayed from How-to-Play rather than a first game: it never ends in a new drill
    private bool replay;
    // This round is a drill coming back after a replay
    private DrillSnapshot resume;

    // Gates read by the rest of the game through the static accessors below
    private StorageFurniture targetFurniture;
    private bool storageOpenable;
    private bool doorAllowed;
    private enum DragRule { None, Only, All }
    private DragRule dragRule = DragRule.None;
    private InventoryItem dragOnly;

    // The scene's pieces
    private OnboardingOverlay overlay;
    private PlayerController player;
    private GoBagPickup goBag;
    private InventoryPanel inventory;
    private JournalPanel journal;
    private PauseManager pause;
    private FinishDoorHandler door;
    private QuizManager quiz;
    private RectTransform canvasRoot;

    private InventoryItem practiceItem;
    private InventoryItem extraItem;
    private int step;

    // Raised by the quiz while the practice question runs
    private bool answerResolved;
    private bool feedbackShown;
    private bool answeredCorrectly;
    private MonoBehaviour startedMinigame;
    private bool minigameFinished;
    private bool quizFinished;

    // ----------------------------------------------------------------- gates

    /// <summary>True for the whole of a practice run, from the first frame to the reload.</summary>
    public static bool IsPracticeRun => active != null && active.practice;

    /// <summary>The item whose question the practice quiz asks.</summary>
    public static string PracticeAnswerItem => active != null ? active.practiceItemName : "Whistle";

    /// <summary>Whether this furniture may be opened (and marked as openable) right now.</summary>
    public static bool StorageAllowed(StorageFurniture furniture)
    {
        return !IsPracticeRun || (active.storageOpenable && furniture == active.targetFurniture);
    }

    /// <summary>Whether this item may be picked up and dragged right now.</summary>
    public static bool DragAllowed(InventoryItem item)
    {
        if (!IsPracticeRun)
            return true;

        switch (active.dragRule)
        {
            case DragRule.All: return true;
            case DragRule.Only: return item != null && item == active.dragOnly;
            default: return false;
        }
    }

    /// <summary>Whether walking up to the exit door brings up the Are-you-finished prompt.</summary>
    public static bool FinishDoorAllowed => !IsPracticeRun || active.doorAllowed;

    /// <summary>Whether the current player has already been through the practice run.</summary>
    public static bool HasCompletedOnboarding()
    {
        return PlayerPrefs.GetInt(PlayerKey(), 0) == 1;
    }

    /// <summary>
    /// Whether the practice can be started again from How-to-Play. Not in a teacher session:
    /// that is one run per student with its result going to the dashboard, the same reason
    /// the pause menu hides Restart there.
    /// </summary>
    public static bool CanReplayPractice => string.IsNullOrEmpty(PlayerPrefs.GetString("SessionCode", ""));

    /// <summary>A drill coming back after a replayed practice, or null. Read by the spawner.</summary>
    public static DrillSnapshot ResumeSnapshot => active != null ? active.resume : null;

    /// <summary>
    /// Starts the practice run over from the beginning, from the main menu or mid-game, by
    /// loading the game scene fresh. A replay is a refresher, not the way into a drill: finishing
    /// or skipping it goes back to the main menu, or to the drill it was opened from.
    /// </summary>
    public static void ReplayPractice()
    {
        if (!CanReplayPractice)
            return;

        // During a first practice, just start that practice over
        if (active != null && active.practice && !active.replay)
        {
            Time.timeScale = 1f;
            LoadingScreen.LoadScene(GAME_SCENE);
            return;
        }

        // From a drill: save it to come back to. Replaying from a replay keeps the drill it has.
        if (active != null && !active.practice)
        {
            DrillSnapshot snapshot = DrillSnapshot.Capture();
            if (snapshot == null)
            {
                active.ConfirmReplayLosingDrill();
                return;
            }
            pendingResume = snapshot;
        }

        BeginReplay();
    }

    private static void BeginReplay()
    {
        // Lasts through reloads of the game scene (Restart from the pause menu), and is dropped
        // as soon as any other scene loads, so leaving mid-replay leaves no practice queued up
        if (!replayRequested)
            SceneManager.sceneLoaded += DropReplayOutsideGame;
        replayRequested = true;

        // The pause menu stops time; the new scene must not start frozen
        Time.timeScale = 1f;
        LoadingScreen.LoadScene(GAME_SCENE);
    }

    /// <summary>
    /// The drill has reached its results, so there is nothing to come back to: say so before it goes.
    /// A replay from here ends at the main menu.
    /// </summary>
    private void ConfirmReplayLosingDrill()
    {
        Confirm("REPLAY TUTORIAL?",
            "Your drill has finished, so it <color=#FF4343>can't be continued</color> after " +
            "the tutorial. The tutorial will finish at the main menu.",
            "REPLAY", "STAY",
            () =>
            {
                pendingResume = null;
                BeginReplay();
            });
    }

    /// <summary>
    /// A yes / no question in the coach cards' style, over whatever is on screen in the game
    /// scene. <paramref name="onYes"/> runs if the player agrees; saying no just closes it.
    /// </summary>
    public static void Confirm(string title, string body, string yesLabel, string noLabel, Action onYes)
    {
        TMP_FontAsset confirmFont = active != null ? active.font : null;
        Sprite sprite = active != null ? active.buttonSprite : null;

        // The same font the practice cards fall back to: the game canvas's
        GameTimer timer = FindFirstObjectByType<GameTimer>(FindObjectsInactive.Include);
        if (confirmFont == null && timer != null)
        {
            Canvas gameCanvas = timer.GetComponentInParent<Canvas>(true);
            TextMeshProUGUI anyText = gameCanvas != null ? gameCanvas.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            confirmFont = anyText != null ? anyText.font : null;
        }

        OnboardingOverlay confirm = OnboardingOverlay.CreateConfirmOnly(confirmFont, sprite);
        confirm.ShowConfirm(title, body,
            () =>
            {
                Destroy(confirm.gameObject);
                onYes?.Invoke();
            },
            () => Destroy(confirm.gameObject),
            yesLabel, noLabel);
    }

    private static void DropReplayOutsideGame(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single && scene.name != GAME_SCENE)
        {
            EndReplay();
            pendingResume = null;
        }
    }

    private static void EndReplay()
    {
        replayRequested = false;
        SceneManager.sceneLoaded -= DropReplayOutsideGame;
    }

    /// <summary>Per player, the same way the character choice is remembered.</summary>
    private static string PlayerKey()
    {
        bool isGuest = PlayerPrefs.GetString("IsGuest", "false") == "true";
        string userName = isGuest ? "Guest" : PlayerPrefs.GetString("StudentName", "User");
        return userName + DONE_SUFFIX;
    }

    // ----------------------------------------------------------------- lifecycle

    // Awake, so every other script's Start already sees the right answer from IsPracticeRun -
    // HouseSpawnRandomizer places the bag by it before anything else has run.
    private void Awake()
    {
        active = this;
        replay = replayRequested;

        // Taken on the load after the replay, so a restart of the drill starts fresh
        if (!replay && pendingResume != null)
        {
            resume = pendingResume;
            pendingResume = null;
        }
        // A teacher-session student back in a drill they left, or that closed on them. The
        // join found it, on the server, and has already taken off the time they were away.
        else if (!replay && SessionResultUploader.IsTeacherSession)
        {
            resume = SessionDrillStore.TakePrepared();
        }

        practice = replay || (resume == null && !HasCompletedOnboarding());
    }

    private void OnDestroy()
    {
        if (active == this)
            active = null;

        if (quiz != null)
        {
            quiz.AnswerResolved -= OnAnswerResolved;
            quiz.FeedbackShown -= OnFeedbackShown;
            quiz.MinigameStarted -= OnMinigameStarted;
            quiz.MinigameFinished -= OnMinigameFinished;
            quiz.PracticeQuizFinished -= OnPracticeQuizFinished;
        }
    }

    private void Start()
    {
        if (practice)
            StartCoroutine(RunPractice());
        else if (resume != null)
            StartCoroutine(ResumeDrill());
        else
            StartCoroutine(StartNormalRound());
    }

    // ----------------------------------------------------------------- resumed drill

    /// <summary>
    /// The drill the practice was replayed from, put back as it was and handed over with its
    /// pause menu up, so Continue picks up exactly where the player paused.
    /// </summary>
    private IEnumerator ResumeDrill()
    {
        // Every Start has run: the house, the bag and the clock are set up and won't reset it
        yield return null;
        resume.RestoreScene();
        StartSessionSaving();

        // Past packing: straight back into the quiz, at the question it had reached. The
        // round clock stays stopped, as the finish door left it.
        if (resume.InQuiz)
        {
            // Once the loading screen is away, so the question's clock isn't running behind it
            while (LoadingScreen.IsLoading)
                yield return null;
            yield return null;

            FinishDoorHandler door = FindFirstObjectByType<FinishDoorHandler>(FindObjectsInactive.Include);
            if (door != null)
                door.ResumeQuiz(resume.Quiz);

            // Offline, back to the pause menu the practice was replayed from, like packing -
            // unless it resumed into a minigame, whose screen sits over the pause menu
            if (!SessionResultUploader.IsTeacherSession && resume.Quiz.pendingMinigame < 0)
            {
                PauseManager quizPause = FindFirstObjectByType<PauseManager>();
                if (quizPause != null)
                    quizPause.OpenPauseMenu();
            }
            yield break;
        }

        // Left before the round began: begin it the normal way, from the splash
        if (!resume.TimerWasRunning)
        {
            yield return StartNormalRound();
            yield break;
        }

        while (LoadingScreen.IsLoading)
            yield return null;
        yield return null;

        StartRoundTimer();

        // In a teacher session the clock doesn't stop for the pause menu, so there is no
        // reason to hold the student in it: they carry straight on
        if (SessionResultUploader.IsTeacherSession)
            yield break;

        PauseManager pauseMenu = FindFirstObjectByType<PauseManager>();
        if (pauseMenu != null)
            pauseMenu.OpenPauseMenu();
    }

    // ----------------------------------------------------------------- teacher session

    private const float SESSION_SAVE_INTERVAL = 5f;
    private bool sessionSaving;

    /// <summary>
    /// Keeps a teacher-session drill saved while the student plays, on the device and the
    /// server (<see cref="SessionDrillStore"/>), so leaving, or the game closing, doesn't hand
    /// them a fresh start when they rejoin.
    /// </summary>
    private void StartSessionSaving()
    {
        if (practice || sessionSaving || !SessionResultUploader.IsTeacherSession)
            return;

        sessionSaving = true;
        StartCoroutine(SaveSessionDrillRegularly());
    }

    private IEnumerator SaveSessionDrillRegularly()
    {
        // Once the results are up Capture returns null and the store is marked over, so later
        // rounds of this save nothing
        while (true)
        {
            SaveSessionDrill();
            yield return new WaitForSecondsRealtime(SESSION_SAVE_INTERVAL);
        }
    }

    /// <summary>
    /// Saves the teacher-session drill as it stands now. <paramref name="now"/> sends it to the
    /// server straight away rather than on the store's own schedule: the pause menu's Exit
    /// does, on the way out before it forgets the session, and so does the quiz as each
    /// answer is scored.
    /// </summary>
    public static void SaveSessionDrill(bool now = false)
    {
        if (active == null || !active.sessionSaving || !SessionResultUploader.IsTeacherSession)
            return;

        SessionDrillStore.Save(DrillSnapshot.Capture(), now);
    }

    // Phones close apps from the background without warning; save on the way there
    private void OnApplicationPause(bool paused)
    {
        if (paused)
            SaveSessionDrill(now: true);
    }

    private void OnApplicationQuit()
    {
        SaveSessionDrill(now: true);
    }

    // ----------------------------------------------------------------- normal round

    private IEnumerator StartNormalRound()
    {
        // One frame in, so the splash's blurred snapshot has the spawned house behind it
        yield return null;

        // The player and bag are placed by now, so the first save already holds this drill
        StartSessionSaving();

        // The loading screen is still sliding away when the scene starts. Wait it out, or the
        // splash freezes it into its blurred backdrop.
        while (LoadingScreen.IsLoading)
            yield return null;

        // It destroys itself on the frame it clears the flag; give that one frame to land
        yield return null;

        if (readySetBagOverlay == null)
        {
            StartRoundTimer();
            yield break;
        }

        // The clock waits for the splash to clear, so the seconds of "READY-SET-BAG!!" are
        // not counted against a player who cannot see the room yet
        readySetBagOverlay.Finished += OnSplashFinished;
        readySetBagOverlay.gameObject.SetActive(true);
    }

    private void OnSplashFinished()
    {
        readySetBagOverlay.Finished -= OnSplashFinished;
        StartRoundTimer();
    }

    private static void StartRoundTimer()
    {
        GameTimer timer = FindFirstObjectByType<GameTimer>();
        if (timer != null)
            timer.StartTimer();
    }
}

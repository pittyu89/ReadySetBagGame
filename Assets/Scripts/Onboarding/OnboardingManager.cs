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
/// player profile, so it plays once. It can be skipped (SKIP PRACTICE, with a confirmation)
/// and played again from the How-to-Play panel (<see cref="ReplayPractice"/>).
///
/// THE NORMAL ROUND
///
/// Opens with the READY-SET-BAG splash and starts the clock when it clears. This used to hang
/// off the start-of-round tutorial slideshow, which now only lives behind How-to-Play.
///
/// Other scripts ask the static gates here (<see cref="StorageAllowed"/>, <see cref="DragAllowed"/>,
/// <see cref="FinishDoorAllowed"/>, <see cref="IsPracticeRun"/>); outside a practice run every
/// gate is open, so a scene without this component plays exactly as before.
/// </summary>
public class OnboardingManager : MonoBehaviour
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

    private static OnboardingManager active;

    private bool practice;

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

    /// <summary>
    /// Starts the practice run over from the beginning, from the main menu or mid-game: the
    /// player is marked as not having done it and the game scene is loaded fresh.
    /// </summary>
    public static void ReplayPractice()
    {
        if (!CanReplayPractice)
            return;

        PlayerPrefs.DeleteKey(PlayerKey());
        PlayerPrefs.Save();

        // The pause menu stops time; the new scene must not start frozen
        Time.timeScale = 1f;
        LoadingScreen.LoadScene("GameScene");
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
        practice = !HasCompletedOnboarding();
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
        else
            StartCoroutine(StartNormalRound());
    }

    // ----------------------------------------------------------------- normal round

    private IEnumerator StartNormalRound()
    {
        // One frame in, so the splash's blurred snapshot has the spawned house behind it
        yield return null;

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

    // ----------------------------------------------------------------- practice run

    private IEnumerator RunPractice()
    {
        // Let the spawner put the player in the house first
        yield return null;
        ResolveScene();

        overlay = OnboardingOverlay.Create(font, buttonSprite);
        overlay.SkipRequested += OnSkipRequested;
        overlay.KeepClearOf(() => InventoryItemDragHandler.OpenDescriptionPanel);
        StartCoroutine(FollowPause());

        SetLocked(inventory != null ? inventory.BagButtonGroup : null, true);
        SetLocked(journal != null ? journal.OpenButtonGroup : null, true);

        PreparePracticeStorage();

        // --- Welcome ---------------------------------------------------------------------
        yield return Info("WELCOME!",
            "Before your first drill, let's practice. We'll go through every part of the game " +
            "one step at a time.\n\nNothing here is timed or scored.",
            "LET'S GO", null, OnboardingOverlay.CardPlace.Center);

        yield return Info("YOUR MISSION",
            "An earthquake has struck! Find your <color=#FF8B43>go-bag</color>, pack it with the " +
            "right emergency supplies, then leave through the <color=#FF8B43>exit door</color>.",
            "NEXT", null, OnboardingOverlay.CardPlace.Center);

        // --- Moving ----------------------------------------------------------------------
        yield return Do("WALK AROUND",
            "Use the <color=#FF8B43>joystick</color> to walk.\n(On a keyboard: WASD or the arrow keys.)",
            WalkedFar(walkDistance), OnboardingOverlay.Block.None, OnboardingOverlay.CardPlace.Top,
            Find("Joystick"));

        yield return Do("LOOK AROUND",
            "<color=#FF8B43>Drag on an empty part of the screen</color> to turn the camera.",
            TurnedCamera(orbitDegrees), OnboardingOverlay.Block.None, OnboardingOverlay.CardPlace.Center);

        // --- HUD -------------------------------------------------------------------------
        yield return Info("THE TIMER",
            "In a real drill, this clock <color=#FF4343>counts down</color> while you search and " +
            "pack. When it reaches zero, packing ends and the quiz begins.\n\nIt's stopped for this practice.",
            "NEXT", Find("Timer"));

        yield return Info("PAUSE",
            "This button pauses the game. You can open <color=#FF8B43>How to Play</color> from the " +
            "pause menu any time to review these tips.",
            "NEXT", pause != null && pause.PauseButton != null ? (RectTransform)pause.PauseButton.transform : null);

        // --- The go-bag ------------------------------------------------------------------
        yield return Do("FIND YOUR GO-BAG",
            "Follow the arrow to your <color=#FF8B43>go-bag</color> and walk into it to pick it up.\n" +
            "In a real drill, the bag and your starting spot are different every time.",
            () => GoBagPickup.IsBagPickedUp() && !BagPickupPose.IsPlaying,
            OnboardingOverlay.Block.None, OnboardingOverlay.CardPlace.Top, null,
            () => goBag != null ? goBag.transform.position + Vector3.up * 1.2f : Vector3.zero);

        SetLocked(inventory != null ? inventory.BagButtonGroup : null, false);
        RectTransform bagButton = inventory != null && inventory.BagButtonGroup != null
            ? (RectTransform)inventory.BagButtonGroup.transform : null;

        yield return Do("CHECK YOUR BAG",
            "You have your go-bag! Tap the <color=#FF8B43>bag button</color> to look inside it.",
            () => inventory != null && inventory.IsOpen,
            OnboardingOverlay.Block.OutsideTargets, OnboardingOverlay.CardPlace.Auto, bagButton);

        yield return Info("YOUR GO-BAG",
            "This is your go-bag. It has <color=#FF8B43>pockets</color> you'll open to pack " +
            "supplies. It's empty for now - let's go find something to put in it.",
            "NEXT", inventory != null ? inventory.BagArt : null);

        yield return Do("CLOSE THE BAG",
            "Tap the <color=#FF8B43>X</color> to close your bag.",
            () => !inventory.IsOpen,
            OnboardingOverlay.Block.OutsideTargets, OnboardingOverlay.CardPlace.Auto, CloseButtonRect());

        // --- Searching furniture ---------------------------------------------------------
        storageOpenable = true;
        string furnitureName = targetFurniture != null ? PrettyName(targetFurniture) : "furniture";

        yield return Do("SEARCH THE FURNITURE",
            "Supplies are stored inside furniture. Furniture you can search shows a " +
            "<color=#96B000>green arrow</color> when you're close.\n" +
            "Walk up to the <color=#FF8B43>" + furnitureName + "</color> and tap it.",
            () => inventory != null && inventory.IsStorageOpen,
            OnboardingOverlay.Block.None, OnboardingOverlay.CardPlace.Top, null,
            () => FurnitureTop());

        // Leaving the storage now would strand the steps that pack from it
        inventory.HideCloseButton();

        yield return Info("SEARCHING",
            "This is inside the " + furnitureName + ". Its supplies sit in its " +
            "<color=#FF8B43>compartments</color>. Your go-bag is on the left.",
            "NEXT", inventory.StoragePicture);

        yield return DoTracking("READ AN ITEM",
            "Tap the <color=#FF8B43>" + practiceItemName + "</color> to see what it is and how much it weighs.",
            () => InventoryItemDragHandler.IsDescriptionOpen,
            OnboardingOverlay.Block.OutsideTargets, OnboardingOverlay.CardPlace.Auto, null,
            () => ItemRect(practiceItem));

        yield return DoTracking("READ AN ITEM",
            "Every item shows its name, weight and what it's for. When you've read it, " +
            "<color=#FF8B43>tap anywhere</color> to close it.",
            () => !InventoryItemDragHandler.IsDescriptionOpen,
            OnboardingOverlay.Block.None, OnboardingOverlay.CardPlace.Auto, null,
            () => InventoryItemDragHandler.OpenDescriptionPanel);

        yield return Do("OPEN A POCKET",
            "Tap one of your bag's <color=#FF8B43>pockets</color> to open it.",
            () => inventory.IsBagPocketOpen,
            OnboardingOverlay.Block.OutsideTargets, OnboardingOverlay.CardPlace.Top, inventory.BagArt);

        // --- Packing ---------------------------------------------------------------------
        dragRule = DragRule.Only;
        dragOnly = practiceItem;

        yield return DoTracking("PACK IT",
            "Drag the <color=#FF8B43>" + practiceItemName + "</color> into the open pocket.",
            () => InBag(practiceItem),
            OnboardingOverlay.Block.None, OnboardingOverlay.CardPlace.Top,
            () => inventory.IsBagPocketOpen ? null : "Your pocket is closed - <color=#FF8B43>tap a pocket</color> to open it first.",
            () => ItemRect(practiceItem), () => inventory.OpenPocket);

        dragOnly = extraItem;

        yield return DoTracking("PACK ANOTHER",
            "Now drag the <color=#FF8B43>" + extraItemName + "</color> into your bag too.",
            () => InBag(extraItem),
            OnboardingOverlay.Block.None, OnboardingOverlay.CardPlace.Top,
            () => inventory.IsBagPocketOpen ? null : "Your pocket is closed - <color=#FF8B43>tap a pocket</color> to open it first.",
            () => ItemRect(extraItem), () => inventory.OpenPocket);

        dragRule = DragRule.None;

        float limit = InventoryManager.Instance != null ? InventoryManager.Instance.GetGoBagWeightLimit() : 0f;
        string limitText = limit > 0f ? " of <color=#FF4343>" + limit.ToString("0.#") + " kg</color>" : "";

        yield return Info("CHOOSE WISELY",
            "Not everything is worth carrying! A " + extraItemName + " won't help you in an emergency.\n\n" +
            "Your bag can only hold a limited weight" + limitText + ". Extra weight slows you down, " +
            "and useless items lower your score.",
            "NEXT", inventory.OpenPocket);

        dragRule = DragRule.Only;
        dragOnly = extraItem;

        yield return DoTracking("UNPACK IT",
            "Drag the <color=#FF8B43>" + extraItemName + "</color> out of your bag and back into the " + furnitureName + ".",
            () => !InBag(extraItem),
            OnboardingOverlay.Block.None, OnboardingOverlay.CardPlace.Top,
            () => inventory.IsBagPocketOpen || InventoryItemDragHandler.IsAnyItemBeingDragged
                ? null
                : "Open the pocket the " + extraItemName + " is in first.",
            () => ItemRect(extraItem), () => inventory.StoragePicture);

        dragRule = DragRule.None;
        inventory.ShowCloseButton();

        yield return Do("CLOSE THE STORAGE",
            "Well packed! Tap the <color=#FF8B43>X</color> to close the storage.",
            () => !inventory.IsOpen,
            OnboardingOverlay.Block.OutsideTargets, OnboardingOverlay.CardPlace.Auto, CloseButtonRect());

        storageOpenable = false;

        yield return Info("WEIGHT METER",
            "The ring around the bag button shows <color=#FF8B43>how full your bag is</color> by weight. " +
            "<color=#96B000>Green</color> is fine, <color=#FFD700>yellow</color> is getting heavy, " +
            "<color=#FF4343>red</color> is full.\n\nThe heavier your bag, the slower you walk.",
            "NEXT", bagButton);

        // --- The Journal -----------------------------------------------------------------
        SetLocked(journal != null ? journal.OpenButtonGroup : null, false);

        yield return Do("THE JOURNAL",
            "Tap the <color=#FF8B43>Journal</color>.",
            () => journal != null && journal.IsOpen,
            OnboardingOverlay.Block.OutsideTargets, OnboardingOverlay.CardPlace.Auto,
            journal != null && journal.OpenButton != null ? (RectTransform)journal.OpenButton.transform : null);

        yield return Info("THE JOURNAL",
            "The Journal lists every item in the game. Items start <color=#FF4343>locked</color>. " +
            "Pack an item and finish a drill to <color=#96B000>unlock</color> it and learn more about it.",
            "NEXT", null, OnboardingOverlay.CardPlace.Bottom);

        yield return Do("THE JOURNAL",
            "Tap the <color=#FF8B43>X</color> to close the Journal.",
            () => journal == null || !journal.IsOpen,
            OnboardingOverlay.Block.OutsideTargets, OnboardingOverlay.CardPlace.Auto,
            journal != null && journal.CloseButton != null ? (RectTransform)journal.CloseButton.transform : null);

        // --- Finishing -------------------------------------------------------------------
        doorAllowed = true;

        yield return Do("HEAD OUT",
            "When you're done packing, go to the <color=#FF8B43>exit door</color>. Follow the arrow.",
            () => door != null && door.FinishPromptPanel != null && door.FinishPromptPanel.activeInHierarchy,
            OnboardingOverlay.Block.None, OnboardingOverlay.CardPlace.Top, null,
            () => door != null ? door.transform.position + Vector3.up * 1.5f : Vector3.zero);

        yield return Do("ARE YOU FINISHED?",
            "In a real drill, saying <color=#FF8B43>Yes</color> ends packing for good - make sure " +
            "you have what you need! Tap Yes.",
            () => quiz != null && quiz.DialogueBox != null && quiz.DialogueBox.activeInHierarchy,
            OnboardingOverlay.Block.OutsideTargets, OnboardingOverlay.CardPlace.Bottom,
            door != null && door.YesButton != null ? (RectTransform)door.YesButton.transform : null);

        // --- The quiz --------------------------------------------------------------------
        yield return Info("THE QUIZ",
            "Each question describes an emergency. Answer by dragging the <color=#FF8B43>right item " +
            "from your bag</color> into the answer box.",
            "NEXT", QuizBoxRect());

        yield return Info("QUESTION TIMER",
            "In a real drill, this bar <color=#FF4343>counts down</color> for every question. " +
            "If it runs out, the question is marked wrong.",
            "NEXT", QuestionTimerRect());

        dragRule = DragRule.All;

        yield return DoTracking("ANSWER IT",
            "Open a pocket and drag the <color=#FF8B43>" + practiceItemName + "</color> into the answer box.",
            () => answerResolved,
            OnboardingOverlay.Block.None, OnboardingOverlay.CardPlace.Top, null,
            () => quiz.AnswerBox != null ? (RectTransform)quiz.AnswerBox.transform : null,
            () => inventory.OpenPocket);

        // The verdict banner plays on its own, then the quiz holds on the explanation for us
        overlay.Clear();
        yield return new WaitUntil(() => feedbackShown || quizFinished);

        // --- Feedback --------------------------------------------------------------------
        yield return Info(answeredCorrectly ? "CORRECT!" : "NICE TRY!",
            (answeredCorrectly
                ? "You got it! A <color=#96B000>correct</color> answer earns points, and the item is used up."
                : "Not this time. A <color=#FF4343>wrong</color> answer earns no points, and the item stays in your bag.") +
            "\n\nRight or wrong, the box explains <color=#FF8B43>why</color> - read it before you continue!",
            "CONTINUE", QuizBoxRect());

        quiz.ContinueAfterFeedback();
        yield return new WaitUntil(() => startedMinigame != null || quizFinished);

        // --- The minigame ----------------------------------------------------------------
        if (startedMinigame != null)
        {
            // Let the minigame's panel and objective card finish sliding in first
            yield return new WaitForSecondsRealtime(0.9f);

            MinigameObjective objective = startedMinigame.GetComponentInChildren<MinigameObjective>(true);
            yield return Info("MINIGAME",
                "Every question is followed by a quick minigame. Your task is written on the " +
                "<color=#FF8B43>objective card</color> - do it to earn points!\n" +
                "In a real drill, the bar at the top <color=#FF4343>counts down</color>.",
                "GO!", objective != null ? (RectTransform)objective.transform : null,
                OnboardingOverlay.CardPlace.Center);

            overlay.Clear();
            yield return new WaitUntil(() => minigameFinished || quizFinished);
        }
        else
        {
            step++;
        }

        yield return new WaitUntil(() => quizFinished);

        // --- Done ------------------------------------------------------------------------
        yield return Info("PRACTICE COMPLETE!",
            "You're ready for the real drill. This time the <color=#FF4343>timer runs</color>, your " +
            "bag and starting spot are random, and your score counts: what you pack, your answers, " +
            "the minigames and the time you have left.\n\nWant a refresher later? Replay this " +
            "practice from <color=#FF8B43>How to Play</color>. Good luck!",
            "START DRILL", null, OnboardingOverlay.CardPlace.Center);

        if (step != TOTAL_STEPS)
            Debug.LogWarning($"Onboarding counted {step} steps but shows {TOTAL_STEPS} as the total.", this);

        CompletePractice();
    }

    /// <summary>
    /// SKIP PRACTICE: the game holds still while it asks, then either carries on where it
    /// was or goes straight to the real drill, counting the practice as done.
    /// </summary>
    private void OnSkipRequested()
    {
        if (overlay.IsConfirmOpen)
            return;

        float timeScale = Time.timeScale;
        Time.timeScale = 0f;

        overlay.ShowConfirm("SKIP PRACTICE?",
            "You'll go straight to the real drill, where the <color=#FF4343>timer runs</color> " +
            "and your score counts.\n\nYou can play the practice again any time from " +
            "<color=#FF8B43>How to Play</color>.",
            CompletePractice,
            () => Time.timeScale = timeScale);
    }

    private void CompletePractice()
    {
        PlayerPrefs.SetInt(PlayerKey(), 1);
        PlayerPrefs.Save();

        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    // ----------------------------------------------------------------- step runners

    /// <summary>
    /// A card that has to be read: everything is blocked apart from its button, and the
    /// character stands still, until the button is tapped.
    /// </summary>
    private IEnumerator Info(string title, string body, string buttonText, RectTransform highlight,
                             OnboardingOverlay.CardPlace place = OnboardingOverlay.CardPlace.Auto)
    {
        step++;
        bool done = false;
        bool couldMove = SetMovement(false);

        overlay.SetWorldTarget(null);
        overlay.SetTargets(highlight);
        overlay.SetBlock(OnboardingOverlay.Block.Everything);
        overlay.ShowCard(StepLabel(), title, body, buttonText, () => done = true, place);

        yield return new WaitUntil(() => done);

        overlay.Clear();
        if (couldMove)
            SetMovement(true);
    }

    /// <summary>
    /// An instruction: the step ends when <paramref name="isDone"/> holds. Points at a fixed HUD
    /// element, a spot in the house, or nothing.
    /// </summary>
    private IEnumerator Do(string title, string body, Func<bool> isDone, OnboardingOverlay.Block block,
                           OnboardingOverlay.CardPlace place, RectTransform highlight = null,
                           Func<Vector3> worldTarget = null)
    {
        Func<RectTransform>[] highlights = highlight != null
            ? new Func<RectTransform>[] { () => highlight }
            : new Func<RectTransform>[0];
        return RunInstruction(title, body, isDone, block, place, worldTarget, null, highlights);
    }

    /// <summary>
    /// An instruction whose highlights are looked up every frame - inventory items are rebuilt
    /// whenever anything moves - and whose text <paramref name="hint"/> can swap for a nudge
    /// while it returns something.
    /// </summary>
    private IEnumerator DoTracking(string title, string body, Func<bool> isDone, OnboardingOverlay.Block block,
                                   OnboardingOverlay.CardPlace place, Func<string> hint,
                                   params Func<RectTransform>[] highlights)
    {
        return RunInstruction(title, body, isDone, block, place, null, hint, highlights);
    }

    private IEnumerator RunInstruction(string title, string body, Func<bool> isDone, OnboardingOverlay.Block block,
                                       OnboardingOverlay.CardPlace place, Func<Vector3> worldTarget,
                                       Func<string> hint, Func<RectTransform>[] highlights)
    {
        step++;
        overlay.SetBlock(block);
        overlay.SetWorldTarget(worldTarget);
        overlay.ShowCard(StepLabel(), title, body, null, null, place);

        while (!isDone())
        {
            RectTransform[] rects = new RectTransform[highlights.Length];
            for (int i = 0; i < highlights.Length; i++)
                rects[i] = highlights[i] != null ? highlights[i]() : null;
            overlay.SetTargets(rects);

            string nudge = hint != null ? hint() : null;
            overlay.SetBody(string.IsNullOrEmpty(nudge) ? body : nudge);
            yield return null;
        }

        overlay.Clear();
    }

    private string StepLabel()
    {
        return "PRACTICE  " + step + " / " + TOTAL_STEPS;
    }

    // ----------------------------------------------------------------- conditions

    private Func<bool> WalkedFar(float distance)
    {
        float walked = 0f;
        Vector3 last = player != null ? player.transform.position : Vector3.zero;

        return () =>
        {
            if (player == null)
                return true;

            Vector3 now = player.transform.position;
            Vector3 delta = now - last;
            delta.y = 0f;
            walked += delta.magnitude;
            last = now;
            return walked >= distance;
        };
    }

    private Func<bool> TurnedCamera(float degrees)
    {
        float turned = 0f;
        float last = CameraYaw();

        return () =>
        {
            if (Camera.main == null)
                return true;

            float now = CameraYaw();
            turned += Mathf.Abs(Mathf.DeltaAngle(last, now));
            last = now;
            return turned >= degrees;
        };
    }

    private static float CameraYaw()
    {
        Camera cam = Camera.main;
        return cam != null ? cam.transform.eulerAngles.y : 0f;
    }

    private static bool InBag(InventoryItem item)
    {
        return item != null && InventoryManager.Instance != null
               && InventoryManager.Instance.GetGoBagItems().Contains(item);
    }

    // ----------------------------------------------------------------- quiz events

    private void OnAnswerResolved(bool correct) => answerResolved = true;

    private void OnFeedbackShown(bool correct)
    {
        answeredCorrectly = correct;
        feedbackShown = true;
    }
    private void OnMinigameStarted(MonoBehaviour minigame) => startedMinigame = minigame;
    private void OnMinigameFinished() => minigameFinished = true;
    private void OnPracticeQuizFinished() => quizFinished = true;

    // ----------------------------------------------------------------- setup

    private void ResolveScene()
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        player = playerObject != null ? playerObject.GetComponent<PlayerController>() : FindFirstObjectByType<PlayerController>();

        goBag = FindFirstObjectByType<GoBagPickup>();
        inventory = FindFirstObjectByType<InventoryPanel>();
        journal = FindFirstObjectByType<JournalPanel>(FindObjectsInactive.Include);
        pause = FindFirstObjectByType<PauseManager>();
        door = FindFirstObjectByType<FinishDoorHandler>(FindObjectsInactive.Include);
        quiz = FindFirstObjectByType<QuizManager>(FindObjectsInactive.Include);

        GameTimer timer = FindFirstObjectByType<GameTimer>(FindObjectsInactive.Include);
        Canvas gameCanvas = timer != null ? timer.GetComponentInParent<Canvas>(true) : null;
        canvasRoot = gameCanvas != null ? (RectTransform)gameCanvas.rootCanvas.transform : null;

        if (font == null && gameCanvas != null)
        {
            TextMeshProUGUI anyText = gameCanvas.GetComponentInChildren<TextMeshProUGUI>(true);
            if (anyText != null)
                font = anyText.font;
        }

        if (quiz != null)
        {
            quiz.AnswerResolved += OnAnswerResolved;
            quiz.FeedbackShown += OnFeedbackShown;
            quiz.MinigameStarted += OnMinigameStarted;
            quiz.MinigameFinished += OnMinigameFinished;
            quiz.PracticeQuizFinished += OnPracticeQuizFinished;
        }
    }

    /// <summary>
    /// Picks the furniture nearest the practice bag and stocks it with just the two items the
    /// practice teaches, so there is exactly one thing to find and nothing to get lost in.
    /// </summary>
    private void PreparePracticeStorage()
    {
        SupplyItem practiceSupply = FindSupply(practiceItemName);
        SupplyItem extraSupply = FindSupply(extraItemName);
        if (practiceSupply == null || extraSupply == null)
        {
            Debug.LogWarning($"Onboarding: no SupplyItem named {practiceItemName} or {extraItemName} in Resources/ItemData.", this);
            return;
        }

        Vector3 from = goBag != null ? goBag.transform.position
                     : player != null ? player.transform.position : Vector3.zero;
        GameDifficultyApplier areas = FindAnyObjectByType<GameDifficultyApplier>();

        List<StorageFurniture> candidates = new List<StorageFurniture>();
        foreach (StorageFurniture furniture in FindObjectsByType<StorageFurniture>(FindObjectsSortMode.None))
        {
            if (furniture.Layout == null || Mathf.Abs(furniture.transform.position.y - from.y) > 2f)
                continue;
            if (areas != null && !areas.IsAreaOpen(furniture.transform))
                continue;
            candidates.Add(furniture);
        }

        candidates.Sort((a, b) => a.GetPlanarDistanceTo(from).CompareTo(b.GetPlanarDistanceTo(from)));

        foreach (StorageFurniture furniture in candidates)
        {
            // Asking for a compartment fills the whole house first, so the clear below sticks
            if (furniture.GetCompartment(0) == null)
                continue;

            for (int i = 0; i < furniture.CompartmentCount; i++)
                furniture.GetCompartment(i).Clear();

            InventoryItem packed = InventoryItem.FromSupply(practiceSupply);
            InventoryItem extra = InventoryItem.FromSupply(extraSupply);
            if (furniture.TryStore(packed) && furniture.TryStore(extra))
            {
                targetFurniture = furniture;
                practiceItem = packed;
                extraItem = extra;
                return;
            }
        }

        Debug.LogWarning("Onboarding: no furniture near the go-bag has room for the practice items.", this);
    }

    private static SupplyItem FindSupply(string itemName)
    {
        foreach (SupplyItem supply in Resources.LoadAll<SupplyItem>("ItemData"))
        {
            if (supply != null && string.Equals(supply.ItemName, itemName, StringComparison.OrdinalIgnoreCase))
                return supply;
        }
        return null;
    }

    // ----------------------------------------------------------------- lookups

    private RectTransform Find(string path)
    {
        return canvasRoot != null ? canvasRoot.Find(path) as RectTransform : null;
    }

    private RectTransform CloseButtonRect()
    {
        return inventory != null && inventory.CloseButton != null ? (RectTransform)inventory.CloseButton.transform : null;
    }

    /// <summary>
    /// The quiz's dialogue box as the player sees it. Its group is laid out larger than the
    /// art, so the framed backing panel is used when there is one.
    /// </summary>
    private RectTransform QuizBoxRect()
    {
        if (quiz == null || quiz.DialogueBox == null)
            return null;

        Transform backing = quiz.DialogueBox.transform.Find("PanelBacking");
        return (RectTransform)(backing != null ? backing : quiz.DialogueBox.transform);
    }

    private RectTransform QuestionTimerRect()
    {
        return quiz != null && quiz.QuestionTimerBar != null ? (RectTransform)quiz.QuestionTimerBar.transform : null;
    }

    /// <summary>The on-screen tile of an item, wherever it sits right now. Items are rebuilt on every move.</summary>
    private RectTransform ItemRect(InventoryItem item)
    {
        if (item == null)
            return null;

        foreach (InventoryGridItemUI ui in FindObjectsByType<InventoryGridItemUI>(FindObjectsSortMode.None))
        {
            if (ui.GetItem() == item)
                return (RectTransform)ui.transform;
        }
        return null;
    }

    private Vector3 FurnitureTop()
    {
        if (targetFurniture == null)
            return Vector3.zero;

        Collider collider = targetFurniture.GetComponentInChildren<Collider>();
        if (collider == null)
            return targetFurniture.transform.position + Vector3.up * 1.5f;

        Bounds bounds = collider.bounds;
        return new Vector3(bounds.center.x, bounds.max.y + 0.4f, bounds.center.z);
    }

    /// <summary>
    /// What to call the furniture on the card. Taken from its layout ("SmallDrawerLayout" reads
    /// as "small drawer"), since the object names come straight off the house model
    /// ("Drawer B.003").
    /// </summary>
    private static string PrettyName(StorageFurniture furniture)
    {
        string name = furniture.Layout != null ? furniture.Layout.name : furniture.name;
        if (name.EndsWith("Layout"))
            name = name.Substring(0, name.Length - "Layout".Length);

        System.Text.StringBuilder words = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
                words.Append(' ');
            words.Append(c);
        }
        return words.ToString().Trim().ToLowerInvariant();
    }

    // ----------------------------------------------------------------- helpers

    /// <summary>Stops or restores the character. Returns whether it could move before.</summary>
    private bool SetMovement(bool enabled)
    {
        if (player == null)
            return false;

        bool was = player.enabled;
        player.SetMovementEnabled(enabled);
        return was;
    }

    /// <summary>Greys out a HUD button group and makes it ignore taps, until its step comes.</summary>
    private static void SetLocked(GameObject group, bool locked)
    {
        if (group == null)
            return;

        CanvasGroup canvasGroup = group.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = group.AddComponent<CanvasGroup>();

        canvasGroup.interactable = !locked;
        canvasGroup.blocksRaycasts = !locked;
        canvasGroup.alpha = locked ? 0.35f : 1f;
    }

    /// <summary>The overlay steps aside while the pause menu is up, so its buttons can be used.</summary>
    private IEnumerator FollowPause()
    {
        while (overlay != null)
        {
            overlay.SetHidden(pause != null && pause.IsPaused);
            yield return null;
        }
    }
}

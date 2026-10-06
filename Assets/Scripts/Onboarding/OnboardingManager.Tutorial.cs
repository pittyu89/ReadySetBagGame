using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The tutorial itself: the scripted run of steps a first-time player is walked through, and
/// finishing it - or skipping it, which only a replay allows.
/// </summary>
public partial class OnboardingManager
{
    // ----------------------------------------------------------------- practice run

    private IEnumerator RunPractice()
    {
        // Let the spawner put the player in the house first
        yield return null;
        ResolveScene();

        overlay = OnboardingOverlay.Create(font, buttonSprite);

        // The first run has to be played through; only a replay can be skipped
        overlay.SetSkipAvailable(replay);
        if (replay)
            overlay.SkipRequested += OnSkipRequested;
        overlay.KeepClearOf(() => InventoryItemDragHandler.OpenDescriptionPanel);
        StartCoroutine(FollowPause());

        SetLocked(inventory != null ? inventory.BagButtonGroup : null, true);
        SetLocked(journal != null ? journal.OpenButtonGroup : null, true);

        PreparePracticeStorage();

        // --- Welcome ---------------------------------------------------------------------
        yield return Info("WELCOME!",
            (replay ? "Let's go through the tutorial again. " : "Before your first drill, here's a quick tutorial. ") +
            "We'll go through every part of the game one step at a time.\n\nNothing here is timed or scored.",
            "LET'S GO", null, OnboardingOverlay.CardPlace.Center);

        yield return Info("YOUR MISSION",
            "An earthquake has struck! Find your <color=#FF8B43>go-bag</color>, pack it with the " +
            "right emergency supplies, then leave through the <color=#FF8B43>exit door</color>.",
            "NEXT", null, OnboardingOverlay.CardPlace.Center);

        // --- Moving ----------------------------------------------------------------------
        yield return Do("WALK AROUND",
            "Use the <color=#FF8B43>joystick</color> to walk.",
            WalkedFar(walkDistance), OnboardingOverlay.Block.None, OnboardingOverlay.CardPlace.Top,
            Find("Joystick"));

        yield return Do("LOOK AROUND",
            "<color=#FF8B43>Drag on an empty part of the screen</color> to turn the camera.",
            TurnedCamera(orbitDegrees), OnboardingOverlay.Block.None, OnboardingOverlay.CardPlace.Center);

        // --- HUD -------------------------------------------------------------------------
        yield return Info("THE TIMER",
            "In a real drill, this clock <color=#FF4343>counts down</color> while you search and " +
            "pack. When it reaches zero, packing ends and the quiz begins.\n\nIt's stopped for this tutorial.",
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

        // The bag zooms in on a pocket and opens it by itself; point at it once it's open
        BagPouchNavigator pouches = inventory.PouchNavigator;
        bool severalPockets = pouches != null && pouches.PouchCount > 1;
        while (pouches != null && inventory.IsOpen && pouches.CurrentPouch < 0)
            yield return null;

        yield return Info("YOUR GO-BAG",
            "This is your go-bag, open at one of the <color=#FF8B43>pockets</color> you pack " +
            "supplies into. It's empty for now - let's go find something to put in it.",
            "NEXT", inventory.OpenPocket);

        // A bag with several pockets has the player move to another one
        if (severalPockets)
        {
            int startPouch = pouches.CurrentPouch;
            yield return Do("SWITCH POCKETS",
                "Tap the <color=#FF8B43>arrows</color> above your bag to move to another pocket. " +
                "The name of the pocket you're in is between them.",
                () => pouches.CurrentPouch >= 0 && pouches.CurrentPouch != startPouch,
                OnboardingOverlay.Block.OutsideTargets, OnboardingOverlay.CardPlace.Auto, pouches.Controls);
        }
        else
        {
            step++;
        }

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
            "<color=#FF8B43>compartments</color>. Your go-bag is on the left, open at the " +
            "pocket you were last in.",
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

        // --- Packing ---------------------------------------------------------------------
        dragRule = DragRule.Only;
        dragOnly = practiceItem;

        yield return DoTracking("PACK IT",
            "Drag the <color=#FF8B43>" + practiceItemName + "</color> into the open pocket.",
            () => InBag(practiceItem),
            OnboardingOverlay.Block.None, OnboardingOverlay.CardPlace.Top,
            () => inventory.IsBagPocketOpen ? null : "Wait for the pocket to open.",
            () => ItemRect(practiceItem), () => inventory.OpenPocket);

        dragOnly = extraItem;

        yield return DoTracking("PACK ANOTHER",
            "Now drag the <color=#FF8B43>" + extraItemName + "</color> into your bag too.",
            () => InBag(extraItem),
            OnboardingOverlay.Block.None, OnboardingOverlay.CardPlace.Top,
            () => inventory.IsBagPocketOpen ? null : "Wait for the pocket to open.",
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
                : "Use the <color=#FF8B43>arrows</color> to go to the pocket the " + extraItemName + " is in.",
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
            "Find the pocket holding the <color=#FF8B43>" + practiceItemName + "</color> and drag it into the answer box.",
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
        if (replay)
        {
            yield return Info("TUTORIAL COMPLETE!",
                "Remember: in a real drill the <color=#FF4343>timer runs</color>, your bag and " +
                "starting spot are random, and your score counts: what you pack, your answers, " +
                "the minigames and the time you have left.\n\n" +
                (ReturnsToDrill ? "Your drill is waiting where you paused it. Good luck!" : "Good luck!"),
                ReturnsToDrill ? "BACK TO DRILL" : "MAIN MENU", null, OnboardingOverlay.CardPlace.Center);
        }
        else
        {
            yield return Info("TUTORIAL COMPLETE!",
                "You're ready for the real drill. This time the <color=#FF4343>timer runs</color>, your " +
                "bag and starting spot are random, and your score counts: what you pack, your answers, " +
                "the minigames and the time you have left.\n\nWant a refresher later? Replay this " +
                "tutorial from <color=#FF8B43>How to Play</color>. Good luck!",
                "START DRILL", null, OnboardingOverlay.CardPlace.Center);
        }

        if (step != TOTAL_STEPS)
            Debug.LogWarning($"Onboarding counted {step} steps but shows {TOTAL_STEPS} as the total.", this);

        CompletePractice();
    }

    /// <summary>
    /// SKIP TUTORIAL, offered on a replay only: the game holds still while it asks, then either
    /// carries on where it was or ends the replay as if it were finished.
    /// </summary>
    private void OnSkipRequested()
    {
        if (!replay || overlay.IsConfirmOpen)
            return;

        float timeScale = Time.timeScale;
        Time.timeScale = 0f;

        string body = (ReturnsToDrill ? "You'll go back to your drill, where you paused it." : "You'll go back to the main menu.") +
                      "\n\nYou can play the tutorial again any time from <color=#FF8B43>How to Play</color>.";

        overlay.ShowConfirm("SKIP TUTORIAL?", body, CompletePractice, () => Time.timeScale = timeScale);
    }

    /// <summary>A replay opened from a drill, which it hands back to when it ends.</summary>
    private bool ReturnsToDrill => replay && pendingResume != null;

    /// <summary>
    /// A first practice goes straight on into the real drill; a replay goes back to the drill
    /// it was opened from, or else to the main menu, so looking something up never commits the
    /// player to a new round.
    /// </summary>
    private void CompletePractice()
    {
        PlayerPrefs.SetInt(PlayerKey(), 1);
        PlayerPrefs.Save();

        Time.timeScale = 1f;

        if (!replay)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            return;
        }

        bool toDrill = ReturnsToDrill;
        EndReplay();

        // The next load takes the saved drill
        if (toDrill)
        {
            LoadingScreen.LoadScene(GAME_SCENE);
            return;
        }

        // As the pause menu's Exit does
        if (SoundManager.Instance != null)
            SoundManager.Instance.StopMusic();

        if (SceneNavigationManager.Instance != null)
            SceneNavigationManager.Instance.GoToMainScene();
        else
            MenuTransition.LoadScene("MainScene");
    }
}

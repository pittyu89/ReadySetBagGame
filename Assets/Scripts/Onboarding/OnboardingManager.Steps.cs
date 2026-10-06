using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The machinery the tutorial script runs on: showing a step and waiting for it, the conditions
/// that complete a step, the quiz events it listens for, setting up the scene, and finding the
/// things on screen a step points at.
/// </summary>
public partial class OnboardingManager
{
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
        return "TUTORIAL  " + step + " / " + TOTAL_STEPS;
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

    /// <summary>
    /// The item's tile, or while it is in a pocket that isn't open, the pocket arrows that get
    /// to it.
    /// </summary>
    private RectTransform ItemOrPocketArrows(InventoryItem item)
    {
        RectTransform tile = ItemRect(item);
        if (tile != null)
            return tile;

        return inventory != null && inventory.PouchNavigator != null ? inventory.PouchNavigator.Controls : null;
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

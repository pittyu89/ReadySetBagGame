using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// A drill in progress, saved so the player can carry on where they left off: same spot,
/// same camera angle, same time left, the go-bag where it was or in their hands, and every
/// item where they left it, in the bag and in the furniture. Once the quiz has begun it also
/// holds the quiz's progress (<see cref="QuizProgress"/>), and the drill comes back at the
/// question it had reached.
///
/// Used two ways. Offline, it is held in memory while the practice run is replayed from the
/// pause menu. In a teacher session, <see cref="SessionDrillStore"/> keeps it on the server and
/// the device, so a student who leaves (or whose game closes) carries on when they rejoin, less
/// the time they were away. It serializes to JSON for that; items are kept by name and rebuilt
/// from their SupplyItem data.
///
/// Not saved: doors left open, the cat, what panel was open. The drill comes back with
/// everything else as a fresh round would have it.
/// </summary>
[Serializable]
public class DrillSnapshot
{
    [Serializable]
    private class SavedItem
    {
        public string name;
        public int quantity = 1;
        public int x;
        public int y;
    }

    [Serializable]
    private class SavedGrid
    {
        // A bag section's name, or a piece of furniture's key and compartment ("key#index")
        public string key;
        public List<SavedItem> items = new List<SavedItem>();
    }

    [SerializeField] private Vector3 playerPosition;
    [SerializeField] private bool bagPickedUp;
    [SerializeField] private Vector3 bagRestPosition;

    [SerializeField] private float timeRemaining;
    [SerializeField] private bool timerRunning;
    [SerializeField] private long savedAtUtcTicks;

    [SerializeField] private bool hasCamera;
    [SerializeField] private float cameraYaw;
    [SerializeField] private float cameraPitch;

    [SerializeField] private List<SavedGrid> bag = new List<SavedGrid>();

    // Empty while the house was never filled: nothing had been opened, so it can be filled
    // afresh without the player seeing a change
    [SerializeField] private bool houseFilled;
    [SerializeField] private List<SavedGrid> furniture = new List<SavedGrid>();

    // Set once packing is over and the quiz has begun
    [SerializeField] private bool inQuiz;
    [SerializeField] private QuizProgress quiz = new QuizProgress();

    private static Dictionary<string, SupplyItem> supplies;

    /// <summary>The drill had reached its quiz; <see cref="Quiz"/> says where.</summary>
    public bool InQuiz => inQuiz;
    public QuizProgress Quiz => quiz;

    public Vector3 PlayerPosition => playerPosition;
    public bool BagPickedUp => bagPickedUp;
    public Vector3 BagRestPosition => bagRestPosition;

    /// <summary>Whether the round clock was counting when the drill was saved.</summary>
    public bool TimerWasRunning => timerRunning;

    /// <summary>
    /// Saves the drill in the open game scene, or returns null when there is nothing to come
    /// back to: no player, or the drill has reached its results.
    /// </summary>
    public static DrillSnapshot Capture()
    {
        PlayerController player = Object.FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (player == null)
            return null;

        DrillSnapshot snapshot = new DrillSnapshot
        {
            playerPosition = player.transform.position,
            savedAtUtcTicks = DateTime.UtcNow.Ticks
        };

        QuizManager quizManager = Object.FindFirstObjectByType<QuizManager>(FindObjectsInactive.Include);
        if (quizManager != null && quizManager.HasStarted)
        {
            QuizProgress progress = quizManager.CaptureProgress();
            if (progress == null)
                return null;

            snapshot.inQuiz = true;
            snapshot.quiz = progress;
        }

        GameTimer timer = Object.FindFirstObjectByType<GameTimer>(FindObjectsInactive.Include);
        PauseManager pause = Object.FindFirstObjectByType<PauseManager>(FindObjectsInactive.Include);
        if (timer != null)
        {
            snapshot.timeRemaining = timer.GetTimeRemaining();
            // The pause menu has stopped the clock; what counts is whether it was going before
            snapshot.timerRunning = timer.IsRunning || (pause != null && pause.IsPaused && pause.TimerWasRunning);
        }

        CameraOrbitController orbit = Object.FindFirstObjectByType<CameraOrbitController>();
        if (orbit != null)
        {
            snapshot.hasCamera = true;
            snapshot.cameraYaw = orbit.Yaw;
            snapshot.cameraPitch = orbit.Pitch;
        }

        snapshot.bagPickedUp = GoBagPickup.IsBagPickedUp();
        GoBagPickup goBag = Object.FindFirstObjectByType<GoBagPickup>(FindObjectsInactive.Include);
        if (goBag != null)
            snapshot.bagRestPosition = goBag.RestPosition;

        InventoryManager inventory = InventoryManager.Instance;
        if (inventory != null)
        {
            foreach (string section in inventory.GetAllSectionNames())
                snapshot.bag.Add(Read(section, inventory.GetGrid(section)));
        }

        foreach (StorageFurniture piece in AllFurniture())
        {
            if (!piece.IsFilled)
                continue;

            snapshot.houseFilled = true;
            string key = Key(piece);
            for (int i = 0; i < piece.CompartmentCount; i++)
                snapshot.furniture.Add(Read(key + "#" + i, piece.GetCompartment(i)));
        }

        return snapshot;
    }

    public string ToJson() => JsonUtility.ToJson(this);

    public static DrillSnapshot FromJson(string json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonUtility.FromJson<DrillSnapshot>(json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Couldn't read the saved drill: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Takes the time since the drill was saved off whichever clock was running: in a teacher
    /// session the drill does not wait for a student who left. While packing that is the
    /// round's clock; in the quiz, where the round's clock has stopped, it is the question's.
    /// Never quite to zero, so a clock that ran out while they were away still ends the round,
    /// or the question, the normal way as soon as it runs.
    /// </summary>
    public void TakeOffTimeAway()
    {
        TakeOffTimeAway((float)Math.Max(0.0, (DateTime.UtcNow - new DateTime(savedAtUtcTicks, DateTimeKind.Utc)).TotalSeconds));
    }

    /// <summary>
    /// The same, with the time away already measured - by the server's clock, which the device
    /// can't be set to fool. In the quiz it comes off the minigame that was cut off, if one
    /// was, and otherwise the question's.
    /// </summary>
    public void TakeOffTimeAway(float away)
    {
        away = Mathf.Max(0f, away);

        if (!inQuiz)
            timeRemaining = Mathf.Max(0.05f, timeRemaining - away);
        else if (quiz.pendingMinigame >= 0)
            quiz.minigameTimeLeft = Mathf.Max(0.05f, quiz.minigameTimeLeft - away);
        else
            quiz.questionTimeLeft = Mathf.Max(0.05f, quiz.questionTimeLeft - away);
    }

    /// <summary>
    /// Puts back what does not depend on the scene having settled: the items, the bag in the
    /// player's hands, the camera and the clock. The positions were placed by
    /// HouseSpawnRandomizer already. Run once every Start in the scene has, so nothing refills
    /// or resets it after.
    /// </summary>
    public void RestoreScene()
    {
        InventoryManager inventory = InventoryManager.Instance;
        if (inventory != null)
        {
            foreach (SavedGrid section in bag)
                Write(inventory.GetGrid(section.key), section);
            inventory.InvokeInventoryChanged();
        }

        if (houseFilled)
            RestoreFurniture();

        if (bagPickedUp)
        {
            GoBagPickup goBag = Object.FindFirstObjectByType<GoBagPickup>(FindObjectsInactive.Include);
            if (goBag != null)
                goBag.RestorePickedUp(Object.FindFirstObjectByType<PlayerController>());
        }

        CameraOrbitController orbit = Object.FindFirstObjectByType<CameraOrbitController>();
        if (orbit != null && hasCamera)
            orbit.SetAngles(cameraYaw, cameraPitch);

        GameTimer timer = Object.FindFirstObjectByType<GameTimer>();
        if (timer != null)
            timer.RestoreTimeRemaining(timeRemaining);
    }

    private void RestoreFurniture()
    {
        StorageFurniture[] all = AllFurniture();

        Dictionary<string, SavedGrid> saved = new Dictionary<string, SavedGrid>();
        foreach (SavedGrid grid in furniture)
            saved[grid.key] = grid;

        // Marked filled first, so reading a compartment below never sets off a fresh fill
        foreach (StorageFurniture piece in all)
            piece.IsFilled = true;

        foreach (StorageFurniture piece in all)
        {
            string key = Key(piece);
            for (int i = 0; i < piece.CompartmentCount; i++)
            {
                InventoryGrid grid = piece.GetCompartment(i);
                if (grid == null)
                    continue;

                grid.Clear();
                SavedGrid contents;
                if (saved.TryGetValue(key + "#" + i, out contents))
                    Write(grid, contents);
            }
        }
    }

    private static SavedGrid Read(string key, InventoryGrid grid)
    {
        SavedGrid saved = new SavedGrid { key = key };
        if (grid == null)
            return saved;

        foreach (var (item, x, y) in grid.GetPlacements())
            saved.items.Add(new SavedItem { name = item.itemName, quantity = item.quantity, x = x, y = y });
        return saved;
    }

    private static void Write(InventoryGrid grid, SavedGrid saved)
    {
        if (grid == null)
            return;

        grid.Clear();
        foreach (SavedItem entry in saved.items)
        {
            SupplyItem supply = FindSupply(entry.name);
            if (supply == null)
            {
                Debug.LogWarning($"Resuming the drill: no item data called {entry.name}, so it is left out.");
                continue;
            }

            InventoryItem item = InventoryItem.FromSupply(supply);
            item.quantity = Mathf.Max(1, entry.quantity);
            if (!grid.PlaceItem(entry.x, entry.y, item))
                Debug.LogWarning($"Resuming the drill: {entry.name} no longer fits where it was.");
        }
    }

    private static SupplyItem FindSupply(string itemName)
    {
        if (supplies == null)
        {
            supplies = new Dictionary<string, SupplyItem>(StringComparer.OrdinalIgnoreCase);
            foreach (SupplyItem supply in Resources.LoadAll<SupplyItem>("ItemData"))
            {
                if (supply != null && !string.IsNullOrEmpty(supply.ItemName))
                    supplies[supply.ItemName] = supply;
            }
        }

        SupplyItem found;
        return itemName != null && supplies.TryGetValue(itemName, out found) ? found : null;
    }

    private static StorageFurniture[] AllFurniture()
    {
        return Object.FindObjectsByType<StorageFurniture>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    /// <summary>
    /// Names the same piece of furniture in every load of the scene: its root's name, then its
    /// place among its siblings at every level below. The model's own names repeat ("Drawer
    /// B.003" and friends), and sibling order inside the house never changes at runtime.
    /// </summary>
    private static string Key(StorageFurniture piece)
    {
        StringBuilder key = new StringBuilder();
        Transform t = piece.transform;
        while (t.parent != null)
        {
            key.Insert(0, "/" + t.GetSiblingIndex());
            t = t.parent;
        }
        key.Insert(0, t.name);
        return key.ToString();
    }
}

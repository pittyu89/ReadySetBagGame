using UnityEngine;
using TMPro;

public class GameDifficultyApplier : MonoBehaviour
{
    // Weight limits for each difficulty
    // The same 5 kg at every difficulty. The bag is not where the difficulty lives — the
    // clock, the number of scenarios and how much of the house is open are. A tighter limit
    // only punished students for packing the very items the quiz had just taught them.
    private static readonly float[] WEIGHT_LIMITS = { 5f, 5f, 5f }; // beginner, intermediate, advanced
    private static readonly string[] DIFFICULTY_NAMES = { "beginner", "intermediate", "advanced" };

    // The house is the model straight from its FBX, so the garage and the second floor are
    // lists of that model's pieces rather than a single group object each.
    [SerializeField] private GameObject[] garageParts = new GameObject[0];
    [SerializeField] private GameObject[] secondFloorParts = new GameObject[0];
    [SerializeField] private GameObject stairs;

    private string currentDifficulty = "beginner";
    private float weightLimit = 5f;
    private float timeLimit = 600f;

    private void Start()
    {
        // Get difficulty from session
        currentDifficulty = PlayerPrefs.GetString("SessionDifficulty", "beginner").ToLower();
        string sessionCode = PlayerPrefs.GetString("SessionCode", "");

        // Apply difficulty settings
        ApplyDifficulty(currentDifficulty);
    }

    private void ApplyDifficulty(string difficulty)
    {
        // WEIGHT_LIMITS is the one place the limit is set, so the editor's essential-weight
        // check and the game can never disagree about it
        weightLimit = GetWeightLimitForDifficulty(difficulty);

        switch (difficulty.ToLower())
        {
            // The weight limit is the same throughout; only the clock tightens.
            case "beginner":
                timeLimit = 600f; // 10 minutes
                break;
            case "intermediate":
                timeLimit = 480f; // 8 minutes
                break;
            case "advanced":
                timeLimit = 360f; // 6 minutes
                break;
            default:
                timeLimit = 600f;
                break;
        }

        // Apply floor visibility based on difficulty
        ApplyFloorVisibility(difficulty.ToLower());

        // Save time limit to PlayerPrefs for Timer.cs to read
        PlayerPrefs.SetInt("GameTime", (int)timeLimit);
        PlayerPrefs.Save();

        // Apply to game systems
        ApplyToGameSystems();
    }

    /// <summary>
    /// Whether the part of the house <paramref name="target"/> is in can be reached on this
    /// session's difficulty: the garage opens on Intermediate, upstairs on Advanced. Asked by
    /// difficulty rather than by what is currently visible, because Advanced keeps the second
    /// floor hidden until the player climbs the stairs.
    /// </summary>
    public bool IsAreaOpen(Transform target)
    {
        string difficulty = PlayerPrefs.GetString("SessionDifficulty", "beginner").ToLowerInvariant();
        bool inGarage = IsInside(target, garageParts);
        bool upstairs = IsInside(target, secondFloorParts);

        switch (difficulty)
        {
            case "advanced":
                return true;
            case "intermediate":
                return !upstairs;
            default:
                return !inGarage && !upstairs;
        }
    }

    private static bool IsInside(Transform target, GameObject[] parts)
    {
        foreach (GameObject part in parts)
        {
            if (part != null && target.IsChildOf(part.transform))
                return true;
        }
        return false;
    }

    private static void SetActive(GameObject[] parts, bool active)
    {
        foreach (GameObject part in parts)
        {
            if (part != null)
                part.SetActive(active);
        }
    }

    private void ApplyFloorVisibility(string difficulty)
    {
        switch (difficulty)
        {
            case "beginner":
                SetActive(garageParts, false);
                SetActive(secondFloorParts, false);
                if (stairs != null)
                    stairs.SetActive(false);
                break;
            case "intermediate":
                SetActive(garageParts, true);
                SetActive(secondFloorParts, false);
                if (stairs != null)
                    stairs.SetActive(false);
                break;
            case "advanced":
                SetActive(garageParts, true);
                SetActive(secondFloorParts, false); // Initially hidden, FloorVisibilityManager will show it
                if (stairs != null)
                    stairs.SetActive(true);
                break;
        }
    }

    private void ApplyToGameSystems()
    {
        // Apply time limit directly to Timer
        GameTimer timer = FindFirstObjectByType<GameTimer>();
        if (timer != null)
        {
            timer.SetTimeLimit(timeLimit);
        }

        // Apply weight limit to InventoryManager as global GoBag limit
        InventoryManager inventoryManager = InventoryManager.Instance;
        if (inventoryManager != null)
        {
            inventoryManager.SetGoBagWeightLimit(weightLimit);

        }
    }

    public float GetWeightLimit()
    {
        return weightLimit;
    }

    public float GetTimeLimit()
    {
        return timeLimit;
    }

    public string GetDifficulty()
    {
        return currentDifficulty;
    }

    public static float GetWeightLimitForDifficulty(string difficulty)
    {
        for (int i = 0; i < DIFFICULTY_NAMES.Length; i++)
        {
            if (DIFFICULTY_NAMES[i] == difficulty.ToLower())
            {
                return WEIGHT_LIMITS[i];
            }
        }
        return WEIGHT_LIMITS[0]; // Default to beginner
    }

    /// <summary>The tightest weight limit across every difficulty.</summary>
    public static float SmallestWeightLimit()
    {
        float smallest = float.MaxValue;
        foreach (float limit in WEIGHT_LIMITS)
            smallest = Mathf.Min(smallest, limit);
        return smallest;
    }
}

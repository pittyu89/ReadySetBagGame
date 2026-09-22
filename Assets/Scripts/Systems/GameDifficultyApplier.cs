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

    [SerializeField] private GameObject garage;
    [SerializeField] private GameObject secondFloor;
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
        switch (difficulty.ToLower())
        {
            // The weight limit is the same throughout; only the clock tightens.
            case "beginner":
                weightLimit = 5f;
                timeLimit = 600f; // 10 minutes
                break;
            case "intermediate":
                weightLimit = 5f;
                timeLimit = 480f; // 8 minutes
                break;
            case "advanced":
                weightLimit = 5f;
                timeLimit = 360f; // 6 minutes
                break;
            default:
                weightLimit = 5f;
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
        bool inGarage = garage != null && target.IsChildOf(garage.transform);
        bool upstairs = secondFloor != null && target.IsChildOf(secondFloor.transform);

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

    private void ApplyFloorVisibility(string difficulty)
    {
        switch (difficulty)
        {
            case "beginner":
                if (garage != null)
                    garage.SetActive(false);
                if (secondFloor != null)
                    secondFloor.SetActive(false);
                if (stairs != null)
                    stairs.SetActive(false);
                break;
            case "intermediate":
                if (garage != null)
                    garage.SetActive(true);
                if (secondFloor != null)
                    secondFloor.SetActive(false);
                if (stairs != null)
                    stairs.SetActive(false);
                break;
            case "advanced":
                if (garage != null)
                    garage.SetActive(true);
                if (secondFloor != null)
                    secondFloor.SetActive(false); // Initially hidden, FloorVisibilityManager will show it
                if (stairs != null)
                    stairs.SetActive(true);
                break;
        }
    }

    private void ApplyToGameSystems()
    {
        // Apply time limit directly to Timer
        GameTimer timer = FindObjectOfType<GameTimer>();
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
        return 5f; // Default to beginner
    }
}

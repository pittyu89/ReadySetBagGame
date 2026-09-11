using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;

public class DifficultyPanelManager : MonoBehaviour
{
    [SerializeField] private Button leftArrowButton;
    [SerializeField] private Button rightArrowButton;
    [SerializeField] private TextMeshProUGUI difficultyTitle;
    [SerializeField] private TextMeshProUGUI difficultyDescription;
    [SerializeField] private RenderTexture difficultyRT;

    private int currentDifficultyIndex = 0;

    private readonly string[] difficultyNames = { "Beginner", "Intermediate", "Advanced" };
    private readonly string[] difficultyKeys = { "beginner", "intermediate", "advanced" };
    private readonly string[] difficultyDescriptions =
    {
        "5 KG LIMIT 10 MINUTES",
        "4.5 KG LIMIT 8 MINUTES\nmore non-essential\nnew area unlocked!!",
        "4 KG LIMIT6 MINUTES\nmore non-essential\nitems are random\nnew area unlocked!!"
    };

    private readonly string[] videoKeys =
    {
        VideoManager.BEGINNER_PREVIEW,
        VideoManager.INTERMEDIATE_PREVIEW,
        VideoManager.ADVANCED_PREVIEW
    };

    void Start()
    {
        if (leftArrowButton != null)
            leftArrowButton.onClick.AddListener(PreviousDifficulty);

        if (rightArrowButton != null)
            rightArrowButton.onClick.AddListener(NextDifficulty);
    }

    void OnEnable()
    {
        currentDifficultyIndex = 0;
        UpdateDifficultyDisplay();
    }

    void OnDisable()
    {
        // Stop whichever preview is playing when the panel closes
        if (VideoManager.Instance != null)
            VideoManager.Instance.StopVideo(videoKeys[currentDifficultyIndex]);
    }

    private void NextDifficulty()
    {
        currentDifficultyIndex = (currentDifficultyIndex + 1) % difficultyNames.Length;
        UpdateDifficultyDisplay();
    }

    private void PreviousDifficulty()
    {
        currentDifficultyIndex = (currentDifficultyIndex - 1 + difficultyNames.Length) % difficultyNames.Length;
        UpdateDifficultyDisplay();
    }

    private void UpdateDifficultyDisplay()
    {
        if (difficultyTitle != null)
            difficultyTitle.text = difficultyNames[currentDifficultyIndex];

        if (difficultyDescription != null)
            difficultyDescription.text = difficultyDescriptions[currentDifficultyIndex];

        // Play the preview video through VideoManager
        if (VideoManager.Instance != null)
        {
            // Stop all previews first
            for (int i = 0; i < videoKeys.Length; i++)
                VideoManager.Instance.StopVideo(videoKeys[i]);

            // Point the selected video at this panel's RenderTexture and play
            var vp = VideoManager.Instance.GetPlayer(videoKeys[currentDifficultyIndex]);
            if (vp != null)
            {
                if (difficultyRT != null)
                    vp.targetTexture = difficultyRT;
                vp.Play();
            }
        }

        PlayerPrefs.SetString("SessionDifficulty", difficultyKeys[currentDifficultyIndex]);
        PlayerPrefs.Save();
    }
}

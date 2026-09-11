using UnityEngine;
using UnityEngine.UI;

public class SwitchPanelManager : MonoBehaviour
{
    [Header("Character Buttons")]
    [SerializeField] private Button femaleButton;
    [SerializeField] private Button maleButton;

    [Header("Character Images")]
    [SerializeField] private Image femaleImage;
    [SerializeField] private Image maleImage;
    [SerializeField] private Image femaleSprite;
    [SerializeField] private Image maleSprite;

    [Header("Character Animators")]
    [SerializeField] private Animator femaleAnimator;
    [SerializeField] private Animator maleAnimator;

    [Header("Confirm Button")]
    [SerializeField] private Button confirmButton;

    [Header("Main Scene Avatar")]
    [SerializeField] private GameObject switchPanel;
    [SerializeField] private UIManager uiManager;

    private enum SelectedCharacter { None, Female, Male }
    private SelectedCharacter currentSelection = SelectedCharacter.None;
    private SelectedCharacter previousSelection = SelectedCharacter.None;

    private const string SELECTED_CHARACTER_SUFFIX = "_SelectedCharacter";

    private const float SELECTED_ALPHA = 1f;  // 255
    private const float UNSELECTED_ALPHA = 128f / 255f;  // 128
    private const string ANIMATION_NAME = "WalkDown";
    private const string IS_WALKING_PARAM = "isWalking";

    void Start()
    {
        // Setup button listeners
        if (femaleButton != null)
            femaleButton.onClick.AddListener(SelectFemale);

        if (maleButton != null)
            maleButton.onClick.AddListener(SelectMale);

        if (confirmButton != null)
            confirmButton.onClick.AddListener(ConfirmSelection);
    }

    void OnEnable()
    {
        // Reset both animators to Idle first by disabling walking
        if (femaleAnimator != null)
            femaleAnimator.SetBool(IS_WALKING_PARAM, false);
        if (maleAnimator != null)
            maleAnimator.SetBool(IS_WALKING_PARAM, false);

        // Load the last selected character for the current user
        string userKey = GetUserKey();
        string lastSelected = PlayerPrefs.GetString(userKey, "Female");
        currentSelection = lastSelected == "Male" ? SelectedCharacter.Male : SelectedCharacter.Female;
        previousSelection = SelectedCharacter.None;
        
        UpdateCharacterDisplay();
    }

    private void SelectFemale()
    {
        currentSelection = SelectedCharacter.Female;
        UpdateCharacterDisplay();
    }

    private void SelectMale()
    {
        currentSelection = SelectedCharacter.Male;
        UpdateCharacterDisplay();
    }

    private void UpdateCharacterDisplay()
    {
        // Disable walking on previous character
        if (previousSelection == SelectedCharacter.Female && femaleAnimator != null)
        {
            femaleAnimator.SetBool(IS_WALKING_PARAM, false);
        }
        else if (previousSelection == SelectedCharacter.Male && maleAnimator != null)
        {
            maleAnimator.SetBool(IS_WALKING_PARAM, false);
        }

        // Update image alphas
        SetImageAlpha(femaleImage, currentSelection == SelectedCharacter.Female ? SELECTED_ALPHA : UNSELECTED_ALPHA);
        SetImageAlpha(maleImage, currentSelection == SelectedCharacter.Male ? SELECTED_ALPHA : UNSELECTED_ALPHA);
        SetImageAlpha(femaleSprite, currentSelection == SelectedCharacter.Female ? SELECTED_ALPHA : UNSELECTED_ALPHA);
        SetImageAlpha(maleSprite, currentSelection == SelectedCharacter.Male ? SELECTED_ALPHA : UNSELECTED_ALPHA);

        // Enable walking on selected character
        if (currentSelection == SelectedCharacter.Female && femaleAnimator != null)
        {
            femaleAnimator.SetBool(IS_WALKING_PARAM, true);
        }
        else if (currentSelection == SelectedCharacter.Male && maleAnimator != null)
        {
            maleAnimator.SetBool(IS_WALKING_PARAM, true);
        }

        previousSelection = currentSelection;
    }

    private void SetImageAlpha(Image image, float alpha)
    {
        if (image != null)
        {
            Color color = image.color;
            color.a = alpha;
            image.color = color;
        }
    }

    private void ConfirmSelection()
    {
        if (currentSelection == SelectedCharacter.None)
        {
            return;
        }

        // Save the selected character for the current user
        string selectedCharacter = currentSelection == SelectedCharacter.Female ? "Female" : "Male";
        string userKey = GetUserKey();
        PlayerPrefs.SetString(userKey, selectedCharacter);
        PlayerPrefs.Save();

        // Close the switch panel first
        if (switchPanel != null)
        {
            switchPanel.SetActive(false);
        }

        if (uiManager != null)
        {
            uiManager.ResetLeftMenuPanel();
            uiManager.RefreshAvatar();
        }
    }

    private string GetUserKey()
    {
        bool isGuest = PlayerPrefs.GetString("IsGuest", "false") == "true";
        string userName = isGuest ? "Guest" : PlayerPrefs.GetString("StudentName", "User");
        return userName + SELECTED_CHARACTER_SUFFIX;
    }
}

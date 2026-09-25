using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using TMPro;
using System.Collections;

public class LoginModeToggle : MonoBehaviour
{
    [SerializeField] private Slider modeToggleSlider;
    [SerializeField] private GameObject loginPanel;
    [SerializeField] private GameObject guestPanel;
    [SerializeField, FormerlySerializedAs("rightPanel")] private GameObject iconPanel;
    [SerializeField] private GameObject loginIcon;
    [SerializeField] private GameObject guestIcon;
    [SerializeField] private Toggle termsAndConditionsCheckbox;
    [SerializeField] private Button guestLoginButton;
    [SerializeField] private TextMeshProUGUI errorText;
    [SerializeField] private float fadeDuration = 0.15f;

    private CanvasGroup loginCanvasGroup;
    private CanvasGroup guestCanvasGroup;
    private CanvasGroup loginIconCanvasGroup;
    private CanvasGroup guestIconCanvasGroup;
    private Image iconPanelImage;
    private Image sliderHandleImage;
    private Color originalSliderHandleColor;
    private Color loginPanelBackgroundColor = new Color(0.3608f, 0.5569f, 0.9098f, 1f); // #5C8DE8
    private Color guestPanelBackgroundColor = new Color(0.3529f, 0.3529f, 0.3529f, 1f); // #5A5A5A
    private Color guestSliderHandleColor = new Color(0.3529f, 0.3529f, 0.3529f, 1f); // #5A5A5A
    private bool isLogin = true;
    private bool isTransitioning = false;

    private void Start()
    {
        // Setup CanvasGroups for panels
        loginCanvasGroup = loginPanel.GetComponent<CanvasGroup>();
        if (loginCanvasGroup == null)
            loginCanvasGroup = loginPanel.AddComponent<CanvasGroup>();

        guestCanvasGroup = guestPanel.GetComponent<CanvasGroup>();
        if (guestCanvasGroup == null)
            guestCanvasGroup = guestPanel.AddComponent<CanvasGroup>();

        // Setup CanvasGroups for icons
        loginIconCanvasGroup = loginIcon.GetComponent<CanvasGroup>();
        if (loginIconCanvasGroup == null)
            loginIconCanvasGroup = loginIcon.AddComponent<CanvasGroup>();

        guestIconCanvasGroup = guestIcon.GetComponent<CanvasGroup>();
        if (guestIconCanvasGroup == null)
            guestIconCanvasGroup = guestIcon.AddComponent<CanvasGroup>();

        // Setup right panel background color
        iconPanelImage = iconPanel.GetComponent<Image>();

        // Setup slider handle color
        sliderHandleImage = modeToggleSlider.targetGraphic as Image;
        if (sliderHandleImage != null)
            originalSliderHandleColor = sliderHandleImage.color;

        // Set initial state (Login mode)
        loginPanel.SetActive(true);
        guestPanel.SetActive(false);
        loginCanvasGroup.alpha = 1f;
        guestCanvasGroup.alpha = 0f;

        loginIcon.SetActive(true);
        guestIcon.SetActive(false);
        loginIconCanvasGroup.alpha = 1f;
        guestIconCanvasGroup.alpha = 0f;

        if (iconPanelImage != null)
            iconPanelImage.color = loginPanelBackgroundColor;

        modeToggleSlider.value = 0;
        isLogin = true;

        // Setup slider for click-only toggle
        modeToggleSlider.onValueChanged.AddListener(OnSliderValueChanged);

        // Setup slider for click-only toggle
        SetupSliderClickToggle();

        // Keep slider interactable for visual appearance

        // Setup buttons
        if (guestLoginButton != null)
            guestLoginButton.onClick.AddListener(OnGuestLoginClicked);
    }

    private void OnSliderValueChanged(float value)
    {
        // Ignore changes during transition
        if (isTransitioning)
            return;

        // Detect click and start smooth transition
        if (value < 0.5f && !isLogin)
        {
            StartCoroutine(SmoothSliderTransition(1f, 0f));
            StartCoroutine(TransitionToLogin());
            isLogin = true;
        }
        else if (value >= 0.5f && isLogin)
        {
            StartCoroutine(SmoothSliderTransition(0f, 1f));
            StartCoroutine(TransitionToGuest());
            isLogin = false;
        }
    }

    private void SetupSliderClickToggle()
    {
        // Get or create EventTrigger on the slider itself
        EventTrigger eventTrigger = modeToggleSlider.GetComponent<EventTrigger>();
        if (eventTrigger == null)
            eventTrigger = modeToggleSlider.gameObject.AddComponent<EventTrigger>();

        // Clear existing triggers to avoid duplicates
        eventTrigger.triggers.Clear();

        // Create and add PointerClick event entry
        EventTrigger.Entry pointerClickEntry = new EventTrigger.Entry();
        pointerClickEntry.eventID = EventTriggerType.PointerClick;
        pointerClickEntry.callback.AddListener((data) => OnSliderClicked());
        eventTrigger.triggers.Add(pointerClickEntry);
    }

    private void OnSliderClicked()
    {
        if (isTransitioning)
            return;

        // Toggle the slider
        if (isLogin)
        {
            StartCoroutine(SmoothSliderTransition(0f, 1f));
            StartCoroutine(TransitionToGuest());
            isLogin = false;
        }
        else
        {
            StartCoroutine(SmoothSliderTransition(1f, 0f));
            StartCoroutine(TransitionToLogin());
            isLogin = true;
        }
    }

    private IEnumerator SmoothSliderTransition(float fromValue, float toValue)
    {
        isTransitioning = true;
        float elapsed = 0f;
        
        // Temporarily disable whole numbers for smooth animation
        modeToggleSlider.wholeNumbers = false;
        
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            modeToggleSlider.value = Mathf.Lerp(fromValue, toValue, elapsed / fadeDuration);
            yield return null;
        }
        
        // Snap to final value and re-enable whole numbers
        modeToggleSlider.value = toValue;
        modeToggleSlider.wholeNumbers = true;
        isTransitioning = false;
    }

    private IEnumerator TransitionToLogin()
    {
        yield return StartCoroutine(FadeOut(guestCanvasGroup));
        guestPanel.SetActive(false);
        loginPanel.SetActive(true);

        // Run all transitions in parallel
        StartCoroutine(SwitchIcon(guestIconCanvasGroup, loginIconCanvasGroup, guestIcon, loginIcon));
        StartCoroutine(TransitionPanelColor(guestPanelBackgroundColor, loginPanelBackgroundColor));
        StartCoroutine(TransitionSliderHandleColor(guestSliderHandleColor, originalSliderHandleColor));
        
        yield return StartCoroutine(FadeIn(loginCanvasGroup));
    }

    private IEnumerator TransitionToGuest()
    {
        yield return StartCoroutine(FadeOut(loginCanvasGroup));
        loginPanel.SetActive(false);
        guestPanel.SetActive(true);

        // Run all transitions in parallel
        StartCoroutine(SwitchIcon(loginIconCanvasGroup, guestIconCanvasGroup, loginIcon, guestIcon));
        StartCoroutine(TransitionPanelColor(loginPanelBackgroundColor, guestPanelBackgroundColor));
        StartCoroutine(TransitionSliderHandleColor(originalSliderHandleColor, guestSliderHandleColor));
        
        yield return StartCoroutine(FadeIn(guestCanvasGroup));
    }

    private IEnumerator FadeOut(CanvasGroup canvasGroup)
    {
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float alphaValue = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);
            canvasGroup.alpha = alphaValue;
            yield return null;
        }
        canvasGroup.alpha = 0f;
    }

    private IEnumerator FadeIn(CanvasGroup canvasGroup)
    {
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float alphaValue = Mathf.Lerp(0f, 1f, elapsed / fadeDuration);
            canvasGroup.alpha = alphaValue;
            yield return null;
        }
        canvasGroup.alpha = 1f;
    }

    private IEnumerator TransitionPanelColor(Color fromColor, Color toColor)
    {
        if (iconPanelImage == null)
            yield break;

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            iconPanelImage.color = Color.Lerp(fromColor, toColor, elapsed / fadeDuration);
            yield return null;
        }
        iconPanelImage.color = toColor;
    }

    private IEnumerator SwitchIcon(CanvasGroup fadeOutIcon, CanvasGroup fadeInIcon, GameObject fadeOutIconGO, GameObject fadeInIconGO)
    {
        float halfDuration = fadeDuration / 2f;

        // Fade out current icon
        float elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            fadeOutIcon.alpha = Mathf.Lerp(1f, 0f, elapsed / halfDuration);
            yield return null;
        }
        fadeOutIcon.alpha = 0f;
        fadeOutIconGO.SetActive(false);

        // Switch to new icon
        fadeInIconGO.SetActive(true);

        // Fade in new icon
        elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            fadeInIcon.alpha = Mathf.Lerp(0f, 1f, elapsed / halfDuration);
            yield return null;
        }
        fadeInIcon.alpha = 1f;
    }

    private IEnumerator TransitionSliderHandleColor(Color fromColor, Color toColor)
    {
        if (sliderHandleImage == null)
            yield break;

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            sliderHandleImage.color = Color.Lerp(fromColor, toColor, elapsed / fadeDuration);
            yield return null;
        }
        sliderHandleImage.color = toColor;
    }

    private void OnGuestLoginClicked()
    {
        if (!termsAndConditionsCheckbox.isOn)
        {
            if (errorText != null)
                errorText.text = "Please accept the terms and conditions first.";
            return;
        }

        // Set guest mode indicator in PlayerPrefs
        PlayerPrefs.SetString("IsGuest", "true");
        PlayerPrefs.DeleteKey("StudentId");
        PlayerPrefs.DeleteKey("StudentUsername");
        PlayerPrefs.DeleteKey("SessionCode");
        PlayerPrefs.Save();

        // Transition to MainScene
        SceneNavigationManager.Instance.GoToMainScene();
    }
}

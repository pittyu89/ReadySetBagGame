using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Terms and Conditions window on the login screen. It is built like the Options and About
/// pop-ups and animated by the same <see cref="PopupPanelTransition"/>, only it rises from the
/// bottom of the screen instead of sliding in from the left.
/// It stays hidden until the player taps the login screen's terms circle. That tap does not tick
/// the circle - it opens this window instead. The player scrolls the terms, ticks "I Agree" -
/// which is what turns the Confirm button on - and confirms; only then is the login circle ticked
/// and the window slides back down. The close button backs out without ticking anything.
/// The choice is remembered: the circle comes back ticked next time, and tapping a ticked circle
/// unticks it and forgets the acceptance.
/// </summary>
public class TermsAndConditionsPanel : MonoBehaviour
{
    /// <summary>PlayerPrefs flag set once the player has confirmed the terms.</summary>
    public const string AcceptedKey = "TermsAccepted";

    [Tooltip("Root of the pop-up. Left off, this object is the pop-up.")]
    [SerializeField] private GameObject panel;
    [Tooltip("The \"I Agree\" checkbox inside the window.")]
    [SerializeField] private Toggle agreeToggle;
    [SerializeField] private Button confirmButton;
    [Tooltip("Closes the window without accepting.")]
    [SerializeField] private Button closeButton;
    [Tooltip("The login screen's own terms circle. Tapping it opens this window; it is ticked once the player confirms.")]
    [SerializeField] private Toggle loginTermsToggle;
    [Tooltip("Scroll the terms back to the top every time the window opens.")]
    [SerializeField] private ScrollRect termsScroll;

    /// <summary>Whether the player has already accepted the terms on this device.</summary>
    public static bool HasAccepted => PlayerPrefs.GetInt(AcceptedKey, 0) == 1;

    private GameObject Panel => panel != null ? panel : gameObject;

    void Awake()
    {
        if (agreeToggle != null)
            agreeToggle.onValueChanged.AddListener(OnAgreeChanged);

        if (confirmButton != null)
            confirmButton.onClick.AddListener(Accept);

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        if (loginTermsToggle != null)
        {
            loginTermsToggle.SetIsOnWithoutNotify(HasAccepted);
            loginTermsToggle.onValueChanged.AddListener(OnLoginToggleChanged);
        }

        // Hidden until the player asks for it. The listeners above live on this object, so it has
        // to start enabled in the scene and switch itself off here.
        Panel.SetActive(false);
    }

    void OnDestroy()
    {
        if (loginTermsToggle != null)
            loginTermsToggle.onValueChanged.RemoveListener(OnLoginToggleChanged);
    }

    /// <summary>Brings the window up from the bottom with the terms reset to the top.</summary>
    public void Open()
    {
        if (agreeToggle != null)
            agreeToggle.isOn = false;

        RefreshConfirm();
        PopupPanelTransition.Show(Panel);

        // Only after the layout has settled, or the scroll view snaps back a frame later
        if (termsScroll != null)
        {
            Canvas.ForceUpdateCanvases();
            termsScroll.verticalNormalizedPosition = 1f;
        }
    }

    /// <summary>Remembers the acceptance, ticks the login circle and closes the window.</summary>
    public void Accept()
    {
        if (agreeToggle != null && !agreeToggle.isOn)
            return;

        PlayerPrefs.SetInt(AcceptedKey, 1);
        PlayerPrefs.Save();

        if (loginTermsToggle != null)
            loginTermsToggle.SetIsOnWithoutNotify(true);

        PopupPanelTransition.Hide(Panel);
    }

    /// <summary>Closes the window without accepting; the login circle stays as it was.</summary>
    public void Close() => PopupPanelTransition.Hide(Panel);

    // A tap on the empty login circle opens the terms instead of ticking it; a tap on a ticked
    // one unticks it and forgets the acceptance
    private void OnLoginToggleChanged(bool isOn)
    {
        if (!isOn)
        {
            PlayerPrefs.SetInt(AcceptedKey, 0);
            PlayerPrefs.Save();
            return;
        }

        loginTermsToggle.SetIsOnWithoutNotify(false);
        Open();
    }

    private void OnAgreeChanged(bool _) => RefreshConfirm();

    // Confirm stays dead until the box is ticked, so agreeing is always a deliberate tap
    private void RefreshConfirm()
    {
        if (confirmButton != null)
            confirmButton.interactable = agreeToggle == null || agreeToggle.isOn;
    }
}

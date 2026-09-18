using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AudioSettingsPanel : MonoBehaviour
{
    [Header("Volume Sliders")]
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private Slider sfxVolumeSlider;

    [Header("Master / Mute (Optional)")]
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private Toggle muteToggle;

    // This UI is built with TextMeshPro, so these are TMP rather than legacy Text.
    [Header("Volume Labels (Optional)")]
    [SerializeField] private TextMeshProUGUI musicVolumeLabel;
    [SerializeField] private TextMeshProUGUI sfxVolumeLabel;
    [SerializeField] private TextMeshProUGUI masterVolumeLabel;

    private void OnEnable()
    {
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.RegisterVolumeSliders(
                musicVolumeSlider, sfxVolumeSlider, masterVolumeSlider, muteToggle);
        }

        // The labels have to follow the slider as it is dragged, not just show
        // whatever the value happened to be when the panel opened. These are
        // named methods rather than lambdas so OnDisable can remove exactly
        // these listeners without disturbing SoundManager's own callbacks.
        if (musicVolumeSlider != null)
            musicVolumeSlider.onValueChanged.AddListener(OnMusicSliderChanged);
        if (sfxVolumeSlider != null)
            sfxVolumeSlider.onValueChanged.AddListener(OnSFXSliderChanged);
        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.AddListener(OnMasterSliderChanged);

        UpdateVolumeLabels();
    }

    private void OnDisable()
    {
        if (musicVolumeSlider != null)
            musicVolumeSlider.onValueChanged.RemoveListener(OnMusicSliderChanged);
        if (sfxVolumeSlider != null)
            sfxVolumeSlider.onValueChanged.RemoveListener(OnSFXSliderChanged);
        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.RemoveListener(OnMasterSliderChanged);

        if (SoundManager.Instance != null)
        {
            // The volume setters no longer touch PlayerPrefs on every drag
            // frame, so this is where the player's choice actually gets written.
            SoundManager.Instance.SaveVolumeSettings();
        }
    }

    private void OnMusicSliderChanged(float value)
    {
        SetLabel(musicVolumeLabel, value);
    }

    private void OnSFXSliderChanged(float value)
    {
        SetLabel(sfxVolumeLabel, value);
    }

    private void OnMasterSliderChanged(float value)
    {
        SetLabel(masterVolumeLabel, value);
    }

    private void UpdateVolumeLabels()
    {
        if (musicVolumeSlider != null)
            SetLabel(musicVolumeLabel, musicVolumeSlider.value);

        if (sfxVolumeSlider != null)
            SetLabel(sfxVolumeLabel, sfxVolumeSlider.value);

        if (masterVolumeSlider != null)
            SetLabel(masterVolumeLabel, masterVolumeSlider.value);
    }

    private void SetLabel(TextMeshProUGUI label, float value)
    {
        if (label == null)
            return;

        label.text = $"{Mathf.RoundToInt(value * 100f)}%";
    }
}

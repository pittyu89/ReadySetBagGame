using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameTimer : MonoBehaviour
{
    public delegate void OnTimeUpDelegate();
    public static event OnTimeUpDelegate OnTimeUp;

    [SerializeField] private TextMeshProUGUI timerDisplay;
    [SerializeField] private TextMeshProUGUI timerDisplay2;
    [SerializeField] private AudioClip tickingTimer;
    private float timeRemaining;
    private float totalTime; // Store the original total time
    private bool isRunning = false;
    private bool tickingStarted = false;
    private AudioSource tickingAudioSource;

    // Whole seconds currently shown on the timer labels. -1 means "nothing drawn yet", which
    // forces the next UpdateDisplay to write even if the clock happens to start at 0.
    private int lastDisplayedSeconds = -1;
    
    // Screen pulse thresholds
    private bool orange50PercentPulsed = false;
    private bool red90PercentPulsed = false;
    private ScreenPulseEffect screenPulseEffect;

    void Start()
    {
        // Get the time from PlayerPrefs (defaulting to 180 if not set)
        int gameTimeInSeconds = PlayerPrefs.GetInt("GameTime", 180);
        timeRemaining = gameTimeInSeconds;
        totalTime = timeRemaining; // Store the original total time

        tickingAudioSource = GetComponent<AudioSource>();
        if (tickingAudioSource == null)
        {
            tickingAudioSource = gameObject.AddComponent<AudioSource>();
        }
        tickingAudioSource.loop = true;
        tickingAudioSource.playOnAwake = false;

        // Hand the source to the SFX bus. It tracks the SFX volume from here on,
        // so UpdateTickingAudio no longer has to re-apply it every frame.
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.RegisterSFXSource(tickingAudioSource);
        }


        // Find the ScreenPulseEffect component
        screenPulseEffect = FindObjectOfType<ScreenPulseEffect>();
        
        // Update display immediately
        UpdateDisplay();
        
        // Don't start the countdown yet
        isRunning = false;
    }

    public void StartTimer()
    {
        isRunning = true;
    }

    void Update()
    {
        if (isRunning && timeRemaining > 0)
        {
            timeRemaining -= Time.deltaTime;
            UpdateDisplay();
            UpdateTickingAudio();
            CheckPulseThresholds();
            
            // Check if time has run out
            if (timeRemaining <= 0)
            {
                timeRemaining = 0;
                UpdateDisplay();
                StopTickingAudio();
                EndGame();
            }
        }
    }

    private void UpdateTickingAudio()
    {
        if (tickingTimer == null || tickingAudioSource == null)
            return;

        if (timeRemaining <= 5f)
        {
            if (!tickingStarted)
            {
                tickingAudioSource.clip = tickingTimer;
                tickingAudioSource.Play();
                tickingStarted = true;
            }
        }
        else
        {
            StopTickingAudio();
        }
    }

    private void CheckPulseThresholds()
    {
        if (screenPulseEffect == null)
            return;

        float fiftyPercentThreshold = totalTime * 0.5f;
        float ninetyPercentThreshold = totalTime * 0.1f;

        // Check if 50% of time has elapsed (meaning 50% remains)
        if (!orange50PercentPulsed && timeRemaining <= fiftyPercentThreshold)
        {
            orange50PercentPulsed = true;
            screenPulseEffect.PulseOrange();
        }

        // Check if 90% of time has elapsed (meaning 10% remains)
        if (!red90PercentPulsed && timeRemaining <= ninetyPercentThreshold)
        {
            red90PercentPulsed = true;
            screenPulseEffect.StartRedPulse();
        }
    }

    private void StopTickingAudio()
    {
        if (tickingAudioSource != null)
        {
            tickingAudioSource.Stop();
        }

        tickingStarted = false;
    }

    private void UpdateDisplay()
    {
        // Update() calls this every frame, but the clock only ever reads whole seconds.
        // Reformatting an unchanged value would allocate a string 60x a second for nothing,
        // so the work is skipped until the displayed second actually ticks over.
        int totalSeconds = Mathf.FloorToInt(timeRemaining);
        if (totalSeconds == lastDisplayedSeconds)
            return;

        lastDisplayedSeconds = totalSeconds;

        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        string timeText = string.Format("{0:00}:{1:00}", minutes, seconds);

        if (timerDisplay != null)
            timerDisplay.text = timeText;

        if (timerDisplay2 != null)
            timerDisplay2.text = timeText;
    }

    private void EndGame()
    {
        isRunning = false;
        StopTickingAudio();
        
        // Stop red pulsing when game ends
        if (screenPulseEffect != null)
        {
            screenPulseEffect.StopRedPulse();
        }
        
        // Invoke the time up event
        OnTimeUp?.Invoke();
    }

    // Public method to pause/resume timer if needed
    public void PauseTimer()
    {
        isRunning = false;
        if (screenPulseEffect != null && red90PercentPulsed)
        {
            screenPulseEffect.StopRedPulse();
        }

        // The last-5-seconds tick loops on its own source, so it has to be held here
        // or it keeps ticking behind the pause menu
        if (tickingStarted && tickingAudioSource != null)
        {
            tickingAudioSource.Pause();
        }
    }

    public void ResumeTimer()
    {
        isRunning = true;
        if (screenPulseEffect != null && red90PercentPulsed)
        {
            screenPulseEffect.StartRedPulse();
        }

        if (tickingStarted && tickingAudioSource != null)
        {
            tickingAudioSource.UnPause();
        }
    }

    // Public method to stop the ticking audio
    public void StopAudio()
    {
        StopTickingAudio();
    }

    // Called by GameDifficultyApplier to set the correct time
    public void SetTimeLimit(float limit)
    {
        timeRemaining = limit;
        totalTime = limit; // Update total time as well
        
        // Reset pulse flags when time limit changes
        orange50PercentPulsed = false;
        red90PercentPulsed = false;
        
        if (screenPulseEffect != null)
        {
            screenPulseEffect.StopRedPulse();
        }
        
        if (timeRemaining > 5f)
        {
            StopTickingAudio();
        }
        UpdateDisplay();
    }

    // Public method to get remaining time
    public float GetTimeRemaining()
    {
        return timeRemaining;
    }

    // Public method to get total time limit
    public float GetTotalTime()
    {
        return totalTime;
    }

    private void OnDestroy()
    {
        StopTickingAudio();

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.UnregisterSFXSource(tickingAudioSource);
        }

        if (screenPulseEffect != null)
        {
            screenPulseEffect.StopRedPulse();
        }
    }
}

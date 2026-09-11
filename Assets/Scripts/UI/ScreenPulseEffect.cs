using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class ScreenPulseEffect : MonoBehaviour
{
    [SerializeField] private Image screenOverlay;
    [SerializeField] private float orangePulseDuration = 4.0f;
    [SerializeField] private float redPulseDuration = 4.0f;
    [SerializeField] private float pulseAlpha = 0.4f; // Reduced alpha for less intense effect

    private Coroutine orangePulseCoroutine;
    private Coroutine redPulseCoroutine;
    private bool isRedPulsing = false;

    private void Start()
    {
        // If no overlay is assigned, try to find one or create one
        if (screenOverlay == null)
        {
            screenOverlay = GetComponent<Image>();
        }

        if (screenOverlay == null)
        {
            return;
        }

        // Make overlay non-interactive so clicks pass through
        screenOverlay.raycastTarget = false;

        // Ensure overlay is initially transparent
        Color overlayColor = screenOverlay.color;
        overlayColor.a = 0f;
        screenOverlay.color = overlayColor;
    }

    /// <summary>
    /// Pulses the screen orange 5 times
    /// </summary>
    public void PulseOrange()
    {
        if (screenOverlay == null)
        {
            return;
        }

        // Stop any existing orange pulse
        if (orangePulseCoroutine != null)
            StopCoroutine(orangePulseCoroutine);

        orangePulseCoroutine = StartCoroutine(OrangePulseSequence());
    }

    /// <summary>
    /// Starts continuous red pulsing until stopped
    /// </summary>
    public void StartRedPulse()
    {
        if (screenOverlay == null)
        {
            return;
        }

        if (isRedPulsing)
            return; // Already pulsing red

        isRedPulsing = true;
        
        // Stop any existing orange pulse
        if (orangePulseCoroutine != null)
            StopCoroutine(orangePulseCoroutine);

        redPulseCoroutine = StartCoroutine(RedPulseLoop());
    }

    /// <summary>
    /// Stops the red pulsing
    /// </summary>
    public void StopRedPulse()
    {
        if (redPulseCoroutine != null)
            StopCoroutine(redPulseCoroutine);

        isRedPulsing = false;
        
        // Only fade out if the GameObject is active
        if (gameObject.activeInHierarchy)
        {
            FadeOverlay(0f, 0.2f);
        }
    }

    private IEnumerator OrangePulseSequence()
    {
        Color orangeColor = new Color(1f, 0.65f, 0f, pulseAlpha); // Orange color with reduced alpha
        int pulseCount = 5;

        for (int i = 0; i < pulseCount; i++)
        {
            // Fade in to orange
            yield return StartCoroutine(FadeToColor(orangeColor, orangePulseDuration / 2f));
            
            // Fade out from orange
            yield return StartCoroutine(FadeToColor(new Color(1f, 0.65f, 0f, 0f), orangePulseDuration / 2f));
        }
    }

    private IEnumerator RedPulseLoop()
    {
        Color redColor = new Color(1f, 0f, 0f, pulseAlpha); // Red color with reduced alpha
        
        while (isRedPulsing)
        {
            // Fade in to red
            yield return StartCoroutine(FadeToColor(redColor, redPulseDuration / 2f));
            
            // Fade out from red
            yield return StartCoroutine(FadeToColor(new Color(1f, 0f, 0f, 0f), redPulseDuration / 2f));
        }
    }

    private IEnumerator FadeToColor(Color targetColor, float duration)
    {
        if (screenOverlay == null)
            yield break;

        Color startColor = screenOverlay.color;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / duration;
            screenOverlay.color = Color.Lerp(startColor, targetColor, progress);
            yield return null;
        }

        screenOverlay.color = targetColor;
    }

    private void FadeOverlay(float targetAlpha, float duration)
    {
        if (screenOverlay == null || !gameObject.activeInHierarchy)
            return;

        StartCoroutine(FadeOverlayCoroutine(targetAlpha, duration));
    }

    private IEnumerator FadeOverlayCoroutine(float targetAlpha, float duration)
    {
        Color startColor = screenOverlay.color;
        Color endColor = startColor;
        endColor.a = targetAlpha;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / duration;
            screenOverlay.color = Color.Lerp(startColor, endColor, progress);
            yield return null;
        }

        screenOverlay.color = endColor;
    }

    private void OnDestroy()
    {
        StopRedPulse();
    }
}

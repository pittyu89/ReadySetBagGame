using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The shade and lock over the difficulty panel's preview video while a locked difficulty is
/// selected. Uses the Journal's locked container and its chain-breaking frames, so a locked
/// difficulty looks like a locked Journal entry and opens the same way.
///
/// It rattles when a locked bar is tapped, and plays the unlock through before fading away
/// the first time a newly unlocked difficulty is looked at.
/// </summary>
public class DifficultyLockOverlay : MonoBehaviour
{
    [Tooltip("The shade and the lock together, faded as one when the difficulty unlocks.")]
    [SerializeField] private CanvasGroup group;
    [SerializeField] private Image lockImage;

    [Header("Sprites (the Journal's)")]
    [SerializeField] private Sprite lockedSprite;
    [SerializeField] private Sprite[] unlockFrames = new Sprite[0];
    [Tooltip("The Journal's unlockFrameDuration, so both unlocks play at the same speed.")]
    [SerializeField] private float unlockFrameDuration = 0.05f;
    [Tooltip("Optional, like the Journal's unlock sound.")]
    [SerializeField] private AudioClip unlockSFX;

    [Header("Motion")]
    [SerializeField] private float fadeDuration = 0.3f;
    [SerializeField] private float rattleDuration = 0.35f;
    [Tooltip("How far the lock swings either way when a locked bar is tapped, in UI units.")]
    [SerializeField] private float rattleDistance = 6f;

    private Vector2 lockHome;
    private bool homeCaptured;
    private Coroutine running;

    public bool IsShowing => gameObject.activeSelf && group != null && group.alpha > 0f;

    /// <summary>True while the chains are breaking and the overlay fading.</summary>
    public bool IsUnlocking { get; private set; }

    private void CaptureHome()
    {
        if (homeCaptured || lockImage == null)
            return;
        lockHome = lockImage.rectTransform.anchoredPosition;
        homeCaptured = true;
    }

    /// <summary>Shows the locked state straight away.</summary>
    public void ShowLocked()
    {
        CaptureHome();
        Stop();
        gameObject.SetActive(true);
        if (group != null)
            group.alpha = 1f;
        if (lockImage != null)
        {
            lockImage.sprite = lockedSprite;
            lockImage.rectTransform.anchoredPosition = lockHome;
        }
    }

    /// <summary>Takes the overlay away without animating.</summary>
    public void Hide()
    {
        CaptureHome();
        Stop();
        if (lockImage != null)
            lockImage.rectTransform.anchoredPosition = lockHome;
        gameObject.SetActive(false);
    }

    /// <summary>A quick side-to-side shake: the bar was tapped but won't open.</summary>
    public void Rattle()
    {
        if (!isActiveAndEnabled || lockImage == null)
            return;

        CaptureHome();
        Stop();
        running = StartCoroutine(RattleRoutine());
    }

    /// <summary>
    /// Breaks the chains, then fades the shade and lock away. Showing or hiding the overlay
    /// again part-way through cuts it short.
    /// </summary>
    public void Unlock()
    {
        CaptureHome();
        Stop();
        gameObject.SetActive(true);
        IsUnlocking = true;
        running = StartCoroutine(UnlockRoutine());
    }

    private IEnumerator UnlockRoutine()
    {
        if (group != null)
            group.alpha = 1f;
        if (lockImage != null)
            lockImage.rectTransform.anchoredPosition = lockHome;

        SoundManager.Sfx(unlockSFX);

        foreach (Sprite frame in unlockFrames)
        {
            if (lockImage != null)
                lockImage.sprite = frame;
            yield return new WaitForSecondsRealtime(unlockFrameDuration);
        }

        for (float t = 0f; t < fadeDuration; t += Time.unscaledDeltaTime)
        {
            if (group != null)
                group.alpha = 1f - t / fadeDuration;
            yield return null;
        }

        running = null;
        Hide();
    }

    private IEnumerator RattleRoutine()
    {
        RectTransform rt = lockImage.rectTransform;
        for (float t = 0f; t < rattleDuration; t += Time.unscaledDeltaTime)
        {
            float p = t / rattleDuration;
            // Three swings, dying away, snapped to whole units so the pixel art stays crisp
            float offset = Mathf.Sin(p * Mathf.PI * 6f) * rattleDistance * (1f - p);
            rt.anchoredPosition = lockHome + new Vector2(Mathf.Round(offset), 0f);
            yield return null;
        }

        rt.anchoredPosition = lockHome;
        running = null;
    }

    private void Stop()
    {
        IsUnlocking = false;
        if (running != null)
        {
            StopCoroutine(running);
            running = null;
        }
    }

    private void OnDisable()
    {
        running = null;
        IsUnlocking = false;
        if (homeCaptured && lockImage != null)
            lockImage.rectTransform.anchoredPosition = lockHome;
    }
}

using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Full-screen "READY-SET-BAG!!" splash shown when the tutorial is dismissed.
/// Lives on its own GameObject so the timing keeps running after the
/// tutorial panel that triggered it has been deactivated.
///
/// The frosted background is delegated to <see cref="ScreenBlurBackdrop"/>, the same
/// component the quiz feedback uses, so both share one blur implementation and one set
/// of look settings. It also means this splash picks up that component's GPU capture
/// path, which is the one that works on Android under Vulkan.
/// </summary>
public class ReadySetBagOverlay : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [Tooltip("Blurred backdrop behind the splash. Configure its look on the component itself.")]
    [SerializeField] private ScreenBlurBackdrop blurBackdrop;
    [SerializeField] private float displayDuration = 2f;
    [SerializeField] private float fadeOutDuration = 0.35f;

    /// <summary>
    /// Raised once the splash has faded out and switched itself off — the first moment
    /// the player can actually see and play the room. Anything that should not begin
    /// while "READY-SET-BAG!!" is covering the screen, the round timer above all, hangs
    /// off this rather than off the splash being started.
    /// </summary>
    public event Action Finished;

    private void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    private void OnEnable()
    {
        // Hide everything for one frame so the capture below sees the game,
        // not the splash drawn on top of it.
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;

        if (blurBackdrop != null)
            blurBackdrop.Clear();

        StartCoroutine(ShowThenHide());
    }

    private void OnDisable()
    {
        if (blurBackdrop != null)
            blurBackdrop.Clear();
    }

    private IEnumerator ShowThenHide()
    {
        // The backdrop grabs the frame with the UI left on. This splash is still at
        // alpha 0 at that point, so it stays out of its own snapshot while the HUD
        // behind it is preserved.
        if (blurBackdrop != null)
            yield return StartCoroutine(blurBackdrop.CaptureIncludingUIRoutine());
        else
            yield return new WaitForEndOfFrame();

        if (canvasGroup != null)
            canvasGroup.alpha = 1f;

        // Realtime waits so the splash still finishes if the game is paused
        // (Time.timeScale == 0) while the tutorial is up.
        yield return new WaitForSecondsRealtime(displayDuration);

        // The group fade takes the backdrop and the title band down together, which is
        // why the backdrop's own fade durations are left at 0 — otherwise the two would
        // compound and the backdrop would drop out ahead of the band.
        if (canvasGroup != null && fadeOutDuration > 0f)
        {
            float elapsed = 0f;
            while (elapsed < fadeOutDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                canvasGroup.alpha = 1f - (elapsed / fadeOutDuration);
                yield return null;
            }
            canvasGroup.alpha = 0f;
        }

        if (blurBackdrop != null)
            blurBackdrop.Clear();

        // Raised before the SetActive rather than after it: by this line the splash is
        // already at alpha 0 with its backdrop released, so the screen is clear, and
        // deactivating the object first would stop this coroutine at its next yield.
        Finished?.Invoke();

        gameObject.SetActive(false);
    }
}

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One of the people hiding in the dark for the flashlight minigame.
///
/// A target owns nothing but its own visibility. Whether the light is on it, and for how
/// long, is <see cref="FlashlightMinigame"/>'s business — this just turns that into how
/// clearly the person reads on screen.
///
/// Hiding is not this component's job. The person sits under the dark sheet and is covered
/// by it until the beam's hole uncovers them, so nothing here has to fade them out — and a
/// person the light is resting on stays visible whether or not the finger is down. What is
/// set here is only how clearly they read once the light is on them: dim at first, coming
/// clear as the hold fills.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class FlashlightTarget : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("The person. Its alpha is driven from here, so leave the colour at white.")]
    [SerializeField] private Image portrait;
    [Tooltip("How clearly the person reads the instant the light lands on them, before " +
             "any holding. Low enough to be a shape rather than a face — 'malabo-labo'.")]
    [SerializeField, Range(0f, 1f)] private float glimpseAlpha = 0.18f;

    // 0 when the light has never settled here, 1 once the hold has finished.
    private float progress = 0f;
    private bool isFound = false;

    public float Progress => progress;
    public bool IsFound => isFound;
    public RectTransform Rect => (RectTransform)transform;

    private void Awake()
    {
        if (portrait == null)
            portrait = GetComponent<Image>();

        ResetTarget();
    }

    /// <summary>
    /// Puts the person back into the dark, unfound. Used to set up before a run.
    /// </summary>
    public void ResetTarget()
    {
        progress = 0f;
        isFound = false;
        ApplyAlpha();
    }

    /// <summary>
    /// Advances or drains the hold for this frame. <paramref name="lit"/> is whether the
    /// beam is on this person right now with the finger down; a found person ignores both.
    /// </summary>
    public void Tick(bool lit, float fillRate, float drainRate, float deltaTime)
    {
        if (isFound)
            return;

        progress = Mathf.Clamp01(progress + (lit ? fillRate : -drainRate) * deltaTime);

        // Found people are lifted out from under the dark sheet by the minigame, so they
        // stay lit and the screen fills up as the round goes on.
        if (progress >= 1f)
            isFound = true;

        ApplyAlpha();
    }

    private void ApplyAlpha()
    {
        if (portrait == null)
            return;

        // Held light clarifies the person: the alpha rides the same 0-1 the ring shows, so
        // "the yellow is proportional to the opacity" holds by construction.
        //
        // No zero case — being out of the beam is handled by the dark sheet covering them.
        // Zeroing here as well is what used to make a person vanish the instant the finger
        // came up, even with the light still sitting on them.
        float alpha = isFound ? 1f : Mathf.Lerp(glimpseAlpha, 1f, progress);

        Color c = portrait.color;
        portrait.color = new Color(c.r, c.g, c.b, alpha);
    }
}

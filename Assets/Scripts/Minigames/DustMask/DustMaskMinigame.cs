using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The dust mask minigame that runs after the quiz's dust mask question.
///
/// The character stands in the ruined room and the mask sits beside her. The player drags
/// it onto her face and lets go, and she is wearing it for the rest of the round. That is
/// the whole of it — one action, done properly.
///
/// Like the others there is nothing to fail. It runs whether the quiz answer was right or
/// wrong, there is no clock, and letting go of the mask short of her face only drops it
/// back where it started, ready to try again.
///
/// <see cref="Play"/> is a coroutine so QuizManager can simply yield on it and carry on
/// with the next question once it returns.
/// </summary>
public class DustMaskMinigame : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private CanvasGroup panelGroup;
    [Tooltip("Blurs the quiz behind the minigame, the same way the correct / wrong " +
             "overlay does. Optional.")]
    [SerializeField] private ScreenBlurBackdrop backdrop;

    [Header("Instruction")]
    [SerializeField] private CanvasGroup instructionCard;
    [SerializeField] private TextMeshProUGUI instructionLabel;
    [SerializeField, TextArea] private string instructionText = "Put on the mask";

    [Header("Character")]
    [Tooltip("The character. Its sprite is swapped for the masked one once the mask is on, " +
             "rather than a second mask object being switched on over her face — the two " +
             "authored sprites already line up, so there is nothing to keep in register.")]
    [SerializeField] private Image characterImage;
    [SerializeField] private Sprite unmaskedSprite;
    [SerializeField] private Sprite maskedSprite;
    [Tooltip("Marks where her face is. The mask goes on when it is dragged within " +
             "snapRadius of this, so the drop is judged against her face rather than " +
             "against the middle of her body.")]
    [SerializeField] private RectTransform faceTarget;

    [Header("Mask")]
    [Tooltip("The mask the player drags. Reuses the medkit's tool, which already knows how " +
             "to be picked up, carried and dropped back where it came from.")]
    [SerializeField] private MedkitTool maskTool;
    [Tooltip("How close to her face the mask has to get, in canvas units. Generous on " +
             "purpose: the point of the minigame is the gesture, not the precision.")]
    [SerializeField] private float snapRadius = 70f;

    [Header("Timing")]
    [Tooltip("How long the mask takes to travel the last of the way onto her face once it " +
             "has been dropped there.")]
    [SerializeField] private float slideDuration = 0.18f;
    [SerializeField] private float panelFadeDuration = 0.25f;
    [SerializeField] private float instructionFadeDuration = 0.3f;
    [Tooltip("Beat after the mask goes on, before the banner.")]
    [SerializeField] private float wearBeat = 0.5f;
    [Tooltip("Pause once the mask is on, before the minigame closes.")]
    [SerializeField] private float finishDelay = 0.9f;

    [Header("Finish")]
    [Tooltip("Optional. Flashed once the mask is on.")]
    [SerializeField] private GameObject completedBanner;

    private bool isPlaying = false;

    // Carried between frames so the drop can be judged on where the mask was while it was
    // still in the player's hand
    private bool wasHeld = false;
    private bool heldOverFace = false;

    public bool IsPlaying => isPlaying;

    private void Awake()
    {
        if (panelGroup == null && panelRoot != null)
            panelGroup = panelRoot.GetComponent<CanvasGroup>();

        if (instructionLabel != null && !string.IsNullOrEmpty(instructionText))
            instructionLabel.text = instructionText;

        // Reset the contents but leave the panel's active state alone. Awake first runs
        // during the SetActive in Open, so deactivating here would switch the panel back
        // off underneath the very coroutine that just turned it on.
        ResetVisuals();
    }

    /// <summary>
    /// Runs the whole minigame and returns once it has closed.
    /// Yield on this from the quiz; it never returns early or leaves the panel up.
    /// </summary>
    public IEnumerator Play()
    {
        if (isPlaying)
            yield break;

        if (characterImage == null || maskTool == null || faceTarget == null || maskedSprite == null)
        {
            // Bail loudly rather than quietly: a half-wired panel would otherwise hang the
            // quiz on a mask that can never go on.
            Debug.LogWarning("[DustMaskMinigame] Needs the character image, the mask, the " +
                             "face marker and the masked sprite — skipping.", this);
            yield break;
        }

        isPlaying = true;

        Open();

        if (backdrop != null)
            yield return StartCoroutine(backdrop.CaptureIncludingUIRoutine());

        yield return FadeGroup(panelGroup, 0f, 1f, panelFadeDuration);
        yield return FadeGroup(instructionCard, 0f, 1f, instructionFadeDuration);

        maskTool.gameObject.SetActive(true);
        maskTool.ResetTool();
        maskTool.SetAvailable(true);

        while (!WasMaskDroppedOnFace())
            yield return null;

        yield return SlideMaskOntoFace();

        yield return new WaitForSecondsRealtime(wearBeat);

        if (completedBanner != null)
            completedBanner.SetActive(true);

        yield return new WaitForSecondsRealtime(finishDelay);

        yield return FadeGroup(instructionCard, 1f, 0f, instructionFadeDuration);
        yield return FadeGroup(panelGroup, 1f, 0f, panelFadeDuration);

        if (backdrop != null)
            yield return StartCoroutine(backdrop.FadeOutRoutine());

        Close();
        isPlaying = false;
    }

    /// <summary>
    /// True on the frame the player lets go of the mask over her face.
    ///
    /// The drop is what puts it on, not merely passing over her: dragging the mask across
    /// her face on the way somewhere else should not count, and the player should be the
    /// one deciding when it goes on.
    ///
    /// Whether it was over her face is remembered from the last frame it was actually
    /// held, rather than measured after the release. The tool starts springing back to its
    /// corner the moment it is let go, so by the time the release is noticed it has already
    /// begun moving away from where it was dropped.
    /// </summary>
    private bool WasMaskDroppedOnFace()
    {
        if (maskTool.IsDragging)
        {
            wasHeld = true;
            heldOverFace = IsMaskWithinReach();
            return false;
        }

        if (!wasHeld)
            return false;

        wasHeld = false;

        bool landed = heldOverFace;
        heldOverFace = false;
        return landed;
    }

    /// <summary>
    /// Whether the mask is close enough to her face to count.
    ///
    /// Measured in the panel's own space rather than in screen pixels, so the reach is the
    /// same on a phone as in the Editor whatever the canvas is scaled to.
    /// </summary>
    private bool IsMaskWithinReach()
    {
        RectTransform panelRect = panelRoot != null
            ? panelRoot.transform as RectTransform
            : transform as RectTransform;

        if (panelRect == null)
            return false;

        Vector2 mask = panelRect.InverseTransformPoint(maskTool.transform.position);
        Vector2 face = panelRect.InverseTransformPoint(faceTarget.position);

        return Vector2.Distance(mask, face) <= snapRadius;
    }

    /// <summary>
    /// Carries the mask the last of the way onto her face, then hands over to the sprite
    /// that has it drawn on.
    ///
    /// Without this the mask jumps back to its corner and blinks out the instant it is
    /// dropped, because the tool springs home the moment it is no longer held — the player
    /// slides it to her face and watches it fly away from her. Sliding it the rest of the
    /// way finishes the gesture they started.
    /// </summary>
    private IEnumerator SlideMaskOntoFace()
    {
        maskTool.SetAvailable(false);

        // The tool's own spring pulls it home every frame it is not held, so it has to be
        // switched off for the mask to go anywhere else.
        maskTool.enabled = false;

        RectTransform maskRect = (RectTransform)maskTool.transform;
        Vector3 from = maskRect.position;
        Vector3 to = faceTarget.position;
        Vector3 fromScale = maskRect.localScale;

        for (float t = 0f; t < slideDuration; t += Time.unscaledDeltaTime)
        {
            // Ease out, so it settles onto her face rather than arriving at full speed
            float k = Mathf.Clamp01(t / slideDuration);
            float eased = 1f - Mathf.Pow(1f - k, 3f);

            maskRect.position = Vector3.Lerp(from, to, eased);
            maskRect.localScale = Vector3.Lerp(fromScale, Vector3.one, eased);
            yield return null;
        }

        maskRect.position = to;

        // The masked sprite takes over on the same frame the carried one goes, so there is
        // never a gap with no mask anywhere
        if (characterImage != null && maskedSprite != null)
            characterImage.sprite = maskedSprite;

        maskTool.gameObject.SetActive(false);
        maskTool.enabled = true;
        maskTool.ResetTool();
    }

    private void Open()
    {
        if (panelRoot != null)
            panelRoot.SetActive(true);

        if (panelGroup != null)
        {
            panelGroup.alpha = 0f;
            panelGroup.blocksRaycasts = true;
        }

        if (instructionCard != null)
            instructionCard.alpha = 0f;

        ResetVisuals();
    }

    private void Close()
    {
        ResetVisuals();

        if (backdrop != null)
            backdrop.Clear();

        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    /// <summary>
    /// Puts the character and the mask back to how a run starts, without touching whether
    /// the panel itself is on — see the note in Awake.
    /// </summary>
    private void ResetVisuals()
    {
        wasHeld = false;
        heldOverFace = false;

        if (characterImage != null && unmaskedSprite != null)
            characterImage.sprite = unmaskedSprite;

        // Out of sight until the round actually starts, so it cannot be grabbed during the
        // fade in
        if (maskTool != null)
        {
            // Switched back on in case a run was cut short mid-slide, which would otherwise
            // leave the tool inert for the next one
            maskTool.enabled = true;
            maskTool.ResetTool();
            maskTool.gameObject.SetActive(false);
        }

        if (completedBanner != null)
            completedBanner.SetActive(false);
    }

    private IEnumerator FadeGroup(CanvasGroup group, float from, float to, float duration)
    {
        if (group == null)
            yield break;

        if (duration <= 0f)
        {
            group.alpha = to;
            yield break;
        }

        group.alpha = from;

        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }

        group.alpha = to;
    }

    /// <summary>
    /// Shuts the minigame down mid-play, for when the quiz's minigame timer runs out.
    /// QuizManager stops the Play coroutine itself; this clears everything it left up.
    /// </summary>
    public void ForceClose()
    {
        StopAllCoroutines();
        Close();
        isPlaying = false;
    }
}

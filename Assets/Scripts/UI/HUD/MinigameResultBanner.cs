using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The banner across the middle of the screen when a minigame ends: the chibi over a dark
/// grey strip (the same #2A2A2A as the objective tab), with COMPLETE! or TIMES UP! beneath her.
///
/// One banner serves all twenty minigames. The quiz drives it, since the quiz is what
/// already knows both endings: the objective card tells it when the player is done, and its
/// own clock tells it when they ran out of time.
///
/// COMPLETE! plays the thumbs-up once and holds the last frame, fading out with the
/// minigame's panel. TIMES UP! is a Mario death: the chibi freezes, hops, and drops off the
/// bottom of the screen, and the quiz waits for her to land before tearing the minigame down.
///
/// Realtime throughout, like the minigames, so a banner never hangs on a paused timeScale.
/// </summary>
public class MinigameResultBanner : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [Tooltip("The chibi. Drawn over the strip so she can fall out through it.")]
    [SerializeField] private Image character;
    [SerializeField] private TextMeshProUGUI label;

    [Header("Complete")]
    [SerializeField] private string completeText = "COMPLETE!";
    [SerializeField] private Color completeColor = new Color(150f / 255f, 176f / 255f, 0f, 1f);
    [Tooltip("The thumbs-up, played once and held on its last frame.")]
    [SerializeField] private Sprite[] completeFrames = new Sprite[0];
    [SerializeField] private float completeFrameRate = 10f;
    [Tooltip("Played as COMPLETE! lands. Optional.")]
    [SerializeField] private AudioClip completeSFX;

    [Header("Times Up")]
    [SerializeField] private string timesUpText = "TIMES UP!";
    [SerializeField] private Color timesUpColor = new Color(1f, 0.302f, 0.302f, 1f);
    [SerializeField] private Sprite timesUpSprite;
    [Tooltip("Played as TIMES UP! lands. Optional.")]
    [SerializeField] private AudioClip timesUpSFX;
    [Tooltip("Played as she hops, the way Mario's death jingle kicks in. Optional.")]
    [SerializeField] private AudioClip fallSFX;
    [Tooltip("How long she stands frozen before the hop, like Mario does.")]
    [SerializeField] private float timesUpFreeze = 0.5f;
    [Tooltip("How high the hop goes, in canvas units.")]
    [SerializeField] private float hopHeight = 140f;
    [Tooltip("Pull on the fall, in canvas units per second squared.")]
    [SerializeField] private float gravity = 2600f;
    [Tooltip("How long the banner holds once she is off screen, before the minigame closes.")]
    [SerializeField] private float timesUpLinger = 0.35f;

    [Header("Show / Hide")]
    [SerializeField] private float fadeInDuration = 0.12f;
    [SerializeField] private float fadeOutDuration = 0.25f;
    [Tooltip("How far the chibi and the words swell as the banner lands, before settling.")]
    [SerializeField] private float popScale = 1.2f;
    [SerializeField] private float popDuration = 0.25f;

    private RectTransform characterRect;
    private Vector2 characterRestPosition;

    // The minigame panel the COMPLETE! banner fades out with
    private CanvasGroup followGroup;
    private float ownAlpha;
    private Coroutine routine;

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (character != null)
        {
            characterRect = character.rectTransform;
            characterRestPosition = characterRect.anchoredPosition;
        }
    }

    private void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    private void LateUpdate()
    {
        if (canvasGroup == null)
            return;

        // A panel that has closed counts as faded out, whatever its group was reset to
        float follow = followGroup == null ? 1f
            : followGroup.gameObject.activeInHierarchy ? followGroup.alpha : 0f;
        canvasGroup.alpha = ownAlpha * follow;
    }

    /// <summary>
    /// Raises COMPLETE! and plays the thumbs-up. <paramref name="follow"/> is the minigame's
    /// panel: the banner fades as it does, so the two leave the screen together.
    /// </summary>
    public void ShowComplete(CanvasGroup follow)
    {
        Begin(completeText, completeColor, follow);
        routine = StartCoroutine(CompleteRoutine());
    }

    /// <summary>
    /// Raises TIMES UP! and plays the fall. Yield on it: it returns once she is off screen
    /// and the banner has held, with the banner still up for the minigame to close under.
    /// </summary>
    public IEnumerator PlayTimesUp()
    {
        Begin(timesUpText, timesUpColor, null);
        yield return routine = StartCoroutine(TimesUpRoutine());
        routine = null;
    }

    /// <summary>Takes the banner down, fading unless it is already out of sight.</summary>
    public void Hide()
    {
        if (!gameObject.activeInHierarchy)
            return;

        StopRoutine();

        if (canvasGroup == null || canvasGroup.alpha <= 0f || fadeOutDuration <= 0f)
        {
            Close();
            return;
        }

        routine = StartCoroutine(FadeOutThenClose());
    }

    private void Begin(string text, Color color, CanvasGroup follow)
    {
        gameObject.SetActive(true);
        transform.SetAsLastSibling();
        StopRoutine();

        followGroup = follow;
        ownAlpha = 0f;

        if (label != null)
        {
            label.text = text;
            label.color = color;
        }

        if (characterRect != null)
        {
            characterRect.anchoredPosition = characterRestPosition;
            characterRect.localScale = Vector3.one;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            // The strip swallows taps for as long as it is up, so a minigame frozen by the
            // clock can't be finished behind TIMES UP!
            canvasGroup.blocksRaycasts = true;
        }
    }

    private IEnumerator CompleteRoutine()
    {
        SetSprite(completeFrames != null && completeFrames.Length > 0 ? completeFrames[0] : null);
        SoundManager.Sfx(completeSFX);
        StartCoroutine(FadeIn());
        StartCoroutine(Pop());

        if (completeFrames == null || completeFrames.Length <= 1 || completeFrameRate <= 0f)
            yield break;

        float frameTime = 1f / completeFrameRate;
        for (int i = 1; i < completeFrames.Length; i++)
        {
            yield return new WaitForSecondsRealtime(frameTime);
            SetSprite(completeFrames[i]);
        }
    }

    private IEnumerator TimesUpRoutine()
    {
        SetSprite(timesUpSprite);
        SoundManager.Sfx(timesUpSFX);
        StartCoroutine(FadeIn());
        StartCoroutine(Pop());

        yield return new WaitForSecondsRealtime(timesUpFreeze);

        SoundManager.Sfx(fallSFX);

        if (characterRect != null)
        {
            // Up, then down past the bottom edge: launched at the speed that just reaches
            // hopHeight, and let gravity do the rest.
            float velocity = Mathf.Sqrt(2f * gravity * Mathf.Max(0f, hopHeight));
            float y = characterRestPosition.y;
            float offScreen = OffScreenY();

            while (y > offScreen)
            {
                float dt = Time.unscaledDeltaTime;
                velocity -= gravity * dt;
                y += velocity * dt;
                characterRect.anchoredPosition = new Vector2(characterRestPosition.x, y);
                yield return null;
            }
        }

        yield return new WaitForSecondsRealtime(timesUpLinger);
    }

    /// <summary>The height, in the chibi's own space, at which she is fully below the screen.</summary>
    private float OffScreenY()
    {
        RectTransform root = (RectTransform)transform;
        RectTransform parent = characterRect.parent as RectTransform;
        float screenBottom = parent != null
            ? parent.InverseTransformPoint(root.TransformPoint(new Vector3(0f, root.rect.yMin, 0f))).y
            : root.rect.yMin;

        // anchoredPosition is measured from the anchor, so bring the bottom edge into that
        // frame before adding the chibi's own height to clear it
        float anchorY = parent != null
            ? Mathf.Lerp(parent.rect.yMin, parent.rect.yMax, characterRect.anchorMin.y)
            : 0f;

        return screenBottom - anchorY - characterRect.rect.height;
    }

    private IEnumerator FadeIn()
    {
        for (float t = 0f; t < fadeInDuration; t += Time.unscaledDeltaTime)
        {
            ownAlpha = t / fadeInDuration;
            yield return null;
        }
        ownAlpha = 1f;
    }

    private IEnumerator Pop()
    {
        if (popDuration <= 0f)
            yield break;

        for (float t = 0f; t < popDuration; t += Time.unscaledDeltaTime)
        {
            float k = t / popDuration;
            // Lands big and settles, the same beat as the objective diamond's tick
            float s = Mathf.Lerp(popScale, 1f, 1f - (1f - k) * (1f - k));
            SetPopScale(s);
            yield return null;
        }
        SetPopScale(1f);
    }

    private void SetPopScale(float s)
    {
        Vector3 scale = new Vector3(s, s, 1f);
        if (characterRect != null)
            characterRect.localScale = scale;
        if (label != null)
            label.rectTransform.localScale = scale;
    }

    private IEnumerator FadeOutThenClose()
    {
        float from = ownAlpha;
        for (float t = 0f; t < fadeOutDuration; t += Time.unscaledDeltaTime)
        {
            ownAlpha = Mathf.Lerp(from, 0f, t / fadeOutDuration);
            yield return null;
        }

        routine = null;
        Close();
    }

    private void SetSprite(Sprite sprite)
    {
        if (character == null)
            return;

        character.sprite = sprite;
        character.enabled = sprite != null;
    }

    private void StopRoutine()
    {
        // The fade and pop run alongside the main routine, so stop the lot
        StopAllCoroutines();
        routine = null;
    }

    private void Close()
    {
        followGroup = null;
        ownAlpha = 0f;

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
        }

        gameObject.SetActive(false);
    }
}

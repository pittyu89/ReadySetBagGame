using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The water bottle the player taps and holds to pour.
///
/// One of these sits above each glass, but only the armed one takes input — the rest are
/// hidden until their turn, so the minigame reads as one bottle at a time even though
/// three are authored in the scene.
///
/// Input goes through the EventSystem rather than polling Input, so it behaves the same
/// on a phone and in the Editor, and a press that started on the bottle still ends here
/// even if the finger slides off before lifting.
/// </summary>
[RequireComponent(typeof(Image))]
public class PourBottle : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [Header("Spout")]
    [Tooltip("Marks the bottle's mouth. The pour stream starts here, so it should sit on " +
             "the opening in the sprite, not at the bottle's centre.")]
    [SerializeField] private RectTransform spout;

    [Header("Tilt")]
    [Tooltip("Angle the bottle tips to while pouring.")]
    [SerializeField] private float pourAngle = -38f;
    [Tooltip("Seconds to tip over and back. The stream waits for the tip, so keep it short.")]
    [SerializeField] private float tiltDuration = 0.14f;

    [Header("Entrance")]
    [SerializeField] private float appearDuration = 0.28f;
    [Tooltip("How far above its resting spot the bottle drops in from.")]
    [SerializeField] private float appearRise = 40f;
    [SerializeField] private float retireDuration = 0.22f;

    [Header("Idle Hint")]
    [Tooltip("Bob height while the bottle is waiting to be tapped, so it reads as the " +
             "thing to press. Set 0 to hold it still.")]
    [SerializeField] private float idleBobHeight = 6f;
    [SerializeField] private float idleBobSpeed = 2.4f;

    /// <summary>Raised on press and release, but only while the bottle is armed.</summary>
    public event Action<PourBottle> HoldStarted;
    public event Action<PourBottle> HoldEnded;

    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;

    private Vector2 restPosition;
    private float restAngle;

    // Armed means "this is the bottle whose turn it is" — everything else ignores input.
    private bool isArmed = false;
    private bool isHeld = false;

    private Coroutine tiltRoutine;

    public bool IsHeld => isHeld;
    public RectTransform Spout => spout != null ? spout : rectTransform;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();

        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        restPosition = rectTransform.anchoredPosition;
        restAngle = rectTransform.localEulerAngles.z;

        Hide();
    }

    private void OnDisable()
    {
        // A hold left running while the panel closes would keep the stream pouring into
        // nothing the next time the minigame opens.
        ReleaseHold();
    }

    private void Update()
    {
        if (!isArmed || isHeld || idleBobHeight <= 0f)
            return;

        float bob = Mathf.Sin(Time.unscaledTime * idleBobSpeed) * idleBobHeight;
        rectTransform.anchoredPosition = restPosition + new Vector2(0f, bob);
    }

    /// <summary>
    /// Puts the bottle away with no animation. Used to set up before the minigame runs.
    /// </summary>
    public void Hide()
    {
        isArmed = false;
        ReleaseHold();

        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        rectTransform.anchoredPosition = restPosition;
        rectTransform.localEulerAngles = new Vector3(0f, 0f, restAngle);
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Drops the bottle in and starts accepting presses.
    /// </summary>
    public IEnumerator Appear()
    {
        gameObject.SetActive(true);
        rectTransform.localEulerAngles = new Vector3(0f, 0f, restAngle);
        canvasGroup.blocksRaycasts = false;

        Vector2 from = restPosition + new Vector2(0f, appearRise);

        for (float t = 0f; t < appearDuration; t += Time.unscaledDeltaTime)
        {
            float k = t / appearDuration;
            // Ease out, so it settles rather than arriving at full speed
            float eased = 1f - (1f - k) * (1f - k);
            rectTransform.anchoredPosition = Vector2.Lerp(from, restPosition, eased);
            canvasGroup.alpha = eased;
            yield return null;
        }

        rectTransform.anchoredPosition = restPosition;
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        isArmed = true;
    }

    /// <summary>
    /// Stops accepting presses and fades the bottle out, once its glass is full.
    /// </summary>
    public IEnumerator Retire()
    {
        isArmed = false;
        ReleaseHold();
        canvasGroup.blocksRaycasts = false;

        yield return TiltTo(restAngle, tiltDuration);

        Vector2 from = rectTransform.anchoredPosition;
        Vector2 to = from + new Vector2(0f, appearRise * 0.6f);

        for (float t = 0f; t < retireDuration; t += Time.unscaledDeltaTime)
        {
            float k = t / retireDuration;
            rectTransform.anchoredPosition = Vector2.Lerp(from, to, k);
            canvasGroup.alpha = 1f - k;
            yield return null;
        }

        Hide();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!isArmed || isHeld)
            return;

        isHeld = true;

        // Snap back from the idle bob so the tip starts from the resting pose
        rectTransform.anchoredPosition = restPosition;

        StartTilt(pourAngle);
        HoldStarted?.Invoke(this);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        ReleaseHold();
    }

    /// <summary>
    /// Ends a hold if one is running. Safe to call when nothing is held.
    /// </summary>
    public void ReleaseHold()
    {
        if (!isHeld)
            return;

        isHeld = false;
        StartTilt(restAngle);
        HoldEnded?.Invoke(this);
    }

    private void StartTilt(float targetAngle)
    {
        if (!isActiveAndEnabled)
            return;

        if (tiltRoutine != null)
            StopCoroutine(tiltRoutine);

        tiltRoutine = StartCoroutine(TiltTo(targetAngle, tiltDuration));
    }

    private IEnumerator TiltTo(float targetAngle, float duration)
    {
        float from = rectTransform.localEulerAngles.z;
        // Euler angles come back as 0-360; go the short way round instead of spinning
        if (from > 180f)
            from -= 360f;

        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float angle = Mathf.Lerp(from, targetAngle, t / duration);
            rectTransform.localEulerAngles = new Vector3(0f, 0f, angle);
            yield return null;
        }

        rectTransform.localEulerAngles = new Vector3(0f, 0f, targetAngle);
        tiltRoutine = null;
    }
}

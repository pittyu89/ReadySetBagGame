using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The big red button the player mashes during the whistle minigame.
///
/// It owns nothing but its own look and the tap event — how many taps are needed and what
/// they fill up is <see cref="WhistleTapMinigame"/>'s business.
///
/// Taps are counted on press rather than release. A spam button that waits for the finger
/// to lift feels a beat behind on every tap, and at twenty-odd taps that adds up.
///
/// Input goes through the EventSystem rather than polling Input, so it behaves the same on
/// a phone and in the Editor, and a press that started on the button still ends here even
/// if the finger slides off before lifting.
/// </summary>
[RequireComponent(typeof(Image))]
public class WhistleTapButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [Header("Colours")]
    [Tooltip("The graphic that changes colour on a press. Leave empty to tint this " +
             "object's own Image — set it when the red face is a child of a white ring, " +
             "since the ring should keep its colour.")]
    [SerializeField] private Graphic faceGraphic;
    [SerializeField] private Color idleColor = new Color(0.90f, 0.30f, 0.28f, 1f);
    [Tooltip("Held for pressedHold seconds on every tap, so a fast mash still reads as a " +
             "string of separate presses instead of a solid dark button.")]
    [SerializeField] private Color pressedColor = new Color(0.68f, 0.15f, 0.14f, 1f);
    [SerializeField] private float pressedHold = 0.07f;

    [Header("Squash")]
    [Tooltip("Scale the button snaps to on a tap before springing back to 1.")]
    [SerializeField] private float pressedScale = 0.90f;
    [Tooltip("How quickly the button springs back out. Higher is snappier.")]
    [SerializeField] private float releaseSpeed = 12f;

    [Header("Idle Hint")]
    [Tooltip("Gentle breathing while the button is armed and untouched, so it reads as the " +
             "thing to press. Set 0 to hold it still.")]
    [SerializeField] private float idlePulseAmount = 0.035f;
    [SerializeField] private float idlePulseSpeed = 3.2f;

    /// <summary>Raised once per press, and only while the button is armed.</summary>
    public event Action Tapped;

    private RectTransform rectTransform;
    private Image image;

    // Armed means "the minigame is listening" — before and after that the button is inert
    // even though it is still on screen and still fading in or out.
    private bool isArmed = false;
    private bool isHeld = false;

    // Springs back to 1 rather than snapping, so consecutive taps stack into a wobble
    private float squash = 1f;
    private float pressedUntil = 0f;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        image = GetComponent<Image>();

        if (faceGraphic == null)
            faceGraphic = image;

        SetArmed(false);
    }

    private void OnDisable()
    {
        isHeld = false;
    }

    private void Update()
    {
        squash = Mathf.Lerp(squash, 1f, 1f - Mathf.Exp(-releaseSpeed * Time.unscaledDeltaTime));

        float pulse = 1f;
        if (isArmed && !isHeld && idlePulseAmount > 0f)
            pulse += Mathf.Sin(Time.unscaledTime * idlePulseSpeed) * idlePulseAmount;

        rectTransform.localScale = Vector3.one * (squash * pulse);

        if (faceGraphic != null)
            faceGraphic.color = Time.unscaledTime < pressedUntil ? pressedColor : idleColor;
    }

    /// <summary>
    /// Starts or stops accepting taps. Resets the look either way, so the button never
    /// keeps a half-pressed pose across the gap between two runs of the minigame.
    /// </summary>
    public void SetArmed(bool armed)
    {
        isArmed = armed;
        isHeld = false;
        squash = 1f;
        pressedUntil = 0f;

        if (rectTransform != null)
            rectTransform.localScale = Vector3.one;

        if (faceGraphic != null)
            faceGraphic.color = idleColor;

        // Only the armed button swallows presses; otherwise it would eat taps aimed at
        // whatever the panel puts up once the bar is full.
        if (image != null)
            image.raycastTarget = armed;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!isArmed)
            return;

        isHeld = true;
        squash = pressedScale;
        pressedUntil = Time.unscaledTime + pressedHold;

        Tapped?.Invoke();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isHeld = false;
    }
}

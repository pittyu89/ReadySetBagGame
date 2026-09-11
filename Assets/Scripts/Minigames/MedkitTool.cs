using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One thing the player picks up and carries somewhere — a first-aid tool such as the
/// cotton swab or the gauze, or the dust mask in its own minigame.
///
/// A tool is dragged out of the kit and over the arm, and springs back to its slot when
/// let go. What it actually does on contact is <see cref="FirstAidMinigame"/>'s business;
/// this only knows where it is and whether it is in hand.
///
/// The working end is a separate marker rather than the tool's middle, because the swab
/// cleans with its cotton tip and lining that tip up with a bruise is the whole action.
/// The marker says where the tool touches; it is not where the tool is held.
///
/// Input goes through the EventSystem rather than polling Input, so it behaves the same on
/// a phone and in the Editor.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class MedkitTool : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Header("Pieces")]
    [Tooltip("The business end — the swab's cotton, the gauze's pad. Contact is tested " +
             "here, not at the middle of the sprite.")]
    [SerializeField] private RectTransform tip;
    [SerializeField] private Image icon;

    [Header("Behaviour")]
    [Tooltip("Tick for something that is used where it lies rather than carried to the arm " +
             "— the alcohol, which is tapped to wet a swab. A click-only tool raises Used " +
             "but never follows the finger.")]
    [SerializeField] private bool clickOnly = false;

    [Header("Feel")]
    [Tooltip("How quickly the tool springs back to its slot when released.")]
    [SerializeField] private float returnSpeed = 12f;
    [Tooltip("Lifted slightly while in hand, so it reads as picked up off the tray.")]
    [SerializeField] private float heldScale = 1.15f;
    [Tooltip("Degrees the tool tilts to while in hand — a swab is held at an angle like a " +
             "pen, not straight up. It pivots about its own centre, so the tip swings a " +
             "little as it is picked up. 0 keeps it upright.")]
    [SerializeField] private float heldRotation = 0f;
    [Header("Slide Rail")]
    [Tooltip("Optional. With this set the tool cannot be dragged freely: it only slides " +
             "along the straight line running from where it rests, through this point, and " +
             "a little beyond. Leave empty for a tool that is carried anywhere.")]
    [SerializeField] private RectTransform slideTarget;
    [Tooltip("How far the slide may carry on past the target, as a fraction of the distance " +
             "from the resting place to it. 0 stops it dead on the target; 1 gives it the " +
             "same run again on the far side, so the target sits halfway along the rail.")]
    [SerializeField] private float slideOvershoot = 1f;

    [Header("Selection")]
    [Tooltip("Optional. The edge drawn round the tool while it is the one in use — a " +
             "holder of silhouette copies sitting behind the icon, offset in a ring. " +
             "Switched off unless this tool is selected.")]
    [SerializeField] private GameObject selectionOutline;

    [Tooltip("Shown greyed out until the minigame makes it available.")]
    [SerializeField] private Color lockedTint = new Color(1f, 1f, 1f, 0.25f);

    /// <summary>Raised when an available tool is pressed, carried or not.</summary>
    public event Action Used;

    private RectTransform selfRect;
    private Vector2 homePosition;
    private float homeRotation;
    private bool isAvailable = false;
    private bool isDragging = false;
    private Vector2 grabOffset;   // Where on the tool it was picked up, kept for the drag

    /// <summary>True while the player has hold of this tool.</summary>
    public bool IsDragging => isDragging && isAvailable;

    /// <summary>Screen position of the working end, for testing contact with the arm.</summary>
    public Vector2 TipPosition =>
        RectTransformUtility.WorldToScreenPoint(null, (tip != null ? tip : (RectTransform)transform).position);

    private void Awake()
    {
        EnsureInit();
        SetAvailable(false);
    }

    /// <summary>
    /// Grabs what this needs the first time anything asks for it.
    ///
    /// Not left to Awake alone: the panel's own Awake runs before its children's, and it
    /// resets every tool on the way through — so the first call into a tool can arrive
    /// before that tool has woken up. The authored position is still the one in the scene
    /// at that point, so capturing home here is safe.
    /// </summary>
    private void EnsureInit()
    {
        if (selfRect != null)
            return;

        selfRect = (RectTransform)transform;
        homePosition = selfRect.anchoredPosition;
        homeRotation = selfRect.localEulerAngles.z;

        if (icon == null)
            icon = GetComponent<Image>();
    }

    private void OnDisable()
    {
        isDragging = false;
    }

    private void Update()
    {
        EnsureInit();

        if (isDragging)
            return;

        // Springs home rather than snapping, so a fumbled grab reads as the tool being
        // put back rather than teleporting.
        float k = 1f - Mathf.Exp(-returnSpeed * Time.unscaledDeltaTime);
        selfRect.anchoredPosition = Vector2.Lerp(selfRect.anchoredPosition, homePosition, k);
        selfRect.localScale = Vector3.Lerp(selfRect.localScale, Vector3.one, k);

        // Straightens up as it settles back into the kit
        float angle = selfRect.localEulerAngles.z;
        if (angle > 180f)
            angle -= 360f;
        selfRect.localEulerAngles = new Vector3(0f, 0f, Mathf.Lerp(angle, homeRotation, k));
    }

    /// <summary>
    /// Draws or clears the edge that marks this as the tool currently in use.
    ///
    /// Most tools never light up, so an unassigned outline is silently fine rather than
    /// something to warn about.
    /// </summary>
    public void SetSelected(bool selected)
    {
        if (selectionOutline != null)
            selectionOutline.SetActive(selected);
    }

    /// <summary>
    /// Puts the tool back in its slot, unavailable and unheld. Used to set up before a run.
    /// </summary>
    public void ResetTool()
    {
        EnsureInit();

        SetSelected(false);
        isDragging = false;
        selfRect.anchoredPosition = homePosition;
        selfRect.localScale = Vector3.one;
        selfRect.localEulerAngles = new Vector3(0f, 0f, homeRotation);
        SetAvailable(false);
    }

    /// <summary>
    /// Greys the tool out or lets it be picked up. The gauze stays unavailable until the
    /// arm has been cleaned, so the player cannot bandage over the bruises.
    /// </summary>
    public void SetAvailable(bool available)
    {
        EnsureInit();

        isAvailable = available;

        if (!available)
            isDragging = false;

        if (icon != null)
        {
            icon.color = available ? Color.white : lockedTint;
            icon.raycastTarget = available;
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        EnsureInit();

        if (!isAvailable)
            return;

        Used?.Invoke();

        // A click-only tool is used in place — it must not leap to the finger
        if (clickOnly)
            return;

        isDragging = true;

        // Lifted and tilted first: both change where the sprite sits around its centre, and
        // the grip has to be measured against how the tool is actually being held.
        selfRect.localScale = Vector3.one * heldScale;
        selfRect.localEulerAngles = new Vector3(0f, 0f, homeRotation + heldRotation);

        Vector2 pointerAnchored;
        grabOffset = TryPointerAnchored(eventData, out pointerAnchored)
            ? selfRect.anchoredPosition - pointerAnchored
            : Vector2.zero;

        MoveTo(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isAvailable || !isDragging)
            return;

        MoveTo(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isDragging = false;
    }

    /// <summary>
    /// Where the tool would sit if its own centre were directly under the pointer.
    ///
    /// The local point comes back measured from the parent's pivot, but anchoredPosition is
    /// measured from this tool's anchor. Those only coincide when the parent's pivot is
    /// centred, so the anchor's own offset is taken out here — otherwise the tool sits half
    /// the tray away from the finger.
    /// </summary>
    private bool TryPointerAnchored(PointerEventData eventData, out Vector2 anchored)
    {
        anchored = Vector2.zero;

        RectTransform parent = selfRect.parent as RectTransform;
        if (parent == null)
            return false;

        Canvas canvas = GetComponentInParent<Canvas>();
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, cam, out local))
            return false;

        Vector2 anchorCentre = Vector2.Scale(
            (selfRect.anchorMin + selfRect.anchorMax) * 0.5f - parent.pivot,
            parent.rect.size);

        anchored = local - anchorCentre;
        return true;
    }

    /// <summary>
    /// Carries the tool along with the pointer, holding whatever grip the player took.
    ///
    /// This used to drop the tool's working end onto the pointer instead, so that lining
    /// the tip up with a bruise needed no thought. It made the tool jump out from under the
    /// finger the instant it was grabbed — worse on touch, where the swab leapt a third of
    /// its own length away from the thumb that had just pressed it. Keeping the grab offset
    /// means the tool stays exactly where it was taken hold of; the tip still does the
    /// cleaning, it is simply no longer pinned to the cursor.
    /// </summary>
    private void MoveTo(PointerEventData eventData)
    {
        Vector2 pointerAnchored;
        if (!TryPointerAnchored(eventData, out pointerAnchored))
            return;

        selfRect.anchoredPosition = ConstrainToRail(pointerAnchored + grabOffset);
    }

    /// <summary>
    /// Holds the tool on its rail, when it has one.
    ///
    /// The finger is free to wander; the tool is not. Where the player has dragged to is
    /// projected onto the line, so the tool tracks the part of the movement that runs along
    /// the rail and ignores the part that runs across it. Travel is bounded at the resting
    /// place on one side and a little past the target on the other, so it cannot be pushed
    /// out the back of the rail or dragged off behind where it started.
    /// </summary>
    private Vector2 ConstrainToRail(Vector2 wanted)
    {
        if (slideTarget == null)
            return wanted;

        Vector2 railEnd;
        if (!TryWorldToAnchored(slideTarget.position, out railEnd))
            return wanted;

        Vector2 axis = railEnd - homePosition;
        float length = axis.magnitude;

        if (length <= Mathf.Epsilon)
            return wanted;

        Vector2 direction = axis / length;
        float along = Vector2.Dot(wanted - homePosition, direction);

        along = Mathf.Clamp(along, 0f, length * (1f + Mathf.Max(0f, slideOvershoot)));

        return homePosition + direction * along;
    }

    /// <summary>
    /// Turns a world position into the anchoredPosition this tool would need to sit there,
    /// so a point living elsewhere in the panel can be compared against the tool's own.
    /// </summary>
    private bool TryWorldToAnchored(Vector3 world, out Vector2 anchored)
    {
        anchored = Vector2.zero;

        RectTransform parent = selfRect.parent as RectTransform;
        if (parent == null)
            return false;

        Vector2 local = parent.InverseTransformPoint(world);

        Vector2 anchorCentre = Vector2.Scale(
            (selfRect.anchorMin + selfRect.anchorMax) * 0.5f - parent.pivot,
            parent.rect.size);

        anchored = local - anchorCentre;
        return true;
    }
}

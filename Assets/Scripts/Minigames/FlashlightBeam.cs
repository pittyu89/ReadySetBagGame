using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The pool of light the player drags around the dark room.
///
/// Input is taken on a full-screen surface rather than on the light itself: the beam is a
/// small circle, and asking a thumb to grab it precisely on a phone would make the
/// minigame about dexterity instead of searching. Press anywhere and the light comes to
/// the finger; drag and it follows.
///
/// Input goes through the EventSystem rather than polling Input, so it behaves the same on
/// a phone and in the Editor, and a drag that started here still ends here even if the
/// finger leaves the surface before lifting.
/// </summary>
public class FlashlightBeam : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Header("Pieces")]
    [Tooltip("Moves to the pointer. Everything below is parented to it.")]
    [SerializeField] private RectTransform lightRoot;
    [Tooltip("The dark sheet the people hide under. Its material punches a hole where the " +
             "beam is, which is what actually reveals them — nothing is faded in.")]
    [SerializeField] private Image darknessMask;
    [Tooltip("The dim pool that shows where the beam is pointing, whatever it is on.")]
    [SerializeField] private Image halo;
    [Tooltip("The yellow that grows from the middle as a person is held. Its scale is the " +
             "hold, so it reads as the light focusing in.")]
    [SerializeField] private Image focus;
    [Tooltip("White outline, brightening as the hold completes.")]
    [SerializeField] private Image ring;

    [Header("Reach")]
    [Tooltip("Radius of the hole cut in the dark, in canvas units. Big enough that a whole " +
             "person fits inside it rather than being sliced by the rim.")]
    [SerializeField] private float lightRadius = 135f;
    [Tooltip("How soft the rim of the hole is, in canvas units.")]
    [SerializeField] private float edgeSoftness = 26f;
    [Tooltip("Cuts a square patch of light instead of a round beam — a glowstick lying on " +
             "the floor lights the area around it rather than throwing a cone. Set the " +
             "halo, focus and ring to square sprites to match, and drop edgeSoftness, " +
             "since a glowstick's pool has a far more definite edge than a torch beam.")]
    [SerializeField] private bool squareLight = false;

    [Header("Feel")]
    [Tooltip("How quickly the light slides to the finger. Chasing rather than snapping " +
             "keeps a fast drag from teleporting past a person.")]
    [SerializeField] private float followSpeed = 22f;
    [Tooltip("Smallest the yellow shrinks to at zero hold. Not quite nothing, so there is " +
             "a bead of light at the centre of the beam even before a person is found.")]
    [SerializeField, Range(0f, 1f)] private float focusMinScale = 0.06f;

    private bool isOn = false;
    private bool isHeld = false;

    // Where the finger is, which the light eases toward rather than jumping to.
    private Vector2 targetPosition;

    private static readonly int LightPosId = Shader.PropertyToID("_LightPos");
    private static readonly int RadiusId   = Shader.PropertyToID("_Radius");
    private static readonly int SoftId     = Shader.PropertyToID("_Soft");
    private static readonly int AspectId   = Shader.PropertyToID("_Aspect");
    private static readonly int SquareId   = Shader.PropertyToID("_Square");

    private RectTransform selfRect;
    private Canvas parentCanvas;
    private Material maskMaterial;

    /// <summary>True while the finger is down, which is what "hold" means.</summary>
    public bool IsHeld => isHeld && isOn;
    public bool IsOn => isOn;
    public float LightRadius => lightRadius;

    /// <summary>
    /// True when the light is a square patch rather than a round beam. The minigame needs
    /// this to test containment against the shape the player can actually see.
    /// </summary>
    public bool IsSquare => squareLight;
    public Vector2 Position => lightRoot != null ? lightRoot.anchoredPosition : Vector2.zero;

    private void Awake()
    {
        selfRect = (RectTransform)transform;
        parentCanvas = GetComponentInParent<Canvas>();

        // A copy of its own. Unlike Renderer.material, Graphic.material hands back the
        // shared asset rather than instancing it, so writing the hole straight to it would
        // save the last frame's beam position into the project — and the cleanup in
        // OnDestroy would delete the asset outright.
        if (darknessMask != null && darknessMask.material != null)
        {
            maskMaterial = new Material(darknessMask.material);
            darknessMask.material = maskMaterial;
        }

        SetOn(false);
    }

    private void OnDestroy()
    {
        if (maskMaterial != null)
            Destroy(maskMaterial);
    }

    private void OnDisable()
    {
        isHeld = false;
    }

    private void Update()
    {
        if (!isOn || lightRoot == null)
            return;

        float k = 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime);
        lightRoot.anchoredPosition = Vector2.Lerp(lightRoot.anchoredPosition, targetPosition, k);

        PushHoleToMask();
    }

    /// <summary>
    /// Tells the dark sheet where its hole is. Positions go across in the sheet's own UV
    /// space, and the radius is measured against its height, which is the axis the shader
    /// corrects the other one against.
    /// </summary>
    private void PushHoleToMask()
    {
        if (maskMaterial == null || darknessMask == null)
            return;

        Rect rect = darknessMask.rectTransform.rect;
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        // The light lives under this object; the mask may sit elsewhere in the panel, so
        // the position is carried across in world space rather than assumed to match.
        Vector3 worldPoint = lightRoot.position;
        Vector2 local = darknessMask.rectTransform.InverseTransformPoint(worldPoint);

        Vector2 uv = new Vector2(
            Mathf.InverseLerp(rect.xMin, rect.xMax, local.x),
            Mathf.InverseLerp(rect.yMin, rect.yMax, local.y));

        maskMaterial.SetVector(LightPosId, new Vector4(uv.x, uv.y, 0f, 0f));
        maskMaterial.SetFloat(RadiusId, lightRadius / rect.height);
        maskMaterial.SetFloat(SoftId, Mathf.Max(0.001f, edgeSoftness / rect.height));
        maskMaterial.SetFloat(AspectId, rect.width / rect.height);
        maskMaterial.SetFloat(SquareId, squareLight ? 1f : 0f);
    }

    /// <summary>
    /// Switches the beam on at the middle of the screen, or puts it away entirely.
    /// Off is the state before the player has clicked the flashlight.
    /// </summary>
    public void SetOn(bool on)
    {
        isOn = on;
        isHeld = false;

        if (lightRoot != null)
        {
            lightRoot.gameObject.SetActive(on);
            // Opens in the middle, the way clicking a torch on points it straight ahead
            lightRoot.anchoredPosition = Vector2.zero;
            targetPosition = Vector2.zero;
        }

        if (selfRect != null)
        {
            Image surface = GetComponent<Image>();
            if (surface != null)
                surface.raycastTarget = on;
        }

        // Off means a sealed room: the hole is parked outside the sheet entirely, rather
        // than shrunk to nothing at the middle where it would still show a speck.
        if (maskMaterial != null)
        {
            if (on)
                PushHoleToMask();
            else
                maskMaterial.SetVector(LightPosId, new Vector4(-10f, -10f, 0f, 0f));
        }

        SetFocus(0f);
    }

    /// <summary>
    /// Shows how far along the current person's hold is, 0-1. Drives the yellow and the
    /// ring together so the beam reads the same as the person it is on.
    /// </summary>
    public void SetFocus(float t)
    {
        t = Mathf.Clamp01(t);

        if (focus != null)
            focus.rectTransform.localScale = Vector3.one * Mathf.Lerp(focusMinScale, 1f, t);

        if (ring != null)
        {
            Color c = ring.color;
            ring.color = new Color(c.r, c.g, c.b, t);
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!isOn)
            return;

        isHeld = true;
        MoveToPointer(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isOn || !isHeld)
            return;

        MoveToPointer(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isHeld = false;
    }

    /// <summary>
    /// Turns the screen position of the finger into a spot in the panel's own space, so
    /// the light lands under the finger at any resolution or canvas scale.
    /// </summary>
    private void MoveToPointer(PointerEventData eventData)
    {
        if (selfRect == null)
            return;

        Camera cam = null;
        if (parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = parentCanvas.worldCamera;

        Vector2 local;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(selfRect, eventData.position, cam, out local))
            targetPosition = local;
    }
}

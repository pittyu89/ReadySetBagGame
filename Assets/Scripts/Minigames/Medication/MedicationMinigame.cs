using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The medication minigame that runs after the quiz's medication question.
///
/// Fifteen pills sit in a holder — five blue, five red, five green. The player drags each
/// one into the container whose lid matches its colour: blue to Day 1, red to Day 2, green
/// to Day 3.
///
/// A pill let go anywhere but over the holder is handed to gravity and never travels back on
/// its own. The containers are solid boxes with an open top: released over an opening it
/// falls in, right colour or wrong; released clear of all three it drops to the shelf, and
/// once there it cannot slide in through a side. Nothing comes to rest on top of a container
/// — a pill that lands on a lid, or bridges the narrow gap between two of them, slides off
/// into the nearest opening. Every pill stays draggable for the whole round, so anything that
/// landed badly can be picked up and tried again.
///
/// Like the other minigames there is nothing to fail. The round waits until all fifteen are
/// lying in the container that matches them, rather than grading them, because the question
/// has already been scored by the time this runs.
///
/// None of the geometry is authored by hand. The containers' footprints, the glass interior
/// a pill comes to rest in and the shelf underneath are all read off the PillContainers
/// sprite, so they line up with what is drawn however the sprite is scaled.
///
/// <see cref="Play"/> is a coroutine so QuizManager can simply yield on it and carry on with
/// the next question once it returns.
/// </summary>
public partial class MedicationMinigame : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private CanvasGroup panelGroup;
    [Tooltip("Blurs the quiz behind the minigame, the same way the correct / wrong overlay " +
             "does. Optional.")]
    [SerializeField] private ScreenBlurBackdrop backdrop;

    [Header("Instruction")]
    [SerializeField] private CanvasGroup instructionCard;
    [SerializeField] private TextMeshProUGUI instructionLabel;
    [SerializeField, TextArea] private string instructionText =
        "Drag each pill into the day with the matching lid colour";

    [Header("Board")]
    [Tooltip("Everything is positioned inside this rect, and pills are dragged in its space.")]
    [SerializeField] private RectTransform board;
    [Tooltip("The holder the pills start in.")]
    [SerializeField] private RectTransform holder;
    [Tooltip("The three trays, drawn as one sprite.")]
    [SerializeField] private RectTransform trays;
    [Tooltip("Pills are parented here. It sits between the holder and the containers, so a " +
             "pill draws in front of the dish but behind the glass it has fallen into. The " +
             "container graphics have raycastTarget off, so a pill behind them can still be " +
             "picked up.")]
    [SerializeField] private RectTransform pillLayer;

    [Header("Pills")]
    [SerializeField] private Sprite bluePill;
    [SerializeField] private Sprite redPill;
    [SerializeField] private Sprite greenPill;
    [Tooltip("How many of each colour. Five each makes the fifteen the design asks for.")]
    [SerializeField] private int pillsPerColour = 5;
    [Tooltip("Width and height given to each pill's rect. The art sits inside it.")]
    [SerializeField] private float pillSize = 74f;
    [Tooltip("How far from the holder's centre the pills are scattered, as a fraction of its radius.")]
    [SerializeField, Range(0.1f, 1f)] private float scatterRadius = 0.55f;

    [Header("Physics")]
    [Tooltip("Pull on a dropped pill, in units per second squared. Pills fall into the tray " +
             "and pile up on each other rather than being placed in tidy rows.")]
    [SerializeField] private float gravity = 3200f;
    [Tooltip("How much of its speed a pill keeps when it hits the tray floor or a wall.")]
    [SerializeField, Range(0f, 0.8f)] private float bounce = 0.32f;
    [Tooltip("Speed scrubbed off sideways and spin-wise on every contact.")]
    [SerializeField, Range(0.5f, 1f)] private float friction = 0.78f;
    [Tooltip("Length of the drawn pill along its own long axis, as a fraction of pillSize. " +
             "Measured off the art: the capsule fills 28 of the sprite's 32 rows.")]
    [SerializeField, Range(0.3f, 1f)] private float pillLengthFraction = 0.88f;
    [Tooltip("Thickness of the drawn pill across its short axis, as a fraction of pillSize. " +
             "The art is 12 of 32 columns wide.")]
    [SerializeField, Range(0.1f, 0.8f)] private float pillThicknessFraction = 0.38f;
    [Tooltip("Below this speed a resting pill stops being simulated.")]
    [SerializeField] private float sleepSpeed = 12f;
    [Tooltip("How fast a pill that landed on its end topples over onto its side, in degrees " +
             "per second. A capsule cannot balance upright, so nothing is allowed to settle " +
             "at an angle.")]
    [SerializeField] private float rightingSpeed = 520f;
    [Tooltip("How fast a pill that landed on a lid slides off it towards the nearest opening. " +
             "Nothing is allowed to come to rest on top of a container.")]
    [SerializeField] private float lidSlideSpeed = 260f;

    [Header("Timing")]
    [SerializeField] private float panelFadeDuration = 0.25f;
    [SerializeField] private float instructionFadeDuration = 0.3f;
    [Tooltip("Pause once the last pill is sorted, before the minigame closes.")]
    [SerializeField] private float finishDelay = 0.9f;

    [Header("Sound")]
    [Tooltip("As a falling pill hits the floor of a container or the shelf.")]
    [SerializeField] private AudioClip pillLandSFX;
    [Tooltip("Slowest landing that still clinks, in canvas units a second. Keeps the pile " +
             "from chattering as it settles.")]
    [SerializeField] private float pillLandMinSpeed = 350f;
    [Tooltip("Landing speed that plays the clink at full volume.")]
    [SerializeField] private float pillLandFullSpeed = 1400f;

    [Header("Finish")]
    [Tooltip("Optional. Flashed once every pill is sorted.")]
    [SerializeField] private GameObject completedBanner;

    // Everything below is read off the 320x64 PillContainers sprite, so the physics lines up
    // with the containers that are actually drawn however the sprite is scaled.
    private const float SHEET_WIDTH = 320f;
    private const float SHEET_HEIGHT = 64f;

    /// <summary>Outer footprint of each container: a pill let go over this falls into it.</summary>
    private static readonly Vector2[] TRAY_SPANS =
    {
        new Vector2(37f, 118f),    // blue  - day 1
        new Vector2(122f, 203f),   // red   - day 2
        new Vector2(207f, 288f)    // green - day 3
    };

    /// <summary>Inside the glass, between the frame's two side walls. Pills rest in here.</summary>
    private static readonly Vector2[] TRAY_INNER_SPANS =
    {
        new Vector2(39f, 117f),
        new Vector2(123f, 202f),
        new Vector2(208f, 287f)
    };

    // Sprite rows, counted from the bottom. 16-18 is the frame's base, the glass runs from 19
    // up to the rim at 45, and the coloured lid sits on 46-48. A pill resting on row 19 is
    // sitting on the container's floor, which is the whole point.
    private const float INTERIOR_FLOOR_ROW = 19f;
    private const float INTERIOR_RIM_ROW = 45f;

    /// <summary>Top of the coloured lid — the surface a pill that missed the opening sits on.</summary>
    private const float CONTAINER_TOP_ROW = 48f;

    /// <summary>Top of the dark shelf the containers stand on — the floor for a stray pill.</summary>
    private const float SHELF_TOP_ROW = 16f;

    private bool isPlaying = false;

    private readonly List<DraggablePill> pills = new List<DraggablePill>();

    /// <summary>The glass interior of each container: where a pill that fell in comes to rest.</summary>
    private readonly Rect[] dropZones = new Rect[3];

    /// <summary>
    /// Outer footprint of each container in board space, as min/max x. A container is solid
    /// out to here: a pill can only get inside by being dropped over the opening.
    /// </summary>
    private readonly Vector2[] solidSpans = new Vector2[3];

    /// <summary>Top of the containers — the lid a pill lands on if it misses the opening.</summary>
    private float containerTopY;

    /// <summary>Shelf top, and the board's left and right edges, for pills that missed.</summary>
    private float groundY;
    private float boardMinX;
    private float boardMaxX;

    /// <summary>A pill that has been let go and is falling or settled.</summary>
    private class PillBody
    {
        public DraggablePill Pill;
        public Vector2 Velocity;
        public float Spin;

        /// <summary>
        /// Rotation in degrees, held here rather than read back off the transform so the step
        /// works from one authority. The art's long axis is the sprite's local +Y, so 90 and
        /// 270 are the angles a pill lies flat at.
        /// </summary>
        public float Angle;

        /// <summary>Container it fell into, or -1 for one lying loose on the shelf.</summary>
        public int Zone;

        public bool Asleep;

        /// <summary>Touching the floor, a wall or another pill this step — so it can topple.</summary>
        public bool Grounded;

        /// <summary>
        /// How long this pill has been barely moving. Sleeping on floor contact alone would
        /// never quiet a pill resting on top of another one, and sleeping the instant speed
        /// drops would freeze one at the top of a bounce — so it has to stay slow for a
        /// moment before it counts as settled.
        /// </summary>
        public float QuietTime;
    }

    private readonly List<PillBody> bodies = new List<PillBody>();

    public bool IsPlaying { get { return isPlaying; } }

    private void Awake()
    {
        if (panelGroup == null && panelRoot != null)
            panelGroup = panelRoot.GetComponent<CanvasGroup>();

        if (instructionLabel != null && !string.IsNullOrEmpty(instructionText))
            instructionLabel.text = instructionText;

        // Reset the contents but leave the panel's active state alone. Awake first runs
        // during the SetActive in Open, so deactivating here would switch the panel back off
        // underneath the very coroutine that just turned it on.
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

        if (board == null || holder == null || trays == null || pillLayer == null
            || bluePill == null || redPill == null || greenPill == null)
        {
            // Bail loudly rather than quietly: a half-wired panel would otherwise hang the
            // quiz on pills that can never be sorted.
            Debug.LogWarning("[MedicationMinigame] Needs the board, holder, trays, pill layer " +
                             "and all three pill sprites — skipping.", this);
            yield break;
        }

        isPlaying = true;

        Open();

        if (backdrop != null)
            yield return StartCoroutine(backdrop.CaptureIncludingUIRoutine());

        yield return FadeGroup(panelGroup, 0f, 1f, panelFadeDuration);
        yield return FadeGroup(instructionCard, 0f, 1f, instructionFadeDuration);

        foreach (DraggablePill p in pills)
            p.SetArmed(true);

        // Waits on the board itself rather than a running tally, so pulling a pill back out
        // of a container un-counts it exactly the way putting it in counted it.
        while (SortedCount() < pills.Count)
            yield return null;

        foreach (DraggablePill p in pills)
            p.SetArmed(false);

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

    // ------------------------------------------------------------------ sorting

    private void OnPickedUp(DraggablePill pill)
    {
        // Any pill can be picked up again, including one already lying in a container, so
        // that a mis-drop can be fixed. Lifting it takes it back out of the simulation.
        for (int i = bodies.Count - 1; i >= 0; i--)
            if (bodies[i].Pill == pill)
                bodies.RemoveAt(i);

        // Whatever is being carried draws above the rest
        pill.transform.SetAsLastSibling();
        pill.Rect.localRotation = Quaternion.identity;
    }

    private void OnDropped(DraggablePill pill)
    {
        Vector2 p = pill.Rect.anchoredPosition;

        // Let go over the holder and it simply stays there, the way it started. Nothing ever
        // travels back to the holder on its own.
        if (IsOverHolder(p))
            return;

        // Anywhere else, gravity has it. Which container it belongs to is decided here, from
        // where it was released: over a container and it falls in, right colour or wrong;
        // clear of all three and it drops to the shelf and lies there until picked up again.
        bodies.Add(new PillBody
        {
            Pill = pill,
            Zone = CatchingZone(p.x),
            Angle = pill.Rect.localRotation.eulerAngles.z,
            Velocity = new Vector2(Random.Range(-40f, 40f), -120f),
            Spin = Random.Range(-220f, 220f)
        });
    }

    /// <summary>
    /// Which container's opening this x drops cleanly through, or -1 for none. The opening,
    /// not the whole container, and inset by how wide a pill can be, so one let go half over
    /// a rim is handled by the rim rather than snapped inside it. The inset uses a pill stood
    /// on its end — its widest possible turn — because a falling pill is still tumbling and
    /// there is no telling which way it will be facing by the time it arrives.
    /// </summary>
    private int CatchingZone(float x)
    {
        float widest = PillHalfLength + PillRadius;

        for (int i = 0; i < dropZones.Length; i++)
            if (x >= dropZones[i].xMin + widest && x <= dropZones[i].xMax - widest)
                return i;

        return -1;
    }

    /// <summary>
    /// The holder is drawn as a round dish, so a square test would leave a pill hanging in
    /// the empty corners of its rect.
    /// </summary>
    private bool IsOverHolder(Vector2 point)
    {
        if (holder == null)
            return false;

        float r = Mathf.Min(holder.rect.width, holder.rect.height) * 0.5f;

        return ((Vector2)holder.anchoredPosition - point).sqrMagnitude <= r * r;
    }

    /// <summary>How many pills are lying in the container that matches their colour.</summary>
    private int SortedCount()
    {
        int n = 0;
        for (int i = 0; i < bodies.Count; i++)
            if (bodies[i].Pill != null && bodies[i].Zone == bodies[i].Pill.ColourIndex)
                n++;

        return n;
    }

    private Quaternion RandomTilt()
    {
        return Quaternion.Euler(0f, 0f, Random.Range(-75f, 75f));
    }

    private Vector2 RandomHolderPoint()
    {
        float radius = Mathf.Min(holder.rect.width, holder.rect.height) * 0.5f * scatterRadius;
        Vector2 offset = Random.insideUnitCircle * radius;
        return (Vector2)holder.anchoredPosition + offset;
    }

    // ------------------------------------------------------------------ setup

    /// <summary>
    /// Works out where the three trays sit, in board space, from the sprite's own geometry.
    /// </summary>
    private void BuildDropZones()
    {
        float w = trays.rect.width;
        float h = trays.rect.height;
        float left = trays.anchoredPosition.x - w * trays.pivot.x;
        float bottom = trays.anchoredPosition.y - h * trays.pivot.y;

        // The glass interior, not the container's outer box. Using the outer box put the
        // floor down among the frame and the shelf, so a pill landed a good twenty units
        // below the surface it is drawn standing on.
        float y0 = bottom + (INTERIOR_FLOOR_ROW / SHEET_HEIGHT) * h;
        float y1 = bottom + (INTERIOR_RIM_ROW / SHEET_HEIGHT) * h;

        for (int i = 0; i < TRAY_SPANS.Length; i++)
        {
            float inner0 = left + (TRAY_INNER_SPANS[i].x / SHEET_WIDTH) * w;
            float inner1 = left + (TRAY_INNER_SPANS[i].y / SHEET_WIDTH) * w;
            dropZones[i] = new Rect(inner0, y0, inner1 - inner0, y1 - y0);

            solidSpans[i] = new Vector2(
                left + (TRAY_SPANS[i].x / SHEET_WIDTH) * w,
                left + (TRAY_SPANS[i].y / SHEET_WIDTH) * w);
        }

        containerTopY = bottom + (CONTAINER_TOP_ROW / SHEET_HEIGHT) * h;
        groundY = bottom + (SHELF_TOP_ROW / SHEET_HEIGHT) * h;

        boardMinX = board.rect.xMin;
        boardMaxX = board.rect.xMax;
    }

    private void BuildPills()
    {
        foreach (DraggablePill p in pills)
            if (p != null)
                DestroyPill(p.gameObject);
        pills.Clear();
        bodies.Clear();

        Sprite[] sprites = { bluePill, redPill, greenPill };
        Canvas owning = panelRoot != null ? panelRoot.GetComponentInParent<Canvas>() : null;

        for (int colour = 0; colour < 3; colour++)
        {
            for (int n = 0; n < pillsPerColour; n++)
            {
                GameObject go = new GameObject("Pill_" + colour + "_" + n, typeof(RectTransform));
                go.layer = pillLayer.gameObject.layer;

                RectTransform rt = (RectTransform)go.transform;
                rt.SetParent(pillLayer, false);
                rt.localScale = Vector3.one;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(pillSize, pillSize);
                rt.anchoredPosition = RandomHolderPoint();
                rt.localRotation = RandomTilt();

                Image img = go.AddComponent<Image>();
                img.sprite = sprites[colour];
                img.raycastTarget = true;

                DraggablePill pill = go.AddComponent<DraggablePill>();
                pill.Configure(colour, board, owning);
                pill.SetArmed(false);
                pill.PickedUp += OnPickedUp;
                pill.Dropped += OnDropped;

                pills.Add(pill);
            }
        }
    }

    private static void DestroyPill(GameObject go)
    {
        // Editor tooling builds the board outside play mode, where Destroy is refused
        if (Application.isPlaying)
            Destroy(go);
        else
            DestroyImmediate(go);
    }

    // ------------------------------------------------------------------ lifecycle

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

        if (completedBanner != null)
            completedBanner.SetActive(false);

        // Sizes are only real once the layout has been built, so the zones are worked out
        // here rather than in Awake.
        Canvas.ForceUpdateCanvases();
        BuildDropZones();
        BuildPills();
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
    /// Silences and blanks everything the panel owns, without touching whether the panel
    /// itself is on — see the note in Awake.
    /// </summary>
    private void ResetVisuals()
    {
        foreach (DraggablePill p in pills)
            if (p != null)
                DestroyPill(p.gameObject);
        pills.Clear();
        bodies.Clear();

        if (completedBanner != null)
            completedBanner.SetActive(false);

        if (panelGroup == null)
            return;

        panelGroup.alpha = 0f;
        panelGroup.blocksRaycasts = false;
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

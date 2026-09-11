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
/// <see cref="Play"/> is a coroutine so QuizHandler can simply yield on it and carry on with
/// the next question once it returns.
/// </summary>
public class MedicationMinigame : MonoBehaviour
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
        "Drag & Drop the pills to sort the right medication to the right day";

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

    // ------------------------------------------------------------------ pill shape

    /// <summary>Radius of the capsule's rounded ends — half the drawn pill's thickness.</summary>
    private float PillRadius { get { return pillSize * pillThicknessFraction * 0.5f; } }

    /// <summary>
    /// Centre to either end circle. A capsule is those two circles plus the band between
    /// them, so this is half the length less one radius at each end.
    /// </summary>
    private float PillHalfLength
    {
        get { return Mathf.Max(0f, (pillSize * pillLengthFraction - pillSize * pillThicknessFraction) * 0.5f); }
    }

    /// <summary>Unit vector along the pill's long axis at the given rotation.</summary>
    private static Vector2 LongAxis(float angleDegrees)
    {
        float rad = angleDegrees * Mathf.Deg2Rad;
        return new Vector2(-Mathf.Sin(rad), Mathf.Cos(rad));
    }

    /// <summary>
    /// How far the capsule reaches from its centre along each axis. Exact, not a bounding
    /// box guess: the furthest point of a capsule is always the far side of an end circle.
    /// </summary>
    private Vector2 PillExtent(float angleDegrees)
    {
        Vector2 axis = LongAxis(angleDegrees);
        float a = PillHalfLength;
        float r = PillRadius;

        return new Vector2(Mathf.Abs(axis.x) * a + r, Mathf.Abs(axis.y) * a + r);
    }

    /// <summary>The nearest rotation at which the pill is lying on its side.</summary>
    private static float NearestFlatAngle(float angleDegrees)
    {
        return Mathf.Round((angleDegrees - 90f) / 180f) * 180f + 90f;
    }

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

    /// <summary>
    /// Gravity, tray walls and pill-on-pill contact for everything already sorted. Unscaled
    /// time, so the pile keeps falling at the same rate whatever the game's timescale is
    /// doing behind the blur.
    /// </summary>
    private void Update()
    {
        if (!isPlaying || bodies.Count == 0)
            return;

        // Clamped so a hitch cannot tunnel a pill through the tray floor
        StepPhysics(Mathf.Min(Time.unscaledDeltaTime, 1f / 30f));
    }

    /// <summary>
    /// One step of the pile. Split out from Update so it can be driven at a fixed rate from
    /// a test rather than at whatever the editor's frame time happens to be.
    /// </summary>
    private void StepPhysics(float dt)
    {
        for (int i = 0; i < bodies.Count; i++)
        {
            PillBody b = bodies[i];
            if (b.Asleep || b.Pill == null)
                continue;

            // Grounded is deliberately NOT cleared here. Pill-on-pill contacts are found after
            // this loop, so a pill resting on the pile rather than on the floor only learns it
            // is supported at the end of the previous step; clearing the flag on the way in
            // would throw that away and such a pill could never topple flat.

            // Inside a container it is held by the glass; loose on the shelf its only limits
            // are the shelf top and the edges of the board.
            float floorY, wallMinX, wallMaxX;
            if (b.Zone >= 0)
            {
                Rect tray = dropZones[b.Zone];
                floorY = tray.yMin;
                wallMinX = tray.xMin;
                wallMaxX = tray.xMax;
            }
            else
            {
                floorY = groundY;
                wallMinX = boardMinX;
                wallMaxX = boardMaxX;
            }

            Vector2 p = b.Pill.Rect.anchoredPosition;

            b.Velocity.y -= gravity * dt;
            p += b.Velocity * dt;
            b.Angle += b.Spin * dt;

            // How far the capsule reaches at this rotation. Lying flat it is long and low;
            // stood on end it is narrow and tall. Using the real extent is what stops a pill
            // hanging half out through the side of the glass.
            Vector2 extent = PillExtent(b.Angle);

            // floor
            if (p.y - extent.y < floorY)
            {
                p.y = floorY + extent.y;
                b.Grounded = true;

                // A resting pill still gains gravity*dt of downward speed every step, and
                // bouncing that back is a jitter that never dies out. Below that threshold
                // the pill is lying on the tray, not landing on it.
                if (-b.Velocity.y > gravity * dt * 2f)
                    b.Velocity.y = -b.Velocity.y * bounce;
                else
                    b.Velocity.y = 0f;

                b.Velocity.x *= friction;
            }

            // walls
            if (p.x - extent.x < wallMinX)
            {
                p.x = wallMinX + extent.x;
                b.Velocity.x = -b.Velocity.x * bounce;
                b.Grounded = true;
            }
            else if (p.x + extent.x > wallMaxX)
            {
                p.x = wallMaxX - extent.x;
                b.Velocity.x = -b.Velocity.x * bounce;
                b.Grounded = true;
            }

            // The containers themselves are solid to anything outside them
            if (b.Zone < 0)
                BlockAgainstContainers(b, ref p, extent, dt);

            Topple(b, dt);
            b.Grounded = false;

            b.Pill.Rect.anchoredPosition = p;
            b.Pill.Rect.localRotation = Quaternion.Euler(0f, 0f, b.Angle);
        }

        ResolvePillContacts(dt);
        UpdateSleep(dt);
    }

    /// <summary>
    /// Decides what has come to rest. Runs after the contacts, not inside the step loop: a
    /// pill lying on the pile has just had a step's worth of gravity added and only the
    /// contact pass takes it away again, so judging it any earlier sees a pill that is always
    /// falling and it would never go quiet.
    /// </summary>
    private void UpdateSleep(float dt)
    {
        for (int i = 0; i < bodies.Count; i++)
        {
            PillBody b = bodies[i];
            if (b.Asleep || b.Pill == null)
                continue;

            // A pill still mid-topple is not settled however slowly it is drifting, or it
            // would freeze standing on its end.
            bool flat = Mathf.Abs(Mathf.DeltaAngle(b.Angle, NearestFlatAngle(b.Angle))) < 1.5f;

            if (b.Velocity.magnitude < sleepSpeed && Mathf.Abs(b.Spin) < 30f && flat)
                b.QuietTime += dt;
            else
                b.QuietTime = 0f;

            if (b.QuietTime < 0.15f)
                continue;

            b.Velocity = Vector2.zero;
            b.Spin = 0f;
            b.Angle = NearestFlatAngle(b.Angle);
            b.Pill.Rect.localRotation = Quaternion.Euler(0f, 0f, b.Angle);
            b.Asleep = true;
        }
    }

    /// <summary>
    /// Tips a pill that is touching something onto its side.
    ///
    /// A capsule standing on its end has its weight over a contact patch one radius wide, so
    /// it falls over — there is no angle but flat that it can rest at. Rather than carry real
    /// angular dynamics for that, a grounded pill is turned towards the nearest flat angle and
    /// its spin is damped; in the air it keeps tumbling freely.
    /// </summary>
    private void Topple(PillBody b, float dt)
    {
        if (!b.Grounded)
            return;

        b.Spin *= friction;
        b.Angle = Mathf.MoveTowardsAngle(b.Angle, NearestFlatAngle(b.Angle), rightingSpeed * dt);
    }

    /// <summary>
    /// Keeps a loose pill out of the containers. Each one is a solid box from the shelf up to
    /// its lid, so a pill that missed the opening lands on top of it, and a pill lying on the
    /// shelf cannot slide in through the side.
    ///
    /// The pill is pushed out along whichever axis it is least deep on — the same way a
    /// circle-versus-box contact is normally resolved — so a pill that came down onto the lid
    /// is stood on top of it rather than flicked out sideways.
    /// </summary>
    private void BlockAgainstContainers(PillBody b, ref Vector2 p, Vector2 extent, float dt)
    {
        float top = containerTopY + extent.y;

        for (int z = 0; z < solidSpans.Length; z++)
        {
            float lo = solidSpans[z].x - extent.x;
            float hi = solidSpans[z].y + extent.x;

            if (p.x <= lo || p.x >= hi || p.y > top)
                continue;

            // The opening is a hole, not a surface, so it is not solid at all — but the pill
            // only clears a hole once it is far enough in to miss both rims. That is what
            // lets one teeter on a rim instead of sinking through the edge of it.
            if (p.x >= dropZones[z].xMin + extent.x && p.x <= dropZones[z].xMax - extent.x)
            {
                if (p.y <= containerTopY)
                {
                    b.Zone = z;
                    b.QuietTime = 0f;
                }

                continue;
            }

            float outLeft = p.x - lo;
            float outRight = hi - p.x;
            float outTop = top - p.y;

            if (outTop <= outLeft && outTop <= outRight)
            {
                p.y = top;

                if (-b.Velocity.y > gravity * dt * 2f)
                    b.Velocity.y = -b.Velocity.y * bounce;
                else
                    b.Velocity.y = 0f;

                b.Grounded = true;
                TipTowardsOpening(b, p, extent.x);
            }
            else if (outLeft < outRight)
            {
                p.x = lo;
                b.Velocity.x = -Mathf.Abs(b.Velocity.x) * bounce;
                b.Grounded = true;
            }
            else
            {
                p.x = hi;
                b.Velocity.x = Mathf.Abs(b.Velocity.x) * bounce;
                b.Grounded = true;
            }
        }
    }

    /// <summary>
    /// Sends a pill that came down on a lid sliding towards the nearest opening, so it drops
    /// in rather than perching on top.
    ///
    /// The gaps between the containers are sixteen units across and a pill is forty, so a pill
    /// released over one cannot fall down it — it lands bridging the two lids and, left alone,
    /// hangs there in mid-air over the gap. Tipping it off solves that and the pill balanced
    /// on a rim at the same time: nothing is allowed to come to rest on top of a container.
    /// </summary>
    private void TipTowardsOpening(PillBody b, Vector2 p, float radius)
    {
        int nearest = -1;
        float best = float.MaxValue;

        for (int z = 0; z < dropZones.Length; z++)
        {
            float lo = dropZones[z].xMin + radius;
            float hi = dropZones[z].xMax - radius;
            float distance = p.x < lo ? lo - p.x : (p.x > hi ? p.x - hi : 0f);

            if (distance < best)
            {
                best = distance;
                nearest = z;
            }
        }

        if (nearest < 0)
            return;

        b.Velocity.x = lidSlideSpeed * Mathf.Sign(dropZones[nearest].center.x - p.x);

        // It is moving, not settling, however slowly the slide reads
        b.QuietTime = 0f;
    }

    /// <summary>
    /// The closest pair of points on the two pills' centre lines — the standard segment to
    /// segment solve, done exactly rather than by sampling. Sampling was not good enough
    /// here: settled pills lie parallel and side by side, and unless the sample points happen
    /// to line up it reads their separation as larger than it is, leaves them a couple of
    /// units inside each other, and the pile never stops twitching.
    /// </summary>
    private void ClosestPointsOnAxes(Vector2 pa, float angleA, Vector2 pb, float angleB,
                                     out Vector2 ca, out Vector2 cb)
    {
        const float epsilon = 0.0001f;
        float half = PillHalfLength;

        Vector2 startA = pa - LongAxis(angleA) * half;
        Vector2 startB = pb - LongAxis(angleB) * half;
        Vector2 dirA = LongAxis(angleA) * (half * 2f);
        Vector2 dirB = LongAxis(angleB) * (half * 2f);
        Vector2 between = startA - startB;

        float lenA = Vector2.Dot(dirA, dirA);
        float lenB = Vector2.Dot(dirB, dirB);
        float projB = Vector2.Dot(dirB, between);

        float s, t;

        if (lenA <= epsilon && lenB <= epsilon)
        {
            // Both degenerate — two points
            ca = startA;
            cb = startB;
            return;
        }

        if (lenA <= epsilon)
        {
            s = 0f;
            t = Mathf.Clamp01(projB / lenB);
        }
        else
        {
            float projA = Vector2.Dot(dirA, between);

            if (lenB <= epsilon)
            {
                t = 0f;
                s = Mathf.Clamp01(-projA / lenA);
            }
            else
            {
                float dot = Vector2.Dot(dirA, dirB);
                float denominator = lenA * lenB - dot * dot;

                // Parallel lines leave the pair undetermined along their length; any point
                // does, so take an end and project it across.
                s = denominator != 0f
                    ? Mathf.Clamp01((dot * projB - projA * lenB) / denominator)
                    : 0f;

                t = (dot * s + projB) / lenB;

                if (t < 0f)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-projA / lenA);
                }
                else if (t > 1f)
                {
                    t = 1f;
                    s = Mathf.Clamp01((dot - projA) / lenA);
                }
            }
        }

        ca = startA + dirA * s;
        cb = startB + dirB * t;
    }

    /// <summary>
    /// Pushes overlapping pills in the same tray apart, so they stack instead of sharing a
    /// spot. A couple of passes is plenty for five pills in a tray.
    ///
    /// A pill is a capsule, not a ball. Two capsules touch when the two line segments down
    /// their middles come within a diameter of each other, so the contact is found on those
    /// segments — a single circle would let the ends of a pill sink into its neighbour, which
    /// is what made a settled tray look like a heap of half-overlapping pills.
    /// </summary>
    private void ResolvePillContacts(float dt)
    {
        // A pill can rest on two others at once, so one pass is not enough for the pile to
        // agree with itself; five settles a tray of five reliably.
        const int passes = 5;

        // Settled pills rest touching, and gravity keeps pressing them together by a hair
        // every step. Separating them is always right, but treating that hair as a shove
        // would wake the whole pile every frame and it would never go quiet — so only a
        // real overlap counts as being disturbed.
        const float wakeOverlap = 2f;

        float radius = PillRadius;
        float minDist = radius * 2f;

        for (int pass = 0; pass < passes; pass++)
        {
            for (int i = 0; i < bodies.Count; i++)
            {
                for (int j = i + 1; j < bodies.Count; j++)
                {
                    PillBody a = bodies[i], b = bodies[j];
                    if (a.Zone != b.Zone || a.Pill == null || b.Pill == null)
                        continue;

                    Vector2 pa = a.Pill.Rect.anchoredPosition;
                    Vector2 pb = b.Pill.Rect.anchoredPosition;

                    Vector2 ca, cb;
                    ClosestPointsOnAxes(pa, a.Angle, pb, b.Angle, out ca, out cb);

                    Vector2 d = cb - ca;
                    float dist = d.magnitude;

                    if (dist >= minDist)
                        continue;

                    // Two pills lying along the same line have centre lines that cross, so the
                    // closest points coincide and there is no direction to push along. Fall
                    // back to the line between their centres, and to sideways if even that is
                    // degenerate — otherwise a pair like this sits inside each other forever.
                    if (dist < 0.0001f)
                    {
                        d = pb - pa;
                        dist = d.magnitude;

                        if (dist < 0.0001f)
                        {
                            d = Vector2.right;
                            dist = 1f;
                        }
                    }

                    float overlap = minDist - dist;

                    Vector2 push = d / dist * (overlap * 0.5f);

                    // Shoving them apart must not shove them out through the glass, so the
                    // same bounds the step uses are re-applied here.
                    Vector2 ea = PillExtent(a.Angle), eb = PillExtent(b.Angle);
                    float loX, hiX, minY;
                    if (a.Zone >= 0)
                    {
                        Rect tray = dropZones[a.Zone];
                        loX = tray.xMin;
                        hiX = tray.xMax;
                        minY = tray.yMin;
                    }
                    else
                    {
                        loX = boardMinX;
                        hiX = boardMaxX;
                        minY = groundY;
                    }

                    pa -= push;
                    pb += push;
                    a.Grounded = true;
                    b.Grounded = true;
                    pa.x = Mathf.Clamp(pa.x, loX + ea.x, hiX - ea.x);
                    pb.x = Mathf.Clamp(pb.x, loX + eb.x, hiX - eb.x);
                    pa.y = Mathf.Max(pa.y, minY + ea.y);
                    pb.y = Mathf.Max(pb.y, minY + eb.y);

                    a.Pill.Rect.anchoredPosition = pa;
                    b.Pill.Rect.anchoredPosition = pb;

                    // Separating them positionally is not enough: a pill landing on the pile
                    // keeps its downward speed, so it presses in again next step and never
                    // settles. Cancel the speed the two are closing at, and what is left is a
                    // pill lying on a pill.
                    Vector2 n = d / dist;
                    float closing = Vector2.Dot(b.Velocity - a.Velocity, n);
                    if (closing < 0f)
                    {
                        // A pill lying on another is not landing on it, it is resting on it:
                        // all it has is the step of gravity just added. Sharing that out
                        // leaves both of them still drifting together, so neither is ever
                        // still enough to settle and the pile hums along for ever. Below the
                        // same threshold the floor uses, take the normal component off both
                        // and the stack genuinely stops.
                        if (-closing <= gravity * dt * 2f)
                        {
                            a.Velocity -= n * Vector2.Dot(a.Velocity, n);
                            b.Velocity -= n * Vector2.Dot(b.Velocity, n);
                        }
                        else
                        {
                            // A real impact. A settled pill is part of the furniture and takes
                            // none of it; the arriving pill takes the lot.
                            float shareA = a.Asleep ? 0f : (b.Asleep ? 1f : 0.5f);
                            float shareB = b.Asleep ? 0f : (a.Asleep ? 1f : 0.5f);

                            a.Velocity += n * closing * shareA;
                            b.Velocity -= n * closing * shareB;
                        }

                        // Pills grip each other as well as the tray floor. Without this a pill
                        // perched on another's shoulder slides off it for ever, creeping
                        // across the tray a fraction of a unit at a time and never settling.
                        Vector2 tangent = new Vector2(-n.y, n.x);
                        float sliding = Vector2.Dot(b.Velocity - a.Velocity, tangent);
                        float grip = 1f - friction;
                        float gripA = a.Asleep ? 0f : (b.Asleep ? 1f : 0.5f);
                        float gripB = b.Asleep ? 0f : (a.Asleep ? 1f : 0.5f);

                        a.Velocity += tangent * sliding * gripA * grip;
                        b.Velocity -= tangent * sliding * gripB * grip;
                    }

                    // Only a real shove counts as being disturbed; the hair of compression a
                    // resting pile produces every step must not wake it.
                    if (overlap > wakeOverlap)
                    {
                        a.Asleep = false; a.QuietTime = 0f;
                        b.Asleep = false; b.QuietTime = 0f;
                    }
                }
            }
        }
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
}

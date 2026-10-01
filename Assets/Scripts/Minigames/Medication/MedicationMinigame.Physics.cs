using UnityEngine;

/// <summary>
/// The sorted pills' physics: the capsule shape, gravity, the container walls, and pills
/// resting against and toppling off one another.
/// </summary>
public partial class MedicationMinigame
{
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
                {
                    PlayPillLand(-b.Velocity.y);
                    b.Velocity.y = -b.Velocity.y * bounce;
                }
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
    /// Clinks for a pill hitting the floor, louder the harder it lands. The small bounces
    /// after the first stay quiet, so fifteen pills settling don't turn into a rattle.
    /// </summary>
    private void PlayPillLand(float speed)
    {
        if (pillLandSFX == null || speed < pillLandMinSpeed || SoundManager.Instance == null)
            return;

        float loudness = Mathf.InverseLerp(pillLandMinSpeed, pillLandFullSpeed, speed);
        SoundManager.Instance.PlaySFX(pillLandSFX, Mathf.Lerp(0.35f, 1f, loudness));
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
}

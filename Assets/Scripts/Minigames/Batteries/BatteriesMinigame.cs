using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// The batteries minigame that runs after the quiz's batteries question.
///
/// The radio has gone dead and the fix is the one the question is about: swap its batteries.
/// Three beats, in the order a real battery change goes —
///   1. drag the cover off the compartment,
///   2. pull the two worn-out, corroded batteries out,
///   3. drag the two fresh ones in the right way round.
/// Anything taken off or pulled out simply vanishes wherever it is let go, as the reference
/// sheet asks; there is nowhere it has to be put.
///
/// The fresh batteries do not start the right way round, so the last beat is the actual
/// lesson: match the + and − marked inside the compartment. A tap turns a battery over, and one
/// dropped in backwards shakes its head and goes back to where it was.
///
/// Like the other minigames there is nothing to fail. It runs whether the quiz answer was
/// right or wrong, there is no clock, and a wrong drop only means trying again.
///
/// <para>
/// How the artwork lines up. BatteriesSocket and BatteriesPanel are both 128x128 with their
/// pieces drawn in place, so the cover registers over the compartment by sitting at the same
/// size and position as the radio. The battery sheets are 64x64 drawn at the same pixel scale,
/// which makes them exactly half the radio's size. Each piece's own rect is only the drawn
/// object, with the full sheet on a child offset to fit — see <see cref="BatteryPiece"/>.
/// </para>
///
/// <see cref="Play"/> is a coroutine so QuizManager can simply yield on it and carry on with
/// the next question once it returns.
/// </summary>
public class BatteriesMinigame : MonoBehaviour
{
    /// <summary>One of the two channels in the compartment.</summary>
    [Serializable]
    public class BatterySlot
    {
        [Tooltip("Where a battery sits in this channel. A new battery is seated on this " +
                 "rect's centre, and has to be let go over it (plus Drop Padding) to go in.")]
        public RectTransform point;

        [Tooltip("Which way round a new battery has to go in. The batteries alternate the way " +
                 "they do in a real radio: the compartment art marks the left channel ⊕ at the " +
                 "top and the right channel ⊕ at the bottom, so the left slot is + up and the " +
                 "right slot is not.")]
        public bool positiveUp = true;

        [Tooltip("The worn-out battery sitting in this channel when the round starts. It has " +
                 "to come out before a new one will go in.")]
        public BatteryPiece oldBattery;
    }

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private CanvasGroup panelGroup;
    [Tooltip("Blurs the quiz behind the minigame, the same way the correct / wrong overlay " +
             "does. Optional.")]
    [SerializeField] private ScreenBlurBackdrop backdrop;

    [Header("Instruction")]
    [SerializeField] private CanvasGroup instructionCard;
    [SerializeField] private TextMeshProUGUI instructionLabel;
    [SerializeField, TextArea] private string instructionText = "Change the radio's batteries";
    [Tooltip("Swapped in the first time a battery is dropped in backwards, since tapping to " +
             "turn one over is the one move the round never shows. Leave empty to keep the " +
             "instruction as it is.")]
    [SerializeField, TextArea] private string flipHintText = "Tap a battery to flip it around";

    [Header("Board")]
    [Tooltip("Everything is positioned inside this rect, and pieces are dragged in its space.")]
    [SerializeField] private RectTransform board;
    [Tooltip("Where a new battery is parented once it is seated. Has to sit behind the cover " +
             "layer in the hierarchy.")]
    [SerializeField] private RectTransform slotLayer;
    [Tooltip("Whatever is being carried is lifted onto this so it passes over everything else. " +
             "Keep it last on the board.")]
    [SerializeField] private RectTransform dragLayer;

    [Header("Radio")]
    [Tooltip("The plate over the battery compartment. Optional — without one the old " +
             "batteries can be pulled straight out.")]
    [SerializeField] private BatteryPiece cover;
    [Tooltip("The channels in the compartment, left to right.")]
    [SerializeField] private BatterySlot[] slots = new BatterySlot[0];
    [Tooltip("The fresh batteries beside the radio. Needs at least one per slot.")]
    [SerializeField] private BatteryPiece[] newBatteries = new BatteryPiece[0];

    [Header("Feel")]
    [Tooltip("How far the cover or an old battery has to be dragged before letting go throws " +
             "it away. Shorter than this and it drops back into place, so a nudge does not " +
             "count.")]
    [SerializeField] private float removeDistance = 60f;
    [Tooltip("How far outside a slot a new battery can be let go and still go in.")]
    [SerializeField] private float dropPadding = 40f;
    [Tooltip("How long a piece takes to slide into a slot, or back to where it came from.")]
    [SerializeField] private float settleDuration = 0.22f;
    [Tooltip("How long a tap takes to turn a battery over.")]
    [SerializeField] private float flipDuration = 0.16f;
    [Tooltip("How long something thrown away takes to fade out.")]
    [SerializeField] private float vanishDuration = 0.2f;
    [Tooltip("How far it shrinks while it fades, as a fraction of its size.")]
    [SerializeField, Range(0.3f, 1f)] private float vanishScale = 0.85f;
    [Tooltip("How far a battery dropped in backwards, or into a full slot, wobbles on its way " +
             "back, in degrees. Zero for no wobble.")]
    [SerializeField] private float rejectShake = 9f;
    [Tooltip("How long that wobble lasts.")]
    [SerializeField] private float rejectShakeDuration = 0.25f;

    [Header("Timing")]
    [SerializeField] private float panelFadeDuration = 0.25f;
    [SerializeField] private float instructionFadeDuration = 0.3f;
    [Tooltip("Pause once both batteries are in, before the minigame closes.")]
    [SerializeField] private float finishDelay = 0.9f;

    [Header("Finish")]
    [Tooltip("Optional. Flashed once both batteries are in.")]
    [SerializeField] private GameObject completedBanner;

    private bool isPlaying = false;

    private bool homesCaptured = false;

    private readonly List<BatteryPiece> pieces = new List<BatteryPiece>();

    // Per slot: whether its old battery has been thrown away, and which new one has gone in.
    private bool[] cleared = new bool[0];
    private BatteryPiece[] installed = new BatteryPiece[0];

    // New batteries that have finished sliding into their slot. Counted separately from
    // installed so the round does not close with a battery still on its way in.
    private readonly HashSet<BatteryPiece> seated = new HashSet<BatteryPiece>();

    // Where the current drag started, in board space, for telling a throw from a nudge
    private Vector2 pickupPoint;

    private bool hintShown = false;

    public bool IsPlaying { get { return isPlaying; } }

    private void Awake()
    {
        if (panelGroup == null && panelRoot != null)
            panelGroup = panelRoot.GetComponent<CanvasGroup>();

        CollectPieces();

        // Reset the contents but leave the panel's active state alone. Awake first runs during
        // the SetActive in Open, so deactivating here would switch the panel back off
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

        // The panel lives switched off between rounds, so its Awake has not run yet the first
        // time through. Wiring the pieces up here is what lets that first run pass the check.
        CollectPieces();

        // Any battery can be turned to suit any slot, so having one per slot is all it takes
        // for the round to be finishable.
        if (board == null || slotLayer == null || dragLayer == null || !SlotsAreWired()
            || CountNewBatteries() < slots.Length)
        {
            // Bail loudly rather than quietly: a half-wired panel would otherwise hang the
            // quiz on a radio that can never be fixed.
            Debug.LogWarning("[BatteriesMinigame] Needs the board, the slot and drag layers, " +
                             "every slot's point, and at least one new battery per slot — " +
                             "skipping.", this);
            yield break;
        }

        isPlaying = true;

        Open();

        if (backdrop != null)
            yield return StartCoroutine(backdrop.CaptureIncludingUIRoutine());

        yield return FadeGroup(panelGroup, 0f, 1f, panelFadeDuration);
        yield return FadeGroup(instructionCard, 0f, 1f, instructionFadeDuration);

        if (cover != null)
            cover.SetArmed(true);
        else
            ArmOldBatteries();

        // Loose from the start, so they can be turned over and tried at any point. Dropping one
        // on a channel that still has its old battery in is simply refused.
        foreach (BatteryPiece battery in newBatteries)
            if (battery != null)
                battery.SetArmed(true);

        while (seated.Count < slots.Length)
            yield return null;

        foreach (BatteryPiece piece in pieces)
            piece.SetArmed(false);

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

    // ------------------------------------------------------------------ handling pieces

    private void OnPickedUp(BatteryPiece piece)
    {
        StopMoving(piece);

        // Picked up mid-flip or mid-wobble, it straightens up rather than being carried off
        // at whatever angle the tween had reached.
        piece.Rect.localRotation = piece.UprightRotation;
        piece.Rect.localScale = Vector3.one;

        pickupPoint = BoardPoint(piece.WorldCentre);

        piece.Rect.SetParent(dragLayer, true);
        piece.Rect.SetAsLastSibling();
    }

    private void OnDropped(BatteryPiece piece)
    {
        switch (piece.PieceKind)
        {
            case BatteryPiece.Kind.Cover:
                if (DraggedFarEnough(piece))
                {
                    piece.SetArmed(false);
                    StartMoving(piece, Vanish(piece));

                    // The compartment is open, so what is inside it can be reached
                    ArmOldBatteries();
                }
                else
                {
                    StartMoving(piece, SendHome(piece, false));
                }
                break;

            case BatteryPiece.Kind.OldBattery:
                if (DraggedFarEnough(piece))
                {
                    int index = SlotHolding(piece);
                    if (index >= 0)
                        cleared[index] = true;

                    piece.SetArmed(false);
                    StartMoving(piece, Vanish(piece));
                }
                else
                {
                    StartMoving(piece, SendHome(piece, false));
                }
                break;

            default:
                DropNewBattery(piece);
                break;
        }
    }

    private void OnTapped(BatteryPiece piece)
    {
        // Only the fresh batteries turn over — and not while one is still sliding somewhere,
        // which would leave it stranded wherever the flip stopped the slide.
        if (piece.PieceKind != BatteryPiece.Kind.NewBattery || IsMoving(piece))
            return;

        piece.PositiveUp = !piece.PositiveUp;
        StartMoving(piece, Flip(piece));
    }

    /// <summary>
    /// Seats a new battery if it was let go over an open channel the right way round. Anywhere
    /// else it goes back to where it came from — with a wobble if it was over a channel, so a
    /// backwards or blocked drop reads as refused rather than as a miss.
    /// </summary>
    private void DropNewBattery(BatteryPiece piece)
    {
        bool overAnySlot;
        int index = OpenSlotUnder(piece, out overAnySlot);

        if (index < 0)
        {
            StartMoving(piece, SendHome(piece, overAnySlot));
            return;
        }

        if (piece.PositiveUp != slots[index].positiveUp)
        {
            ShowFlipHint();
            StartMoving(piece, SendHome(piece, true));
            return;
        }

        installed[index] = piece;
        piece.SetArmed(false);

        piece.Rect.SetParent(slotLayer, true);
        piece.Rect.SetAsLastSibling();

        StartMoving(piece, Seat(piece, slots[index].point));
    }

    /// <summary>
    /// The nearest channel under the battery that has had its old battery taken out and has
    /// nothing in it yet, or -1. <paramref name="overAnySlot"/> says whether it was over one at
    /// all, open or not.
    /// </summary>
    private int OpenSlotUnder(BatteryPiece piece, out bool overAnySlot)
    {
        overAnySlot = false;

        Vector3 world = piece.WorldCentre;
        int best = -1;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < slots.Length; i++)
        {
            RectTransform point = slots[i].point;

            Vector2 offset = (Vector2)point.InverseTransformPoint(world) - point.rect.center;
            if (Mathf.Abs(offset.x) > point.rect.width * 0.5f + dropPadding
                || Mathf.Abs(offset.y) > point.rect.height * 0.5f + dropPadding)
                continue;

            overAnySlot = true;

            if (!SlotIsOpen(i))
                continue;

            float distance = offset.sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    private bool SlotIsOpen(int index)
    {
        bool emptied = cleared[index] || slots[index].oldBattery == null;
        return emptied && installed[index] == null;
    }

    private int SlotHolding(BatteryPiece oldBattery)
    {
        for (int i = 0; i < slots.Length; i++)
            if (slots[i].oldBattery == oldBattery)
                return i;

        return -1;
    }

    private bool DraggedFarEnough(BatteryPiece piece)
    {
        return Vector2.Distance(pickupPoint, BoardPoint(piece.WorldCentre)) >= removeDistance;
    }

    private void ArmOldBatteries()
    {
        for (int i = 0; i < slots.Length; i++)
            if (slots[i].oldBattery != null && !cleared[i])
                slots[i].oldBattery.SetArmed(true);
    }

    private void ShowFlipHint()
    {
        if (hintShown || instructionLabel == null || string.IsNullOrEmpty(flipHintText))
            return;

        hintShown = true;
        instructionLabel.text = flipHintText;
    }

    private Vector2 BoardPoint(Vector3 world)
    {
        return board.InverseTransformPoint(world);
    }

    // ------------------------------------------------------------------ motion

    private IEnumerator Seat(BatteryPiece piece, RectTransform point)
    {
        yield return Settle(piece, AnchoredFor(piece, point.TransformPoint(point.rect.center)),
                            piece.UprightRotation);

        seated.Add(piece);
    }

    private IEnumerator SendHome(BatteryPiece piece, bool rejected)
    {
        if (rejected && rejectShake > 0f && rejectShakeDuration > 0f)
            yield return Shake(piece);

        piece.Rect.SetParent(piece.HomeParent, true);
        piece.Rect.SetSiblingIndex(piece.HomeSiblingIndex);

        yield return Settle(piece, piece.HomePosition, piece.UprightRotation);
    }

    /// <summary>
    /// Fades a thrown-away piece out where it was let go and switches it off. It is never seen
    /// again this round, so there is no putting it back.
    /// </summary>
    private IEnumerator Vanish(BatteryPiece piece)
    {
        CanvasGroup group = piece.Group;
        group.blocksRaycasts = false;

        for (float t = 0f; t < vanishDuration; t += Time.unscaledDeltaTime)
        {
            float k = t / vanishDuration;
            group.alpha = 1f - k;
            piece.Rect.localScale = Vector3.one * Mathf.Lerp(1f, vanishScale, k);
            yield return null;
        }

        piece.gameObject.SetActive(false);
    }

    /// <summary>
    /// Turns a battery end over end. Always a half turn from wherever it is, so it spins the
    /// same way every time instead of whichever way a slerp happens to pick at 180 degrees.
    /// </summary>
    private IEnumerator Flip(BatteryPiece piece)
    {
        RectTransform rect = piece.Rect;
        float from = rect.localEulerAngles.z;

        for (float t = 0f; t < flipDuration; t += Time.unscaledDeltaTime)
        {
            float k = t / flipDuration;
            float eased = 1f - (1f - k) * (1f - k);
            rect.localRotation = Quaternion.Euler(0f, 0f, from + 180f * eased);
            yield return null;
        }

        rect.localRotation = piece.UprightRotation;
    }

    /// <summary>
    /// The little "no" wobble before heading back. It shakes where it was let go rather than on
    /// the way, so the refusal reads as coming from the radio.
    /// </summary>
    private IEnumerator Shake(BatteryPiece piece)
    {
        RectTransform rect = piece.Rect;
        Quaternion from = rect.localRotation;

        for (float t = 0f; t < rejectShakeDuration; t += Time.unscaledDeltaTime)
        {
            float k = t / rejectShakeDuration;

            // Three swings, damped, so it settles rather than stopping dead mid-swing
            float angle = Mathf.Sin(k * Mathf.PI * 6f) * rejectShake * (1f - k);
            rect.localRotation = from * Quaternion.Euler(0f, 0f, angle);
            yield return null;
        }

        rect.localRotation = from;
    }

    /// <summary>
    /// Slides a piece to where it is going. Unscaled time, so it moves at the same rate
    /// whatever the game's timescale is doing behind the blur.
    /// </summary>
    private IEnumerator Settle(BatteryPiece piece, Vector2 target, Quaternion toRotation)
    {
        RectTransform rect = piece.Rect;
        Vector2 fromPosition = rect.anchoredPosition;
        Quaternion fromRotation = rect.localRotation;

        for (float t = 0f; t < settleDuration; t += Time.unscaledDeltaTime)
        {
            float k = settleDuration <= 0f ? 1f : t / settleDuration;

            // Ease out, so it arrives settling rather than at full speed
            float eased = 1f - (1f - k) * (1f - k);

            rect.anchoredPosition = Vector2.Lerp(fromPosition, target, eased);
            rect.localRotation = Quaternion.Slerp(fromRotation, toRotation, eased);
            yield return null;
        }

        rect.anchoredPosition = target;
        rect.localRotation = toRotation;
    }

    /// <summary>
    /// The anchoredPosition that puts the piece's centre on a world point, under whatever
    /// parent and anchors it currently has.
    /// </summary>
    private Vector2 AnchoredFor(BatteryPiece piece, Vector3 world)
    {
        RectTransform rect = piece.Rect;

        Vector2 saved = rect.anchoredPosition;
        rect.position += world - piece.WorldCentre;
        Vector2 result = rect.anchoredPosition;
        rect.anchoredPosition = saved;

        return result;
    }

    // One tween per piece, so grabbing one mid-flight does not leave an old tween still
    // driving it somewhere it is no longer going.
    private readonly Dictionary<BatteryPiece, Coroutine> moving =
        new Dictionary<BatteryPiece, Coroutine>();
    private readonly HashSet<BatteryPiece> inMotion = new HashSet<BatteryPiece>();

    private void StartMoving(BatteryPiece piece, IEnumerator routine)
    {
        StopMoving(piece);

        if (!isActiveAndEnabled)
            return;

        inMotion.Add(piece);
        Coroutine running = StartCoroutine(Tracked(piece, routine));

        // A tween with nothing to wait on (a duration of zero) has already finished and
        // cleaned up by the time StartCoroutine returns; keeping its handle would leave the
        // piece marked as moving for good.
        if (inMotion.Contains(piece))
            moving[piece] = running;
    }

    private IEnumerator Tracked(BatteryPiece piece, IEnumerator routine)
    {
        yield return routine;
        inMotion.Remove(piece);
        moving.Remove(piece);
    }

    private void StopMoving(BatteryPiece piece)
    {
        Coroutine running;
        if (moving.TryGetValue(piece, out running) && running != null)
            StopCoroutine(running);

        moving.Remove(piece);
        inMotion.Remove(piece);
    }

    private bool IsMoving(BatteryPiece piece)
    {
        return inMotion.Contains(piece);
    }

    private void StopAllMoving()
    {
        foreach (Coroutine running in moving.Values)
            if (running != null)
                StopCoroutine(running);

        moving.Clear();
        inMotion.Clear();
    }

    // ------------------------------------------------------------------ setup

    /// <summary>
    /// Wires up the cover and every battery. Their authored spots are recorded once, the first
    /// time through, so every run starts from the same radio.
    /// </summary>
    private void CollectPieces()
    {
        pieces.Clear();

        if (cover != null)
            pieces.Add(cover);

        foreach (BatterySlot slot in slots)
            if (slot != null && slot.oldBattery != null)
                pieces.Add(slot.oldBattery);

        foreach (BatteryPiece battery in newBatteries)
            if (battery != null)
                pieces.Add(battery);

        Canvas owning = panelRoot != null ? panelRoot.GetComponentInParent<Canvas>() : null;

        foreach (BatteryPiece piece in pieces)
        {
            piece.Configure(board, owning);

            // Cleared first: this runs from both Awake and Play, and subscribing twice would
            // handle every drop twice.
            piece.PickedUp -= OnPickedUp;
            piece.Dropped -= OnDropped;
            piece.Tapped -= OnTapped;
            piece.PickedUp += OnPickedUp;
            piece.Dropped += OnDropped;
            piece.Tapped += OnTapped;

            if (!homesCaptured)
                piece.CaptureHome();
        }

        homesCaptured = true;
    }

    private bool SlotsAreWired()
    {
        if (slots.Length == 0)
            return false;

        foreach (BatterySlot slot in slots)
            if (slot == null || slot.point == null)
                return false;

        return true;
    }

    private int CountNewBatteries()
    {
        int count = 0;
        foreach (BatteryPiece battery in newBatteries)
            if (battery != null)
                count++;

        return count;
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

        ResetBoard();
        ResetVisuals();
    }

    /// <summary>Puts the cover back on and the old batteries back in, and lines the new ones up again.</summary>
    private void ResetBoard()
    {
        StopAllMoving();

        foreach (BatteryPiece piece in pieces)
        {
            piece.SetArmed(false);
            piece.GoHome();
        }

        cleared = new bool[slots.Length];
        installed = new BatteryPiece[slots.Length];
        seated.Clear();
    }

    private void Close()
    {
        StopAllMoving();
        ResetVisuals();

        if (backdrop != null)
            backdrop.Clear();

        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    /// <summary>
    /// Puts the banner and the instruction back to how a run starts, without touching whether
    /// the panel itself is on — see the note in Awake.
    /// </summary>
    private void ResetVisuals()
    {
        hintShown = false;

        if (completedBanner != null)
            completedBanner.SetActive(false);

        if (instructionLabel != null && !string.IsNullOrEmpty(instructionText))
            instructionLabel.text = instructionText;
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

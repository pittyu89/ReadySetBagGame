using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One of the player's gloved hands in the gloves minigame.
///
/// At rest it loops the idle frames in its corner. When sent after a shard it swings and
/// slides so its grip lands on the shard, closes there, and goes back — the rest pose is
/// whatever it was placed at in the scene, so it always returns to exactly where it was.
///
/// The right hand is the same sprite mirrored with a negative X scale; nothing here cares
/// which side it is on, since the reach is worked out from the two marker children.
/// </summary>
[RequireComponent(typeof(Image))]
public class GlovedHand : MonoBehaviour
{
    [Header("Idle")]
    [Tooltip("The IDLE_STATE frames, looped while the hand is at rest.")]
    [SerializeField] private Sprite[] idleFrames = new Sprite[0];
    [SerializeField] private float idleFramesPerSecond = 6f;

    [Header("Reach")]
    [Tooltip("Where the glove closes, between the fingers and the thumb. This is the point " +
             "that lands on the shard.")]
    [SerializeField] private RectTransform grabPoint;
    [Tooltip("Where the arm leaves the screen. The hand swings about this so the arm keeps " +
             "coming from the bottom corner instead of floating free.")]
    [SerializeField] private RectTransform armBase;
    [Tooltip("How much of the swing towards the shard is done by turning the hand rather " +
             "than by sliding it. 1 aims the whole arm at the shard, 0 slides it there " +
             "without turning.")]
    [SerializeField, Range(0f, 1f)] private float swingShare = 0.45f;
    [Tooltip("How far below the bottom of the screen the arm base has to stay, in canvas " +
             "units. The sprite's arm has a drawn end, so a hand that reached all the way to " +
             "a high shard would lift that end into view. Past this the hand stops short and " +
             "the shard flies the rest of the way into the glove.")]
    [SerializeField] private float armClearance = 40f;
    [Tooltip("Seconds to reach the shard.")]
    [SerializeField] private float reachDuration = 0.2f;
    [Tooltip("Seconds the glove stays closed on the shard.")]
    [SerializeField] private float gripHold = 0.06f;
    [Tooltip("Seconds to go back to rest.")]
    [SerializeField] private float returnDuration = 0.26f;

    private RectTransform rectTransform;
    private Image image;

    private Vector3 restPosition;
    private Quaternion restRotation;
    private bool restRecorded = false;

    private float idleClock = 0f;

    public bool IsBusy { get; private set; }
    public float ReachDuration => reachDuration;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        image = GetComponent<Image>();
        RecordRest();
    }

    private void RecordRest()
    {
        if (restRecorded)
            return;

        restPosition = rectTransform.localPosition;
        restRotation = rectTransform.localRotation;
        restRecorded = true;
    }

    private void Update()
    {
        if (idleFrames == null || idleFrames.Length == 0 || image == null)
            return;

        idleClock += Time.unscaledDeltaTime;
        int frame = Mathf.FloorToInt(idleClock * idleFramesPerSecond) % idleFrames.Length;

        if (idleFrames[frame] != null && image.sprite != idleFrames[frame])
            image.sprite = idleFrames[frame];
    }

    /// <summary>Back to the rest pose, dropping any reach in progress.</summary>
    public void ResetHand()
    {
        if (rectTransform == null)
            Awake();

        StopAllCoroutines();
        rectTransform.localPosition = restPosition;
        rectTransform.localRotation = restRotation;
        IsBusy = false;
        idleClock = 0f;
    }

    /// <summary>
    /// Reaches out so the grip lands on <paramref name="target"/>, collects the shard there,
    /// and returns to rest. Returns once the hand is home.
    /// </summary>
    public IEnumerator Reach(GlassShard target)
    {
        if (target == null || grabPoint == null)
            yield break;

        IsBusy = true;

        RectTransform space = rectTransform.parent as RectTransform;

        // Everything is measured in the parent's space with the hand at rest, so the pose
        // can be rebuilt from scratch every frame rather than accumulating drift
        rectTransform.localPosition = restPosition;
        rectTransform.localRotation = restRotation;

        Vector2 rest = restPosition;
        Vector2 grabOffset = (Vector2)space.InverseTransformPoint(grabPoint.position) - rest;
        Vector2 baseOffset = armBase != null
            ? (Vector2)space.InverseTransformPoint(armBase.position) - rest
            : grabOffset - new Vector2(0f, -400f);

        Vector2 goal = space.InverseTransformPoint(target.Rect.position);

        // How far the arm would have to turn to point straight at the shard, of which only
        // swingShare is taken; sliding makes up the rest
        Vector2 armNow = grabOffset - baseOffset;
        Vector2 armWanted = goal - (rest + baseOffset);
        float fullSwing = Vector2.SignedAngle(armNow, armWanted);
        float swing = fullSwing * swingShare;

        Vector2 reachedPosition = goal - Rotate(grabOffset, swing);

        // Only as far as keeps the end of the arm off screen
        float lowest = space.rect.yMin - armClearance;
        float reach = 1f;
        if (ArmBaseY(rest, reachedPosition, swing, baseOffset, 1f) > lowest)
        {
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 12; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (ArmBaseY(rest, reachedPosition, swing, baseOffset, mid) > lowest)
                    hi = mid;
                else
                    lo = mid;
            }
            reach = lo;
        }

        // Whatever the hand cannot cover, the shard does: it flies into the glove as the
        // glove comes to meet it
        target.BeginCarry();
        Vector3 shardStart = target.Rect.position;

        for (float t = 0f; t < reachDuration; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.Clamp01(t / reachDuration);
            float eased = 1f - Mathf.Pow(1f - k, 3f);
            Pose(rest, reachedPosition, swing, eased * reach);

            if (reach < 1f)
                target.Rect.position = Vector3.Lerp(shardStart, grabPoint.position, eased);

            yield return null;
        }

        Pose(rest, reachedPosition, swing, reach);
        if (reach < 1f)
            target.Rect.position = grabPoint.position;

        // The glove closes on the shard, and the shard is gone
        StartCoroutine(target.Collect(gripHold + 0.08f));
        yield return new WaitForSecondsRealtime(gripHold);

        for (float t = 0f; t < returnDuration; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.Clamp01(t / returnDuration);
            float eased = k * k * (3f - 2f * k);
            Pose(rest, reachedPosition, swing, reach * (1f - eased));
            yield return null;
        }

        rectTransform.localPosition = restPosition;
        rectTransform.localRotation = restRotation;
        IsBusy = false;
    }

    private void Pose(Vector2 rest, Vector2 reached, float swing, float amount)
    {
        Vector2 p = Vector2.LerpUnclamped(rest, reached, amount);
        rectTransform.localPosition = new Vector3(p.x, p.y, restPosition.z);
        rectTransform.localRotation = restRotation * Quaternion.Euler(0f, 0f, swing * amount);
    }

    private static float ArmBaseY(Vector2 rest, Vector2 reached, float swing, Vector2 baseOffset, float amount)
    {
        Vector2 p = Vector2.LerpUnclamped(rest, reached, amount);
        return p.y + Rotate(baseOffset, swing * amount).y;
    }

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float r = degrees * Mathf.Deg2Rad;
        float c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }
}

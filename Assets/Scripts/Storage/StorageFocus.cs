using System;
using System.Collections;
using System.Collections.Generic;
using Cinemachine;
using UnityEngine;

/// <summary>
/// Opening a storage: the camera glides in front of the furniture, its doors and drawers open,
/// and then the inventory comes up. Closing the inventory shuts them and glides back.
///
/// The close-up is a second Cinemachine camera that takes priority over the player's follow
/// camera, so the brain blends between the two. It looks at the furniture from the side the
/// player is on, skipping sides pressed against a wall or other furniture: the house model's
/// pivots all sit at the house origin, so the furniture's own rotation says nothing about
/// which way it faces.
///
/// The character is hidden during the close-up, since they stand between the camera and the
/// furniture.
/// </summary>
public class StorageFocus : MonoBehaviour
{
    [Header("Camera")]
    [Tooltip("Seconds the camera takes to glide in, and back out.")]
    [SerializeField] private float blendTime = 0.6f;
    [Tooltip("Space left around the furniture in the close-up, as a multiple of its size.")]
    [SerializeField] private float framePadding = 1.25f;
    [SerializeField] private float minDistance = 1.2f;
    [Tooltip("Degrees the close-up looks down at the furniture. Steep enough to see into " +
             "pulled-out drawers, which from straight on just look closer.")]
    [SerializeField] private float lookDownAngle = 28f;

    [Header("Doors and Drawers")]
    [Tooltip("Seconds a door or drawer takes to open.")]
    [SerializeField] private float openTime = 0.45f;
    [Tooltip("Seconds between one row of drawers or doors starting to move and the next.")]
    [SerializeField] private float rowStagger = 0.12f;

    private enum State { Idle, Opening, Open, Closing }

    private static StorageFocus instance;
    private State state;

    private CinemachineVirtualCamera focusCamera;
    private CinemachineBrain brain;
    private InventoryPanel inventory;

    private StorageFurniture furniture;
    private Vector3 front;
    private PlayerController playerMovement;
    private bool movementWasEnabled;
    private readonly List<Renderer> hiddenPlayerRenderers = new List<Renderer>();
    private BlobShadow hiddenShadow;

    private const int FOCUS_PRIORITY = 100;
    // Space kept between the camera and a wall behind it
    private const float WALL_CLEARANCE = 0.2f;
    // How far past its side the furniture is checked for a wall or neighbour
    private const float SIDE_PROBE = 0.6f;
    // Parts whose tops are this close sit in the same row
    private const float ROW_TOLERANCE = 0.1f;

    /// <summary>True from a storage tap until the camera is back behind the player.</summary>
    public static bool IsBusy => instance != null && instance.state != State.Idle;

    void Awake()
    {
        instance = this;
        inventory = FindFirstObjectByType<InventoryPanel>();

        Camera main = Camera.main;
        brain = main != null ? main.GetComponent<CinemachineBrain>() : null;

        var cameraObject = new GameObject("StorageFocusCamera");
        cameraObject.transform.SetParent(transform, false);
        focusCamera = cameraObject.AddComponent<CinemachineVirtualCamera>();
        focusCamera.Priority = 0;

        // Same lens as the follow camera, so the blend only moves the view
        var follow = FindFirstObjectByType<CameraOrbitController>();
        if (follow != null)
            focusCamera.m_Lens = follow.GetComponent<CinemachineVirtualCamera>().m_Lens;

        SetUpBlends();
    }

    void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    /// <summary>
    /// The brain's default blend is the slow one used elsewhere; the close-up gets its own. Left
    /// alone if the brain already has custom blends set up in the scene.
    /// </summary>
    private void SetUpBlends()
    {
        if (brain == null || brain.m_CustomBlends != null)
            return;

        var blend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Style.EaseInOut, blendTime);
        var blends = ScriptableObject.CreateInstance<CinemachineBlenderSettings>();
        blends.m_CustomBlends = new[]
        {
            new CinemachineBlenderSettings.CustomBlend
            {
                m_From = CinemachineBlenderSettings.kBlendFromAnyCameraLabel,
                m_To = focusCamera.Name,
                m_Blend = blend,
            },
            new CinemachineBlenderSettings.CustomBlend
            {
                m_From = focusCamera.Name,
                m_To = CinemachineBlenderSettings.kBlendFromAnyCameraLabel,
                m_Blend = blend,
            },
        };
        brain.m_CustomBlends = blends;
    }

    /// <summary>
    /// Zooms in on <paramref name="target"/> and opens it, then calls <paramref name="onOpened"/>
    /// to show the inventory. False if another storage is still opening or closing.
    /// </summary>
    public static bool Open(StorageFurniture target, Transform player, Action onOpened)
    {
        if (instance == null)
        {
            onOpened?.Invoke();
            return true;
        }

        if (IsBusy || target == null)
            return false;

        instance.StartCoroutine(instance.OpenRoutine(target, player, onOpened));
        return true;
    }

    void Update()
    {
        // The inventory's close button ends the close-up, whatever closed it
        if (state == State.Open && (inventory == null || !inventory.IsOpen))
            StartCoroutine(CloseRoutine());
    }

    private IEnumerator OpenRoutine(StorageFurniture target, Transform player, Action onOpened)
    {
        state = State.Opening;
        furniture = target;

        Bounds bounds = target.GetWorldBounds();
        front = ChooseFront(target, bounds, player != null ? player.position : bounds.center, player);
        PlaceCamera(target, bounds, player);
        focusCamera.Priority = FOCUS_PRIORITY;

        HidePlayer(player);

        // Doors start opening part-way into the glide, so they are moving as it lands
        float openDelay = blendTime * 0.4f;
        yield return new WaitForSeconds(openDelay);
        yield return MoveParts(true, openTime);

        float remaining = blendTime - openDelay - openTime;
        if (remaining > 0f)
            yield return new WaitForSeconds(remaining);

        state = State.Open;
        onOpened?.Invoke();
    }

    private IEnumerator CloseRoutine()
    {
        state = State.Closing;
        focusCamera.Priority = 0;

        yield return MoveParts(false, openTime * 0.7f);

        float remaining = blendTime - openTime * 0.7f;
        if (remaining > 0f)
            yield return new WaitForSeconds(remaining);

        ShowPlayer();
        furniture = null;
        state = State.Idle;
    }

    /// <summary>
    /// The world-axis side to look at the furniture from: the one facing the player most,
    /// among the sides that aren't against a wall, leaning toward the broad face. Furniture
    /// with doors or drawers faces the way they stick out.
    /// </summary>
    private static Vector3 ChooseFront(StorageFurniture target, Bounds bounds, Vector3 playerPosition, Transform player)
    {
        Vector3 toPlayer = playerPosition - bounds.center;
        toPlayer.y = 0f;
        toPlayer = toPlayer.sqrMagnitude > 1e-4f ? toPlayer.normalized : Vector3.zero;

        Vector3 partsOut = Vector3.zero;
        Renderer body = target.GetComponent<Renderer>();
        foreach (StorageMovingPart moving in target.MovingParts)
        {
            Renderer r = moving != null && moving.part != null ? moving.part.GetComponent<Renderer>() : null;
            if (r != null && body != null)
                partsOut += r.bounds.center - body.bounds.center;
        }
        partsOut.y = 0f;
        partsOut = partsOut.sqrMagnitude > 1e-4f ? partsOut.normalized : Vector3.zero;

        Vector3[] sides = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
        Vector3 best = sides[0];
        float bestScore = float.NegativeInfinity;
        foreach (Vector3 side in sides)
        {
            // Cupboards, shelves and drawers open on their broad face, so it counts for a bit
            float faceWidth = ExtentAlong(bounds, Vector3.Cross(Vector3.up, side));
            float broadness = faceWidth / Mathf.Max(1e-4f, Mathf.Max(bounds.extents.x, bounds.extents.z));

            float score = Vector3.Dot(side, toPlayer) + 0.5f * broadness + 3f * Vector3.Dot(side, partsOut);
            if (IsBlocked(target, player, bounds.center, side, ExtentAlong(bounds, side) + SIDE_PROBE))
                score -= 4f;

            if (score > bestScore)
            {
                bestScore = score;
                best = side;
            }
        }
        return best;
    }

    private void PlaceCamera(StorageFurniture target, Bounds bounds, Transform player)
    {
        Vector3 across = Vector3.Cross(Vector3.up, front);
        float width = ExtentAlong(bounds, across) * 2f;
        float depth = ExtentAlong(bounds, front) * 2f;

        Camera main = Camera.main;
        float aspect = main != null ? main.aspect : 16f / 9f;
        float halfTan = Mathf.Tan(focusCamera.m_Lens.FieldOfView * 0.5f * Mathf.Deg2Rad);

        // Far enough for the whole front face to fit the screen both ways
        float fit = Mathf.Max(bounds.size.y * 0.5f / halfTan, width * 0.5f / (halfTan * aspect)) * framePadding;
        // Drawers slide out toward the camera, so it backs off by as much to keep them in frame
        float pulledOut = 0f;
        foreach (StorageMovingPart moving in target.MovingParts)
        {
            if (moving != null && moving.motion == StorageMovingPart.Motion.SlideOut)
                pulledOut = Mathf.Max(pulledOut, moving.amount);
        }

        float distance = Mathf.Max(minDistance, fit) + depth * 0.5f + pulledOut;

        float pitch = lookDownAngle * Mathf.Deg2Rad;
        Vector3 outward = (front * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch)).normalized;

        // Stop in front of the wall behind the camera, rather than looking through it
        Vector3 faceCentre = bounds.center + front * depth * 0.5f;
        float clear = ClearDistance(target, player, faceCentre, outward, distance - depth * 0.5f);
        distance = Mathf.Min(distance, clear - WALL_CLEARANCE + depth * 0.5f);

        Transform cam = focusCamera.transform;
        cam.position = bounds.center + outward * distance;
        cam.rotation = Quaternion.LookRotation(bounds.center - cam.position, Vector3.up);
    }

    private static float ExtentAlong(Bounds bounds, Vector3 axis)
    {
        return Mathf.Abs(bounds.extents.x * axis.x) + Mathf.Abs(bounds.extents.y * axis.y) + Mathf.Abs(bounds.extents.z * axis.z);
    }

    private static bool IsBlocked(StorageFurniture target, Transform player, Vector3 origin, Vector3 direction, float length)
    {
        return ClearDistance(target, player, origin, direction, length) < length;
    }

    /// <summary>How far a line can run before it meets something other than the furniture or player.</summary>
    private static float ClearDistance(StorageFurniture target, Transform player, Vector3 origin, Vector3 direction, float length)
    {
        int mask = ~LayerMask.GetMask("Sphere");
        float nearest = length;
        foreach (RaycastHit hit in Physics.RaycastAll(origin, direction, length, mask, QueryTriggerInteraction.Ignore))
        {
            if (target.Owns(hit.collider))
                continue;
            if (player != null && hit.collider.transform.IsChildOf(player))
                continue;
            nearest = Mathf.Min(nearest, hit.distance);
        }
        return nearest;
    }

    private IEnumerator MoveParts(bool opening, float duration)
    {
        var moves = new List<PartMove>();
        foreach (StorageMovingPart moving in furniture.MovingParts)
        {
            if (moving != null && moving.part != null)
                moves.Add(new PartMove(moving, front));
        }
        if (moves.Count == 0)
            yield break;

        // Row by row: the top row opens first and the rest follow in turn down the piece,
        // then they shut in the reverse order. Parts level with each other move together.
        var rowTops = new List<float>();
        foreach (PartMove move in moves)
        {
            if (!rowTops.Exists(top => Mathf.Abs(top - move.Top) < ROW_TOLERANCE))
                rowTops.Add(move.Top);
        }
        rowTops.Sort((a, b) => b.CompareTo(a));

        var delays = new float[moves.Count];
        for (int i = 0; i < moves.Count; i++)
        {
            int row = rowTops.FindIndex(top => Mathf.Abs(top - moves[i].Top) < ROW_TOLERANCE);
            delays[i] = (opening ? row : rowTops.Count - 1 - row) * rowStagger;
        }

        float total = duration + (rowTops.Count - 1) * rowStagger;
        for (float t = 0f; t < total; t += Time.deltaTime)
        {
            for (int i = 0; i < moves.Count; i++)
            {
                float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - delays[i]) / duration));
                moves[i].Apply(opening ? eased : 1f - eased);
            }
            yield return null;
        }

        foreach (PartMove move in moves)
            move.Apply(opening ? 1f : 0f);
    }

    /// <summary>
    /// One door or drawer's path from shut to open. Built from its shut pose, which the part
    /// keeps in its local transform: it is always put back exactly there after closing.
    /// </summary>
    private class PartMove
    {
        private readonly Transform part;
        private readonly Vector3 shutPosition;
        private readonly Quaternion shutRotation;
        private readonly Vector3 shutLocalPosition;
        private readonly Quaternion shutLocalRotation;
        private readonly Vector3 hinge;
        private readonly float angle;
        private readonly Vector3 slide;

        /// <summary>Height of the part's top when shut, for sorting it into a row.</summary>
        public float Top { get; }

        private static readonly Dictionary<Transform, (Vector3, Quaternion)> shutPoses =
            new Dictionary<Transform, (Vector3, Quaternion)>();

        public PartMove(StorageMovingPart moving, Vector3 front)
        {
            part = moving.part;

            // Measure from the shut pose, even if the part was left part-way open
            if (!shutPoses.TryGetValue(part, out var shut))
            {
                shut = (part.localPosition, part.localRotation);
                shutPoses[part] = shut;
            }
            shutLocalPosition = shut.Item1;
            shutLocalRotation = shut.Item2;
            part.localPosition = shutLocalPosition;
            part.localRotation = shutLocalRotation;
            shutPosition = part.position;
            shutRotation = part.rotation;

            Renderer renderer = part.GetComponent<Renderer>();
            Bounds bounds = renderer != null ? renderer.bounds : new Bounds(part.position, Vector3.zero);
            Top = bounds.max.y;

            if (moving.motion == StorageMovingPart.Motion.SlideOut)
            {
                slide = front * moving.amount;
                return;
            }

            // The hinge runs up the door's back edge on the chosen side, where it meets the
            // cabinet: turning about the front edge swung a thick door's back away from the
            // body, so it looked detached
            Vector3 viewerRight = Vector3.Cross(Vector3.up, -front);
            float side = moving.motion == StorageMovingPart.Motion.SwingHingeRight ? 1f : -1f;
            hinge = bounds.center
                    + viewerRight * side * ExtentAlong(bounds, viewerRight)
                    - front * ExtentAlong(bounds, front);
            hinge.y = 0f;

            // Whichever way round swings the door out toward the camera
            Vector3 centre = new Vector3(bounds.center.x, 0f, bounds.center.z);
            Vector3 swung = hinge + Quaternion.AngleAxis(moving.amount, Vector3.up) * (centre - hinge);
            angle = Vector3.Dot(swung - centre, front) >= 0f ? moving.amount : -moving.amount;
        }

        public void Apply(float openness)
        {
            if (openness <= 0f)
            {
                part.localPosition = shutLocalPosition;
                part.localRotation = shutLocalRotation;
                return;
            }

            if (angle == 0f)
            {
                part.position = shutPosition + slide * openness;
                return;
            }

            Quaternion turn = Quaternion.AngleAxis(angle * openness, Vector3.up);
            Vector3 pivot = new Vector3(hinge.x, shutPosition.y, hinge.z);
            part.SetPositionAndRotation(pivot + turn * (shutPosition - pivot), turn * shutRotation);
        }
    }

    private void HidePlayer(Transform player)
    {
        hiddenPlayerRenderers.Clear();
        if (player == null)
            return;

        foreach (Renderer r in player.GetComponentsInChildren<Renderer>())
        {
            if (r.enabled)
            {
                r.enabled = false;
                hiddenPlayerRenderers.Add(r);
            }
        }

        // The contact shadow is its own object, so it goes by switching its component off
        hiddenShadow = player.GetComponent<BlobShadow>();
        if (hiddenShadow != null && hiddenShadow.enabled)
            hiddenShadow.enabled = false;
        else
            hiddenShadow = null;

        // No walking off while the camera is on the furniture
        playerMovement = player.GetComponent<PlayerController>();
        movementWasEnabled = playerMovement != null && playerMovement.enabled;
        if (movementWasEnabled)
            playerMovement.SetMovementEnabled(false);
    }

    private void ShowPlayer()
    {
        foreach (Renderer r in hiddenPlayerRenderers)
            if (r != null)
                r.enabled = true;
        hiddenPlayerRenderers.Clear();

        if (hiddenShadow != null)
            hiddenShadow.enabled = true;
        hiddenShadow = null;

        if (movementWasEnabled && playerMovement != null)
            playerMovement.SetMovementEnabled(true);
        playerMovement = null;
        movementWasEnabled = false;
    }
}

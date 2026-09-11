using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Spawns the player and the GoBag on random valid floor spots inside the house
/// instead of the authored fixed positions.
///
/// Works by grid-sampling the house geometry once at startup and keeping only cells that
/// sit on a flat, unobstructed floor with enough headroom for the player. Only ACTIVE
/// geometry is sampled, so the spawnable area automatically follows the difficulty
/// (garage and second floor are toggled by GameDifficultyApplier).
///
/// Execution order matters: this runs after GameDifficultyApplier (so the right rooms are
/// active) and before CharacterSpawner and GoBagFloater (so they pick up the new positions).
/// </summary>
public class HouseSpawnRandomizer : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Transform house;
    [SerializeField] private GameObject player;   // in-scene player that CharacterSpawner replaces
    [SerializeField] private GameObject goBag;

    [Tooltip("The floor meshes that count as spawnable ground. A surface being flat is not " +
             "enough on its own — roofs and the tops of furniture are flat too.")]
    [SerializeField] private List<Transform> floorObjects = new List<Transform>();

    [Header("Enable")]
    [SerializeField] private bool randomizeSpawns = true;

    [Header("Sampling")]
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private float clearanceRadius = 0.55f;
    [SerializeField] private float clearanceHeight = 2f;
    [SerializeField] private float minFloorNormalY = 0.85f;  // reject walls and steep slopes

    [Header("Placement")]
    [Tooltip("Lifted off the floor after the object's own base offset is accounted for. Keep " +
             "this at nearly zero: it is clearance, not the height of the object.")]
    [SerializeField] private float groundClearance = 0.01f;
    [SerializeField] private float minSeparation = 8f;

    [Header("Fairness")]
    [Tooltip("In a teacher session, seed the randomiser from the session code so every " +
             "student in that session gets the same layout. Offline play stays fully random.")]
    [SerializeField] private bool useSessionSeed = true;

    private readonly List<Vector3> floorPoints = new List<Vector3>();
    private System.Random rng;

    private void Start()
    {
        if (!randomizeSpawns)
            return;

        rng = new System.Random(ResolveSeed());

        // The player's and bag's own colliders would register as obstructions, so take them
        // out of the physics queries while we sample.
        Collider playerCollider = player != null ? player.GetComponent<Collider>() : null;
        Collider bagCollider = goBag != null ? goBag.GetComponent<Collider>() : null;
        bool playerColliderWasOn = playerCollider != null && playerCollider.enabled;
        bool bagColliderWasOn = bagCollider != null && bagCollider.enabled;

        if (playerCollider != null) playerCollider.enabled = false;
        if (bagCollider != null) bagCollider.enabled = false;
        Physics.SyncTransforms();

        BuildFloorPoints();

        if (floorPoints.Count == 0)
        {
            Debug.LogWarning("HouseSpawnRandomizer: no valid floor points found — keeping the authored spawn positions.", this);
        }
        else
        {
            PlaceSpawns();
        }

        if (playerCollider != null) playerCollider.enabled = playerColliderWasOn;
        if (bagCollider != null) bagCollider.enabled = bagColliderWasOn;
        Physics.SyncTransforms();
    }

    /// <summary>
    /// Grid-samples the active house geometry and records every cell that is standable.
    /// </summary>
    private void BuildFloorPoints()
    {
        floorPoints.Clear();

        if (house == null)
        {
            Debug.LogWarning("HouseSpawnRandomizer: no house assigned.", this);
            return;
        }

        // includeInactive: false — rooms disabled for this difficulty are not spawnable
        if (floorObjects.Count == 0)
        {
            Debug.LogWarning("HouseSpawnRandomizer: no floor objects assigned — refusing to sample, " +
                             "otherwise the player could spawn on a roof or on top of furniture.", this);
            return;
        }

        Renderer[] renderers = house.GetComponentsInChildren<Renderer>(false);
        if (renderers.Length == 0)
            return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        float rayStart = bounds.max.y + 2f;
        float rayLength = bounds.size.y + 4f;

        for (float x = bounds.min.x; x <= bounds.max.x; x += cellSize)
        {
            for (float z = bounds.min.z; z <= bounds.max.z; z += cellSize)
            {
                RaycastHit hit;
                if (!Physics.Raycast(new Vector3(x, rayStart, z), Vector3.down, out hit,
                                     rayLength, ~0, QueryTriggerInteraction.Ignore))
                    continue;

                // Must be an actual floor mesh. Flatness alone is not enough: roofs and the
                // tops of tables and wardrobes are flat and have headroom above them too.
                if (!IsFloor(hit.collider.transform))
                    continue;

                if (hit.normal.y < minFloorNormalY)
                    continue;

                Vector3 floor = hit.point;
                Vector3 capsuleBottom = floor + Vector3.up * (clearanceRadius + 0.05f);
                Vector3 capsuleTop = floor + Vector3.up * Mathf.Max(clearanceHeight - clearanceRadius, clearanceRadius + 0.1f);

                if (Physics.CheckCapsule(capsuleBottom, capsuleTop, clearanceRadius, ~0, QueryTriggerInteraction.Ignore))
                    continue;

                floorPoints.Add(floor);
            }
        }
    }

    /// <summary>
    /// True if the hit transform is one of the designated floor meshes, or sits under one.
    /// </summary>
    private bool IsFloor(Transform hitTransform)
    {
        for (int i = 0; i < floorObjects.Count; i++)
        {
            Transform floor = floorObjects[i];
            if (floor == null)
                continue;

            if (hitTransform == floor || hitTransform.IsChildOf(floor))
                return true;
        }
        return false;
    }

    private void PlaceSpawns()
    {
        Vector3 playerPoint = floorPoints[rng.Next(floorPoints.Count)];

        if (player != null)
        {
            // A CharacterController fights direct transform writes, so move it while disabled.
            CharacterController controller = player.GetComponent<CharacterController>();
            bool controllerWasOn = controller != null && controller.enabled;
            if (controller != null) controller.enabled = false;

            player.transform.position = playerPoint + Vector3.up * (BaseOffset(player) + groundClearance);

            if (controller != null) controller.enabled = controllerWasOn;
        }

        if (goBag != null)
        {
            Vector3 bagPoint = PickPointAwayFrom(playerPoint, minSeparation);
            goBag.transform.position = bagPoint + Vector3.up * (BaseOffset(goBag) + groundClearance);
        }
    }

    /// <summary>
    /// How far an object's origin sits above its own lowest point.
    ///
    /// This used to be two hand-tuned constants (0.55 for the player, 0.65 for the bag) copied
    /// from the authored scene positions. That silently baked in whatever gap those positions
    /// happened to have: the bag's pivot sits ~0.5 above its sprite's base, so a 0.65 lift left
    /// it hovering ~0.15 off the floor, and the shadow - correctly drawn on the floor - looked
    /// detached from it. Measuring the object instead means a spawn point lands the object ON
    /// the floor whatever its pivot happens to be, and stays correct if the art is reimported.
    ///
    /// The collider is preferred over the renderer where there is one, because that is the
    /// surface physics will actually rest on.
    /// </summary>
    private static float BaseOffset(GameObject go)
    {
        CharacterController controller = go.GetComponent<CharacterController>();
        if (controller != null)
            return go.transform.position.y - controller.bounds.min.y;

        Renderer renderer = go.GetComponentInChildren<Renderer>(true);
        if (renderer != null)
            return go.transform.position.y - renderer.bounds.min.y;

        return 0f;
    }

    /// <summary>
    /// Picks a random floor point at least <paramref name="minDistance"/> from the origin.
    /// If nothing is far enough, returns the farthest point available so the two never overlap.
    /// </summary>
    private Vector3 PickPointAwayFrom(Vector3 origin, float minDistance)
    {
        List<Vector3> candidates = new List<Vector3>();
        float minSqr = minDistance * minDistance;

        for (int i = 0; i < floorPoints.Count; i++)
        {
            if ((floorPoints[i] - origin).sqrMagnitude >= minSqr)
                candidates.Add(floorPoints[i]);
        }

        if (candidates.Count > 0)
            return candidates[rng.Next(candidates.Count)];

        Vector3 farthest = floorPoints[0];
        float farthestSqr = -1f;
        for (int i = 0; i < floorPoints.Count; i++)
        {
            float sqr = (floorPoints[i] - origin).sqrMagnitude;
            if (sqr > farthestSqr)
            {
                farthestSqr = sqr;
                farthest = floorPoints[i];
            }
        }
        return farthest;
    }

    private int ResolveSeed()
    {
        string sessionCode = PlayerPrefs.GetString("SessionCode", "");

        if (useSessionSeed && !string.IsNullOrEmpty(sessionCode))
            return StableHash(sessionCode);

        return System.Environment.TickCount;
    }

    /// <summary>
    /// string.GetHashCode() is not guaranteed to match across runtimes, which would defeat
    /// the point of a shared session seed. This one is deterministic everywhere.
    /// </summary>
    private static int StableHash(string value)
    {
        unchecked
        {
            int hash = 23;
            for (int i = 0; i < value.Length; i++)
                hash = hash * 31 + value[i];
            return hash;
        }
    }
}

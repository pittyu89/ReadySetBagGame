using UnityEngine;

/// <summary>
/// Marks the props a player can interact with, so "which of these can I open?" is answerable
/// at a glance instead of by trial and error.
///
/// Every discoverable prop carries the same steady arrow. It is deliberately one constant
/// colour: the marker answers "this can be searched", and the reach test in ModelClickHandler
/// decides whether a tap actually opens it.
///
/// Optional to place in the scene. ModelClickHandler creates one with these defaults if none
/// exists; add the component to a GameObject in GameScene only if you want to tune it.
/// </summary>
public class ClickableHighlightManager : MonoBehaviour
{
    [Header("Arrow")]
    [Tooltip("Leave empty to use a built-in rounded arrow. Assign your own sprite to match " +
             "the game's art.")]
    [SerializeField] private Sprite iconSprite;

    [SerializeField] private Color iconColor = new Color(0.36f, 0.88f, 0.40f, 1f);
    [SerializeField] private float iconSize = 0.5f;

    [Tooltip("Gap between the top of the prop and the arrow.")]
    [SerializeField] private float heightOffset = 0.55f;

    [Header("Motion")]
    [SerializeField] private float bobAmplitude = 0.08f;
    [SerializeField] private float bobSpeed = 2.5f;

    [Tooltip("Seconds for the arrow to fade in when it appears and out when it leaves.")]
    [SerializeField] private float fadeDuration = 0.25f;

    [Header("Discovery")]
    [Tooltip("Props beyond this show no arrow. Kept near one room's width: markers draw over " +
             "walls, so a longer range fills the screen with arrows from the whole house.")]
    [SerializeField] private float discoveryRange = 7f;

    [Tooltip("Max height difference, so furniture on the floor above does not show through " +
             "the ceiling.")]
    [SerializeField] private float discoveryVerticalReach = 3f;

    [Tooltip("Hide an arrow when a wall stands between the camera and the prop.\n\n" +
             "Off by default. The first floor's walls are one big mesh collider, and with the " +
             "camera looking down at an angle the ray to a marker clips wall geometry that is " +
             "not visually blocking anything - so arrows disappeared for props in plain sight. " +
             "Leaving this off means arrows can show through a wall into the next room, which " +
             "is the lesser problem.")]
    [SerializeField] private bool hideBehindWalls = false;

    [Header("Rescan")]
    [Tooltip("Houses are spawned at runtime by HouseSpawnRandomizer, so the prop list is " +
             "refreshed periodically rather than only once at startup.")]
    [SerializeField] private float rescanInterval = 2f;

    private StorageFurniture[] models;
    private ClickableIndicator[] indicators;
    private Transform playerTransform;
    private float nextRescanTime;
    private int wallMask = -1;

    // Camera used for this frame's line-of-sight tests. Re-resolved whenever it goes null so a
    // scene reload or camera swap still picks the new one up.
    private Camera frameCamera;

    void Start()
    {
        Rescan();
    }

    /// <summary>
    /// Rebuilds the prop list and makes sure each one has a configured indicator.
    /// </summary>
    private void Rescan()
    {
        // Include inactive props: upper floors start hidden and are revealed later, so an
        // active-only scan would miss them permanently.
        models = FindObjectsOfType<StorageFurniture>(true);
        indicators = new ClickableIndicator[models.Length];

        for (int i = 0; i < models.Length; i++)
        {
            if (models[i] == null)
                continue;

            ClickableIndicator indicator = models[i].GetComponent<ClickableIndicator>();
            if (indicator == null)
                indicator = models[i].gameObject.AddComponent<ClickableIndicator>();

            indicator.Configure(iconSprite, iconColor, iconSize, heightOffset,
                                bobAmplitude, bobSpeed, fadeDuration);

            indicators[i] = indicator;
        }

        nextRescanTime = Time.time + rescanInterval;
    }

    void LateUpdate()
    {
        if (Time.time >= nextRescanTime)
            Rescan();

        // Nothing is clickable until the bag is picked up, so nothing should be advertised as
        // clickable either. This also gives picking the bag up a visible payoff: the room
        // fills with markers for everything the player can now search.
        if (!GoBagPickup.IsBagPickedUp())
        {
            HideAll();
            return;
        }

        if (playerTransform == null)
        {
            FindPlayer();
            if (playerTransform == null)
            {
                HideAll();
                return;
            }
        }

        Vector3 playerPosition = playerTransform.position;

        // Cached for the duration of this frame's loop; see HasLineOfSight.
        if (frameCamera == null)
            frameCamera = Camera.main;

        for (int i = 0; i < models.Length; i++)
        {
            StorageFurniture model = models[i];
            ClickableIndicator indicator = indicators[i];

            if (model == null || indicator == null)
                continue;

            if (!model.gameObject.activeInHierarchy)
            {
                indicator.SetVisible(false);
                continue;
            }

            bool nearby = model.IsWithinReach(playerPosition, discoveryRange,
                                              discoveryVerticalReach);

            indicator.SetVisible(nearby && (!hideBehindWalls || HasLineOfSight(indicator)));
        }
    }

    /// <summary>
    /// Whether a wall stands between the camera and this prop's marker.
    ///
    /// Only walls are tested. An earlier version raycast against everything, which meant the
    /// player's own body blocked the ray as they walked up to a prop and the arrow vanished
    /// exactly when it was most useful - along with any lamp or chair that happened to pass
    /// through the line. Furniture should never hide a marker; a wall should.
    /// </summary>
    private bool HasLineOfSight(ClickableIndicator indicator)
    {
        // Resolved once per frame by LateUpdate rather than once per prop: this runs inside the
        // per-model loop, so a Camera.main lookup here cost one tagged search per marker.
        Camera cam = frameCamera;
        if (cam == null)
            return true;

        if (wallMask == -1)
            wallMask = LayerMask.GetMask("Wall");

        // No Wall layer in this project: show the marker rather than hide everything.
        if (wallMask == 0)
            return true;

        Vector3 origin = cam.transform.position;
        Vector3 toIcon = indicator.AnchorPosition - origin;
        float distance = toIcon.magnitude;
        if (distance < 0.01f)
            return true;

        return !Physics.Raycast(origin, toIcon / distance, distance, wallMask);
    }

    private void HideAll()
    {
        if (indicators == null)
            return;

        for (int i = 0; i < indicators.Length; i++)
        {
            if (indicators[i] != null)
                indicators[i].SetVisible(false);
        }
    }

    private void FindPlayer()
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            playerTransform = playerObject.transform;
            return;
        }

        PlayerController playerMovement = FindObjectOfType<PlayerController>();
        if (playerMovement != null)
            playerTransform = playerMovement.transform;
    }
}

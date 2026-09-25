using UnityEngine;

/// <summary>
/// The house cat. Pads around the house on its own, sits down for a while, then picks a new
/// direction - and meows now and then when the player comes close.
///
/// There is no NavMesh in the house, so steering is local: before committing to a heading the
/// cat probes ahead for walls and for a drop (stairs, the edge of a floor), and it is pulled back
/// toward where it started once it strays past <see cref="wanderRadius"/>, which keeps it inside
/// rather than out through an open door.
///
/// The sprite is a camera-facing billboard (see <see cref="BillboardToCamera"/>), so the walk
/// row is picked by which way the cat moves across the screen, not by its world heading.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class CatWander : MonoBehaviour
{
    private enum State { Sitting, Walking }

    [Header("Sprite")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite[] walkRightFrames;
    [SerializeField] private Sprite[] walkLeftFrames;
    [SerializeField] private Sprite[] sitFrames;
    [SerializeField] private float walkFps = 9f;
    [SerializeField] private float sitFps = 3f;

    [Header("Wandering")]
    [SerializeField] private float walkSpeed = 1.1f;
    [SerializeField] private Vector2 walkDuration = new Vector2(2f, 5f);
    [SerializeField] private Vector2 sitDuration = new Vector2(3f, 8f);
    [Tooltip("How far from its starting spot the cat may roam before it heads back.")]
    [SerializeField] private float wanderRadius = 8f;
    [Tooltip("Distance ahead checked for walls and furniture before turning.")]
    [SerializeField] private float lookAhead = 0.8f;
    [Tooltip("A drop deeper than this ahead (stairs, a floor edge) turns the cat around.")]
    [SerializeField] private float maxDrop = 0.5f;
    [SerializeField] private LayerMask obstacleMask = ~0;

    [Header("Gravity")]
    [SerializeField] private float gravity = -9.81f;

    [Header("Meow")]
    [SerializeField] private AudioClip meowClip;
    [Tooltip("The cat only meows when the player is within this distance.")]
    [SerializeField] private float meowRange = 4f;
    [Tooltip("Seconds between meows, picked at random within this range.")]
    [SerializeField] private Vector2 meowInterval = new Vector2(8f, 20f);
    [Tooltip("How far away a meow is still heard. Clicking the cat meows at any distance, " +
             "so this is kept well past the proximity meow's range.")]
    [SerializeField] private float meowHearingDistance = 15f;

    // Every cat in the scene, so a click can be tested against their sprites
    private static readonly System.Collections.Generic.List<CatWander> cats =
        new System.Collections.Generic.List<CatWander>();

    private CharacterController controller;
    private Transform cameraTransform;
    private Transform player;
    private AudioSource meowSource;

    private State state;
    private float stateTimer;
    private Vector3 heading;
    private Vector3 home;
    private float verticalVelocity;

    private Sprite[] currentFrames;
    private float frameTimer;
    private int frameIndex;
    private bool facingRight = true;
    private float meowTimer;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        home = transform.position;

        meowSource = GetComponent<AudioSource>();
        if (meowSource == null)
            meowSource = gameObject.AddComponent<AudioSource>();
        meowSource.playOnAwake = false;
        meowSource.loop = false;
        // Heard from where the cat is, so a meow from the next room sounds like it
        meowSource.spatialBlend = 1f;
        meowSource.rolloffMode = AudioRolloffMode.Linear;
        meowSource.minDistance = 1f;
        meowSource.maxDistance = meowHearingDistance;

        if (SoundManager.Instance != null)
            SoundManager.Instance.RegisterSFXSource(meowSource);

        meowTimer = Random.Range(meowInterval.x, meowInterval.y);
        Sit();
    }

    void Update()
    {
        stateTimer -= Time.deltaTime;

        if (state == State.Walking)
        {
            if (stateTimer <= 0f)
                Sit();
            else if (!PathClear(heading))
                PickHeading();
        }
        else if (stateTimer <= 0f)
        {
            Walk();
        }

        Move();
        Animate();
        UpdateMeow();
    }

    private void Sit()
    {
        state = State.Sitting;
        stateTimer = Random.Range(sitDuration.x, sitDuration.y);
        SetFrames(sitFrames);
    }

    private void Walk()
    {
        state = State.Walking;
        stateTimer = Random.Range(walkDuration.x, walkDuration.y);
        PickHeading();
    }

    /// <summary>
    /// Chooses a clear direction, leaning toward home once the cat has strayed. If every probe
    /// is blocked it simply sits back down and tries again later.
    /// </summary>
    private void PickHeading()
    {
        Vector3 toHome = home - transform.position;
        toHome.y = 0f;
        bool strayed = toHome.magnitude > wanderRadius;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector3 candidate;
            if (strayed && attempt < 4)
                candidate = Quaternion.Euler(0f, Random.Range(-45f, 45f), 0f) * toHome.normalized;
            else
                candidate = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;

            if (PathClear(candidate))
            {
                heading = candidate;
                return;
            }
        }

        Sit();
    }

    private bool PathClear(Vector3 direction)
    {
        Vector3 origin = transform.position + controller.center;
        float radius = controller.radius * transform.lossyScale.x * 0.9f;

        if (Physics.SphereCast(origin, radius, direction, out _, lookAhead, obstacleMask,
                               QueryTriggerInteraction.Ignore))
            return false;

        // Ground ahead? Measured from the feet so a step up doesn't read as a drop.
        Vector3 feet = transform.position + controller.center
                       + Vector3.down * (controller.height * 0.5f * transform.lossyScale.y);
        Vector3 ahead = feet + direction * lookAhead + Vector3.up * 0.3f;
        return Physics.Raycast(ahead, Vector3.down, 0.3f + maxDrop, obstacleMask,
                               QueryTriggerInteraction.Ignore);
    }

    private void Move()
    {
        if (controller.isGrounded)
            verticalVelocity = gravity * 0.3f;
        else
            verticalVelocity += gravity * Time.deltaTime;

        Vector3 motion = Vector3.up * verticalVelocity;
        if (state == State.Walking)
            motion += heading * walkSpeed;

        CollisionFlags hit = controller.Move(motion * Time.deltaTime);

        // Bumped into something the probe missed (another character, a thin table leg)
        if (state == State.Walking && (hit & CollisionFlags.Sides) != 0)
            PickHeading();
    }

    private void Animate()
    {
        if (state == State.Walking)
        {
            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;

            // Only flip on a clear sideways move, so walking straight toward the camera
            // doesn't flicker between the two rows
            if (cameraTransform != null)
            {
                float side = Vector3.Dot(heading, cameraTransform.right);
                if (Mathf.Abs(side) > 0.15f)
                    facingRight = side > 0f;
            }

            SetFrames(facingRight ? walkRightFrames : walkLeftFrames);
        }

        if (currentFrames == null || currentFrames.Length == 0 || spriteRenderer == null)
            return;

        float fps = state == State.Walking ? walkFps : sitFps;
        frameTimer += Time.deltaTime;
        if (frameTimer >= 1f / fps)
        {
            frameTimer = 0f;
            frameIndex = (frameIndex + 1) % currentFrames.Length;
            spriteRenderer.sprite = currentFrames[frameIndex];
        }
    }

    private void SetFrames(Sprite[] frames)
    {
        if (frames == currentFrames)
            return;

        currentFrames = frames;
        frameIndex = 0;
        frameTimer = 0f;
        if (spriteRenderer != null && frames != null && frames.Length > 0)
            spriteRenderer.sprite = frames[0];
    }

    private void UpdateMeow()
    {
        if (meowClip == null)
            return;

        meowTimer -= Time.deltaTime;
        if (meowTimer > 0f)
            return;

        meowTimer = Random.Range(meowInterval.x, meowInterval.y);

        if (player == null)
        {
            GameObject found = GameObject.FindGameObjectWithTag("Player");
            if (found == null)
                return;
            player = found.transform;
        }

        if ((player.position - transform.position).sqrMagnitude <= meowRange * meowRange)
            Meow();
    }

    private void Meow()
    {
        meowSource.pitch = Random.Range(0.92f, 1.08f);
        meowSource.PlayOneShot(meowClip);
    }

    /// <summary>
    /// Called by <see cref="ModelClickHandler"/> for every tap. If the tap lands on a cat's
    /// sprite, and nothing solid is in front of it, that cat stops, sits and meows.
    /// Returns true when a cat took the tap, so the click goes no further.
    /// </summary>
    public static bool TryClick(Ray ray, int occluderMask)
    {
        CatWander closest = null;
        float closestDistance = float.MaxValue;

        foreach (CatWander cat in cats)
        {
            if (cat.spriteRenderer == null || !cat.spriteRenderer.isVisible)
                continue;

            // The sprite, not the collider: the collider only covers the cat's lower body
            if (cat.spriteRenderer.bounds.IntersectRay(ray, out float distance) && distance < closestDistance)
            {
                closest = cat;
                closestDistance = distance;
            }
        }

        if (closest == null)
            return false;

        // A wall or piece of furniture between the camera and the cat takes the tap instead.
        // The cat's own layer is left out so its collider doesn't count as blocking it.
        int mask = occluderMask & ~(1 << closest.gameObject.layer);
        if (Physics.Raycast(ray, out RaycastHit hit, closestDistance, mask, QueryTriggerInteraction.Ignore))
            return false;

        closest.OnClicked();
        return true;
    }

    private void OnClicked()
    {
        // Don't stack meows on a cat that is still mid-meow
        if (meowClip == null || meowSource.isPlaying)
            return;

        Meow();
        Sit();

        // Clicking just made it meow; hold off the next unprompted one
        meowTimer = Random.Range(meowInterval.x, meowInterval.y);
    }

    private void OnEnable()
    {
        cats.Add(this);
    }

    private void OnDisable()
    {
        cats.Remove(this);

        if (meowSource != null)
            meowSource.Stop();
    }

    private void OnDestroy()
    {
        if (SoundManager.Instance != null)
            SoundManager.Instance.UnregisterSFXSource(meowSource);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(Application.isPlaying ? home : transform.position, wanderRadius);
    }
}

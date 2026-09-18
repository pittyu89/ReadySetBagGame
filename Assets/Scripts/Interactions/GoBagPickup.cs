using UnityEngine;

public class GoBagPickup : MonoBehaviour
{
    [SerializeField] private float floatSpeed = 2f;
    [SerializeField] private float floatHeight = 0.5f;
    [SerializeField] private RuntimeAnimatorController maleAnimatorController;
    [SerializeField] private RuntimeAnimatorController femaleAnimatorController;
    [Header("Small Bag")]
    [SerializeField] private RuntimeAnimatorController maleSmallBagAnimatorController;
    [SerializeField] private RuntimeAnimatorController femaleSmallBagAnimatorController;
    [Tooltip("Shown floating instead of the shine animation when the Small Bag is chosen.")]
    [SerializeField] private Sprite smallBagSprite;
    [Header("Medium Bag")]
    [SerializeField] private RuntimeAnimatorController maleMediumBagAnimatorController;
    [SerializeField] private RuntimeAnimatorController femaleMediumBagAnimatorController;
    [Tooltip("Shown floating instead of the shine animation when the Medium Bag is chosen.")]
    [SerializeField] private Sprite mediumBagSprite;
    [Tooltip("World size of every go-bag, floating or held overhead: each bag fits inside a square this big, keeping its own proportions.")]
    [SerializeField] private float bagWorldSize = 1f;
    [SerializeField] private AudioClip pickupAudio;

    [Header("Pickup Pose")]
    [SerializeField] private Sprite femalePickupSprite;
    [SerializeField] private Sprite malePickupSprite;
    [Tooltip("The standard bag, held overhead in the pickup pose.")]
    [SerializeField] private Sprite standardBagSprite;
    [Tooltip("How long the game stays paused on the pose.")]
    [SerializeField] private float pickupPoseDuration = 1.2f;
    [Tooltip("Where the bottom-centre of the bag held overhead sits, in pixels (+y is up): x from the middle of the head, y from the centre of the 32x48 female pickup image - a little way into the hair.")]
    [SerializeField] private Vector2 femaleHeldBagBottom = new Vector2(0f, 7f);
    [Tooltip("Same for the male pickup image, whose hair sits two pixels higher.")]
    [SerializeField] private Vector2 maleHeldBagBottom = new Vector2(0f, 9f);

    private const string SELECTED_CHARACTER_SUFFIX = "_SelectedCharacter";

    // Matches the bag order in DifficultyPanel's carousel
    private const int SMALL_BAG = 1;
    private const int MEDIUM_BAG = 2;
    
    private Animator animator;
    private Vector3 startPosition;
    private bool picked = false;
    private Camera mainCamera;

    private static GoBagPickup instance;

    void Start()
    {
        instance = this;
        animator = GetComponent<Animator>();

        Sprite bagSprite = FloatingBagSprite();
        if (bagSprite == null)
            animator.Play("Shine");

        ApplyBagSize(bagSprite);
        startPosition = transform.position;
    }

    /// <summary>
    /// A still sprite of the Small or Medium bag chosen in the difficulty panel, or null for
    /// the standard bag, which keeps its shine animation.
    /// </summary>
    private Sprite FloatingBagSprite()
    {
        switch (DifficultyPanel.GetActiveGoBag())
        {
            case SMALL_BAG: return smallBagSprite;
            case MEDIUM_BAG: return mediumBagSprite;
            default: return null;
        }
    }

    /// <summary>
    /// Sizes the floating bag to <see cref="bagWorldSize"/> with an even scale, so it keeps its
    /// real proportions (the scene object was squashed) and matches the bag held overhead.
    /// The pickup trigger keeps its world size, and the bag is lifted clear of the floor.
    /// </summary>
    private void ApplyBagSize(Sprite bagSprite)
    {
        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
            return;

        if (bagSprite != null)
        {
            animator.enabled = false;
            spriteRenderer.sprite = bagSprite;
        }

        Sprite measured = spriteRenderer.sprite;
        if (measured == null)
            return;

        Vector2 min, max;
        VisibleBounds(measured, out min, out max);
        Vector2 size = max - min;
        if (size.x <= 0f || size.y <= 0f)
            return;

        Vector3 oldScale = transform.localScale;
        float scale = bagWorldSize / Mathf.Max(size.x, size.y);
        transform.localScale = Vector3.one * scale;

        BoxCollider box3D = GetComponent<BoxCollider>();
        if (box3D != null)
        {
            Vector3 keepWorld = new Vector3(oldScale.x / scale, oldScale.y / scale, oldScale.z / scale);
            box3D.size = Vector3.Scale(box3D.size, keepWorld);
            box3D.center = Vector3.Scale(box3D.center, keepWorld);
        }

        // A bigger sprite grows downward from its centre pivot - lift it so its bottom clears
        // the floor, then pull the collider back so the trigger stays put
        float lift = GetFloorClearance(transform.position.y + min.y * scale);
        if (lift > 0f)
        {
            transform.position += Vector3.up * lift;
            if (box3D != null)
                box3D.center -= Vector3.up * (lift / scale);
        }
    }

    /// <summary>The bag's visible pixels in sprite-local units: the tight mesh skips the sheets' padding.</summary>
    public static void VisibleBounds(Sprite sprite, out Vector2 min, out Vector2 max)
    {
        min = new Vector2(float.MaxValue, float.MaxValue);
        max = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector2 v in sprite.vertices)
        {
            min = Vector2.Min(min, v);
            max = Vector2.Max(max, v);
        }
    }

    void Update()
    {
        if (!picked)
        {
            float newY = startPosition.y + Mathf.Abs(Mathf.Sin(Time.time * floatSpeed)) * floatHeight;
            transform.position = new Vector3(startPosition.x, newY, startPosition.z);
        }
    }

    void LateUpdate()
    {
        FaceCamera();
    }

    /// <summary>
    /// Turns the bag sprite to face the orbiting camera. Yaw only, like the character's
    /// BillboardToCamera: the bag stays upright, and it matches the camera's forward rather
    /// than pointing at the camera's position so the sprite never skews off-centre. Runs in
    /// LateUpdate so it uses this frame's camera, which Cinemachine moves after Update.
    /// </summary>
    private void FaceCamera()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null) return;
        }

        Vector3 camForward = mainCamera.transform.forward;
        camForward.y = 0f;

        if (camForward.sqrMagnitude > 1e-4f)
            transform.rotation = Quaternion.LookRotation(camForward.normalized, Vector3.up);
    }

    void OnTriggerEnter(Collider collision)
    {
        if (collision.CompareTag("Player") && !picked)
        {
            picked = true;

            // Play pickup audio
            if (pickupAudio != null && SoundManager.Instance != null)
            {
                SoundManager.Instance.PlaySFX(pickupAudio);
            }
            
            bool isMale = GetPlayerGender() == "Male";
            Animator playerAnimator = collision.GetComponentInChildren<Animator>();
            PlayerController movement = collision.GetComponent<PlayerController>();

            // The bag is in the character's hands now - hide it, but keep this object alive
            // until the pose is over so it can finish the pickup
            HideBag();

            Sprite pose = isMale ? malePickupSprite : femalePickupSprite;
            if (playerAnimator == null || pose == null)
            {
                FinishPickup(playerAnimator, isMale, movement);
                return;
            }

            if (movement != null)
                movement.SetMovementEnabled(false);

            // The pose sprite is drawn at the same pixels-per-unit as the walk sheets
            float pixel = 1f / pose.pixelsPerUnit;
            Sprite heldBag = GetHeldBagSprite();
            Vector2 bottom = (isMale ? maleHeldBagBottom : femaleHeldBagBottom) * pixel;

            BagPickupPose.Play(playerAnimator.gameObject, pose, heldBag, bottom, bagWorldSize, pickupPoseDuration,
                               () => FinishPickup(playerAnimator, isMale, movement));
        }
    }

    /// <summary>The chosen bag, drawn held overhead in the pickup pose (both poses have raised, empty hands).</summary>
    private Sprite GetHeldBagSprite()
    {
        switch (DifficultyPanel.GetActiveGoBag())
        {
            case SMALL_BAG: return smallBagSprite;
            case MEDIUM_BAG: return mediumBagSprite;
            default: return standardBagSprite;
        }
    }

    private void HideBag()
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
            r.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>())
            c.enabled = false;
        if (animator != null)
            animator.enabled = false;

        BlobShadow shadow = GetComponent<BlobShadow>();
        if (shadow != null)
            shadow.enabled = false;
    }

    /// <summary>Hands control back after the pose: carrying animations, the timer and the bag button.</summary>
    private void FinishPickup(Animator playerAnimator, bool isMale, PlayerController movement)
    {
        // Change player animator controller based on gender and chosen bag
        if (playerAnimator != null)
        {
            RuntimeAnimatorController controller = GetCarryingController(isMale);
            if (controller != null)
                playerAnimator.runtimeAnimatorController = controller;
        }

        if (movement != null)
            movement.SetMovementEnabled(true);

        // Start the timer
        GameTimer timer = FindObjectOfType<GameTimer>();
        if (timer != null)
        {
            timer.StartTimer();
        }

        // Show the bag button
        InventoryPanel handler = FindObjectOfType<InventoryPanel>();
        if (handler != null)
            handler.ShowBagButton();

        // Disable the go bag
        gameObject.SetActive(false);
    }

    /// <summary>
    /// How far a sprite whose bottom edge is at <paramref name="bottomY"/> must rise to sit
    /// just above the floor beneath the bag. 0 if it already clears it or there's no floor.
    /// </summary>
    private float GetFloorClearance(float bottomY)
    {
        const float floorGap = 0.02f;
        Vector3 origin = new Vector3(transform.position.x, transform.position.y + 2f, transform.position.z);

        float floorY = float.MinValue;
        foreach (RaycastHit hit in Physics.RaycastAll(origin, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore))
        {
            // The highest surface below the bag's centre, ignoring the bag itself
            if (hit.transform != transform && hit.point.y < transform.position.y && hit.point.y > floorY)
                floorY = hit.point.y;
        }

        if (floorY == float.MinValue)
            return 0f;

        return Mathf.Max(0f, floorY + floorGap - bottomY);
    }

    /// <summary>
    /// The walk animations for the bag picked in the difficulty panel. Falls back to the
    /// standard bag's animations if the chosen bag's controller isn't assigned.
    /// </summary>
    private RuntimeAnimatorController GetCarryingController(bool isMale)
    {
        RuntimeAnimatorController chosen = null;
        switch (DifficultyPanel.GetActiveGoBag())
        {
            case SMALL_BAG:
                chosen = isMale ? maleSmallBagAnimatorController : femaleSmallBagAnimatorController;
                break;
            case MEDIUM_BAG:
                chosen = isMale ? maleMediumBagAnimatorController : femaleMediumBagAnimatorController;
                break;
        }

        if (chosen != null)
            return chosen;

        return isMale ? maleAnimatorController : femaleAnimatorController;
    }

    private string GetPlayerGender()
    {
        bool isGuest = PlayerPrefs.GetString("IsGuest", "false") == "true";
        string userName = isGuest ? "Guest" : PlayerPrefs.GetString("StudentName", "User");
        string userKey = userName + SELECTED_CHARACTER_SUFFIX;
        return PlayerPrefs.GetString(userKey, "Female");
    }

    public static bool IsBagPickedUp()
    {
        return instance != null && instance.picked;
    }
}

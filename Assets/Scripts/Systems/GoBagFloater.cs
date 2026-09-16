using UnityEngine;

public class GoBagFloater : MonoBehaviour
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
    [Tooltip("Size of the small/medium bag sprite relative to the box the shine animation fills.")]
    [SerializeField] private float bagSpriteScale = 1f;
    [SerializeField] private AudioClip pickupAudio;

    private const string SELECTED_CHARACTER_SUFFIX = "_SelectedCharacter";

    // Matches the bag order in DifficultyPanelManager's carousel
    private const int SMALL_BAG = 1;
    private const int MEDIUM_BAG = 2;
    
    private Animator animator;
    private Vector3 startPosition;
    private bool picked = false;
    private Camera mainCamera;

    private static GoBagFloater instance;

    void Start()
    {
        instance = this;
        animator = GetComponent<Animator>();
        if (!ApplySelectedBagSprite())
            animator.Play("Shine");
        startPosition = transform.position;
    }

    /// <summary>
    /// Swaps the shine animation for a still sprite of the bag chosen in the difficulty panel.
    /// The sprite is scaled to fit the box the shine frames fill, since the bag sheets use a
    /// different pixels-per-unit, and the collider is scaled back so the pickup trigger keeps
    /// its size. Returns false for the standard bag, which keeps the shine.
    /// </summary>
    private bool ApplySelectedBagSprite()
    {
        Sprite bagSprite = null;
        switch (PlayerPrefs.GetInt(DifficultyPanelManager.SELECTED_GO_BAG_KEY, 0))
        {
            case SMALL_BAG: bagSprite = smallBagSprite; break;
            case MEDIUM_BAG: bagSprite = mediumBagSprite; break;
        }

        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
        if (bagSprite == null || spriteRenderer == null)
            return false;

        Vector2 box = spriteRenderer.sprite != null ? (Vector2)spriteRenderer.sprite.bounds.size : Vector2.one;
        Vector2 size = bagSprite.bounds.size;
        float factor = Mathf.Min(box.x / size.x, box.y / size.y) * bagSpriteScale;

        animator.enabled = false;
        spriteRenderer.sprite = bagSprite;
        transform.localScale *= factor;

        BoxCollider box3D = GetComponent<BoxCollider>();
        if (box3D != null)
        {
            box3D.size /= factor;
            box3D.center /= factor;
        }

        // A bigger sprite grows downward from its centre pivot - lift it so its bottom clears
        // the floor, then pull the collider back so the trigger stays put
        float lift = GetFloorClearance(transform.position.y + bagSprite.bounds.min.y * transform.lossyScale.y);
        if (lift > 0f)
        {
            transform.position += Vector3.up * lift;
            if (box3D != null)
                box3D.center -= Vector3.up * (lift / transform.lossyScale.y);
        }

        return true;
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
            
            // Get player gender
            string playerGender = GetPlayerGender();
            
            // Change player animator controller based on gender and chosen bag (search in children)
            Animator playerAnimator = collision.GetComponentInChildren<Animator>();
            if (playerAnimator != null)
            {
                RuntimeAnimatorController controller = GetCarryingController(playerGender == "Male");
                if (controller != null)
                {
                    playerAnimator.runtimeAnimatorController = controller;
                }
            }
            
            // Start the timer
            Timer timer = FindObjectOfType<Timer>();
            if (timer != null)
            {
                timer.StartTimer();
            }

            // Show the bag button
            InventoryPanelHandler handler = FindObjectOfType<InventoryPanelHandler>();
            if (handler != null)
                handler.ShowBagButton();
            
            // Disable the go bag
            gameObject.SetActive(false);
        }
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
        switch (PlayerPrefs.GetInt(DifficultyPanelManager.SELECTED_GO_BAG_KEY, 0))
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

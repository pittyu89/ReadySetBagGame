using UnityEngine;

public class GoBagFloater : MonoBehaviour
{
    [SerializeField] private float floatSpeed = 2f;
    [SerializeField] private float floatHeight = 0.5f;
    [SerializeField] private RuntimeAnimatorController maleAnimatorController;
    [SerializeField] private RuntimeAnimatorController femaleAnimatorController;
    [SerializeField] private AudioClip pickupAudio;
    
    private const string SELECTED_CHARACTER_SUFFIX = "_SelectedCharacter";
    
    private Animator animator;
    private Vector3 startPosition;
    private bool picked = false;
    private Camera mainCamera;

    private static GoBagFloater instance;

    void Start()
    {
        instance = this;
        animator = GetComponent<Animator>();
        animator.Play("Shine");
        startPosition = transform.position;
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
            
            // Change player animator controller based on gender (search in children)
            Animator playerAnimator = collision.GetComponentInChildren<Animator>();
            if (playerAnimator != null)
            {
                RuntimeAnimatorController controller = playerGender == "Male" ? maleAnimatorController : femaleAnimatorController;
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

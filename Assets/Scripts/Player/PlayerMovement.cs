using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float runSpeedMultiplier = 1.5f;
    [SerializeField] private float minSpeedMultiplier = 0.5f;  // Minimum speed at 100% bag capacity (0.5 = half speed)

    [Header("Gravity Settings")]
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float groundDrag = 0.3f;

    [Header("References")]
    [SerializeField] private Animator animator;

    [Header("Footstep Audio")]
    [SerializeField] private AudioClip footstepAudio;

    private CharacterController controller;
    private Vector3 moveDirection;

    // Raw stick/keyboard input. The walk animations are picked from this rather than from the
    // world-space moveDirection, because the camera can orbit: the sprite always faces the
    // camera, so "walking left" means left on screen, not toward world -X.
    private Vector2 inputDirection;
    private Vector3 velocity = Vector3.zero;
    private bool isRunning;
    private AudioSource footstepAudioSource;
    private bool isPlayingFootsteps = false;

    // Movement is camera-relative, so HandleInput needs the camera every frame. Camera.main
    // is a tagged lookup, so it is resolved once here and re-resolved only if the camera is
    // destroyed and replaced (scene reloads, cutscene cameras).
    private Transform cameraTransform;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        CacheCamera();
        
        // Create or get footstep audio source
        footstepAudioSource = GetComponent<AudioSource>();
        if (footstepAudioSource == null)
        {
            footstepAudioSource = gameObject.AddComponent<AudioSource>();
        }
        
        footstepAudioSource.clip = footstepAudio;
        footstepAudioSource.loop = true;
        footstepAudioSource.playOnAwake = false;

        // Hand the source to the SFX bus. It tracks the SFX volume from here on,
        // so there is nothing to poll per frame.
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.RegisterSFXSource(footstepAudioSource);
        }
    }

    void Update()
    {
        HandleInput();
        MoveCharacter();
        UpdateAnimation();
    }

    void CacheCamera()
    {
        Camera cam = Camera.main;
        cameraTransform = cam != null ? cam.transform : null;
    }

    void HandleInput()
    {
        // Get input from keyboard/gamepad OR mobile joystick
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        // If no keyboard input, use mobile joystick
        if (horizontal == 0 && vertical == 0)
        {
            horizontal = MobileJoystick.GetHorizontalInput();
            vertical = MobileJoystick.GetVerticalInput();
        }

        isRunning = Input.GetKey(KeyCode.LeftShift);

        if (cameraTransform == null)
        {
            CacheCamera();
            if (cameraTransform == null)
            {
                moveDirection = Vector3.zero;
                return;
            }
        }

        Vector3 forward = cameraTransform.forward;
        Vector3 right = cameraTransform.right;

        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        moveDirection = (forward * vertical + right * horizontal).normalized;
        inputDirection = new Vector2(horizontal, vertical);
    }

    void MoveCharacter()
    {
        // Apply gravity
        if (controller.isGrounded)
        {
            velocity.y = gravity * groundDrag;
        }
        else
        {
            velocity.y += gravity * Time.deltaTime;
        }

        // Calculate bag weight percentage to determine speed multiplier
        float bagWeightMultiplier = GetBagWeightSpeedMultiplier();

        // Calculate horizontal movement with bag weight penalty
        float currentSpeed = isRunning ? moveSpeed * runSpeedMultiplier : moveSpeed;
        currentSpeed *= bagWeightMultiplier;  // Apply bag weight reduction
        
        Vector3 movement = moveDirection * currentSpeed;
        
        // Add vertical velocity
        movement += Vector3.up * velocity.y;
        
        controller.Move(movement * Time.deltaTime);
    }

    private float GetBagWeightSpeedMultiplier()
    {
        if (InventoryManager.Instance == null)
            return 1f;  // No penalty if inventory manager not available

        float currentWeight = InventoryManager.Instance.GetGoBagTotalWeight();
        float weightLimit = InventoryManager.Instance.GetGoBagWeightLimit();

        // If no weight limit is set, return full speed
        if (weightLimit <= 0)
            return 1f;

        // Calculate weight percentage (0 to 1)
        float weightPercentage = Mathf.Clamp01(currentWeight / weightLimit);

        // Linear interpolation: at 0% fullness = 1.0 (no penalty), at 100% fullness = minSpeedMultiplier
        float speedMultiplier = Mathf.Lerp(1f, minSpeedMultiplier, weightPercentage);

        return speedMultiplier;
    }

    void UpdateAnimation()
    {
        if (animator != null)
        {
            if (moveDirection.magnitude < 0.1f)
            {
                animator.Play("Idle");
                StopFootsteps();
            }
            else
            {
                PlayFootsteps();
                
                if (Mathf.Abs(inputDirection.x) > Mathf.Abs(inputDirection.y))
                {
                    if (inputDirection.x > 0)
                    {
                        animator.Play("WalkRight");
                    }
                    else
                    {
                        animator.Play("WalkLeft");
                    }
                }
                else
                {
                    if (inputDirection.y > 0)
                    {
                        animator.Play("WalkUp");
                    }
                    else
                    {
                        animator.Play("WalkDown");
                    }
                }
            }
        }
    }

    void PlayFootsteps()
    {
        if (!isPlayingFootsteps && footstepAudioSource != null && footstepAudio != null)
        {
            footstepAudioSource.Play();
            isPlayingFootsteps = true;
        }
    }

    void StopFootsteps()
    {
        if (isPlayingFootsteps && footstepAudioSource != null)
        {
            footstepAudioSource.Stop();
            isPlayingFootsteps = false;
        }
    }

    public void SetMovementEnabled(bool enabled)
    {
        this.enabled = enabled;
    }

    private void OnDestroy()
    {
        StopFootsteps();

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.UnregisterSFXSource(footstepAudioSource);
        }
    }
}
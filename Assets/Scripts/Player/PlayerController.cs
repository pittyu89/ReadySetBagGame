using UnityEngine;

public class PlayerController : MonoBehaviour
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

    // Petting the cat: first a short walk to the spot beside it, then the pet loop.
    // Input is ignored for the whole of it.
    private enum PetPhase { None, Approaching, Petting }
    private const float PET_APPROACH_TIMEOUT = 0.75f;
    private PetPhase petPhase;
    private Vector3 petStandAt;
    private bool petFaceRight;
    private float petDuration;
    private float petTimer;
    private System.Action onPetReached;
    private System.Action onPetFinished;

    /// <summary>True while walking over to pet the cat or petting it.</summary>
    public bool IsPetting => petPhase != PetPhase.None;

    /// <summary>Where the camera frames the petting: between the player and the cat.</summary>
    public Vector3 PetFocus { get; private set; }

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
        if (petPhase != PetPhase.None)
        {
            UpdatePetting();
            return;
        }

        HandleInput();
        MoveCharacter();
        UpdateAnimation();
    }

    /// <summary>
    /// Walks to <paramref name="standAt"/>, then plays the pet loop facing the given way for
    /// <paramref name="duration"/> seconds. <paramref name="onReached"/> fires as the hand comes
    /// down (the cat starts its own loop then), <paramref name="onFinished"/> when it's over.
    /// <paramref name="focus"/> is the point the camera zooms in on meanwhile.
    /// Returns false, doing nothing, when the player can't move right now or is already petting.
    /// </summary>
    public bool TryStartPetting(Vector3 standAt, Vector3 focus, bool faceRight, float duration,
                                System.Action onReached, System.Action onFinished)
    {
        if (!enabled || petPhase != PetPhase.None || controller == null)
            return false;

        petPhase = PetPhase.Approaching;
        petStandAt = standAt;
        PetFocus = focus;
        petFaceRight = faceRight;
        petDuration = duration;
        petTimer = 0f;
        onPetReached = onReached;
        onPetFinished = onFinished;
        moveDirection = Vector3.zero;
        return true;
    }

    void UpdatePetting()
    {
        if (controller.isGrounded)
            velocity.y = gravity * groundDrag;
        else
            velocity.y += gravity * Time.deltaTime;

        Vector3 motion = Vector3.up * velocity.y;
        petTimer += Time.deltaTime;

        if (petPhase == PetPhase.Approaching)
        {
            Vector3 toSpot = petStandAt - transform.position;
            toSpot.y = 0f;
            float distance = toSpot.magnitude;

            // Something in the way? Pet from wherever the player got to
            if (distance < 0.05f || petTimer >= PET_APPROACH_TIMEOUT)
            {
                BeginPetLoop();
            }
            else
            {
                float step = Mathf.Min(moveSpeed, distance / Mathf.Max(Time.deltaTime, 1e-4f));
                motion += toSpot / distance * step;
                PlayFootsteps();

                if (animator != null)
                {
                    float side = cameraTransform != null ? Vector3.Dot(toSpot, cameraTransform.right) : 0f;
                    animator.Play(side >= 0f ? "WalkRight" : "WalkLeft");
                }
            }
        }
        else if (petTimer >= petDuration)
        {
            EndPetting();
        }

        controller.Move(motion * Time.deltaTime);
    }

    void BeginPetLoop()
    {
        petPhase = PetPhase.Petting;
        petTimer = 0f;
        StopFootsteps();

        if (animator != null)
            animator.Play(petFaceRight ? "PetRight" : "PetLeft", 0, 0f);

        System.Action reached = onPetReached;
        onPetReached = null;
        reached?.Invoke();
    }

    void EndPetting()
    {
        petPhase = PetPhase.None;

        System.Action finished = onPetFinished;
        onPetFinished = null;
        finished?.Invoke();
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

    private void OnDisable()
    {
        // A disabled controller doesn't update, so it would never stop the looping steps
        StopFootsteps();

        // Nor finish petting - let the cat go rather than leave it waiting
        if (petPhase != PetPhase.None)
            EndPetting();
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
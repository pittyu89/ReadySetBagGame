using UnityEngine;

public class BillboardToCamera : MonoBehaviour
{
    [Tooltip("Yaw only: the sprite stays upright in the world and turns to face the camera " +
             "horizontally. This lets it cast a correctly shaped real shadow. Off = full " +
             "LookAt, which tips the sprite back to the camera's pitch so it is seen square-on " +
             "but casts a distorted shadow.")]
    [SerializeField] private bool yawOnly = true;

    [Tooltip("Counteracts the foreshortening of standing the sprite upright. Viewed from a " +
             "40 degree camera an upright plane loses cos(40) = 0.77 of its height, so ~1.3 " +
             "restores the original on-screen size. Only applied when yawOnly is on.")]
    [SerializeField] private float uprightHeightCompensation = 1.3f;

    private Camera mainCamera;
    private Transform spriteTransform;   // scaled instead of the root, which owns the collider
    private Vector3 baseScale;
    private Vector3 baseLocalPos;
    private float spriteLocalHeight = 1f;
    private bool baseScaleCaptured;
    private bool appliedCompensation;

    void Start()
    {
        mainCamera = Camera.main;
        CaptureBaseScale();
    }

    /// <summary>
    /// Aligns the bottom of the sprite with the surface the character is actually standing on.
    ///
    /// Measured rather than computed: the sprite pivots at its centre, so scaling it for the
    /// upright view moves the feet, and every animation frame has slightly different bounds.
    /// Deriving the offset from a fixed sprite height leaves the character either sunk into
    /// the floor or hovering above it depending on the frame.
    ///
    /// Note the skinWidth term. A CharacterController never touches what it stands on - it
    /// rests one skinWidth clear of it (measured: 0.0800 for this capsule, matching skinWidth
    /// exactly, so the value is world-space and must NOT be multiplied by lossyScale like
    /// centre and height are). Aligning the sprite to the geometric bottom of the capsule
    /// therefore left the character hovering that same 0.08 above the floor, with its contact
    /// shadow - correctly drawn on the floor - sitting visibly below its feet.
    /// </summary>
    private void KeepFeetPlanted()
    {
        var sr = spriteTransform.GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null)
            return;

        var controller = GetComponent<CharacterController>();
        float baseY = controller != null
            ? transform.position.y
              + (controller.center.y - controller.height * 0.5f) * transform.lossyScale.y
              - controller.skinWidth
            : transform.position.y;

        float delta = baseY - sr.bounds.min.y;
        spriteTransform.position += Vector3.up * delta;
    }

    private void CaptureBaseScale()
    {
        if (baseScaleCaptured) return;

        // This component sits on the root, which also carries the CharacterController.
        // Scaling the root would resize the collider, so compensate on the sprite child.
        var sr = GetComponentInChildren<SpriteRenderer>(true);
        spriteTransform = sr != null ? sr.transform : transform;

        baseScale = spriteTransform.localScale;
        baseLocalPos = spriteTransform.localPosition;

        // Sprite height in local units, needed to keep the FEET planted when the height is
        // compensated. The sprite pivots at its centre, so scaling Y grows it symmetrically
        // and half the added height would otherwise push the feet through the floor.
        if (sr != null && sr.sprite != null)
            spriteLocalHeight = sr.sprite.bounds.size.y;

        baseScaleCaptured = true;
    }

    void LateUpdate()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null) return;
        }

        CaptureBaseScale();

        if (yawOnly)
        {
            // Align to the camera's FORWARD, not to the direction of the camera's position.
            // Facing the position makes the sprite yaw off-axis whenever the character is not
            // dead centre - and because the follow camera lags on lateral movement, that reads
            // as the character tilting as they run left or right. Matching the camera's forward
            // keeps the sprite plane parallel to the view plane, so it never skews.
            Vector3 camForward = mainCamera.transform.forward;
            camForward.y = 0f;

            if (camForward.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.LookRotation(camForward.normalized, Vector3.up);

            if (spriteTransform != null)
            {
                if (!appliedCompensation)
                {
                    spriteTransform.localScale = new Vector3(
                        baseScale.x,
                        baseScale.y * uprightHeightCompensation,
                        baseScale.z);
                    appliedCompensation = true;
                }

                KeepFeetPlanted();
            }
        }
        else
        {
            transform.LookAt(transform.position + mainCamera.transform.rotation * Vector3.forward,
                             mainCamera.transform.rotation * Vector3.up);

            if (appliedCompensation && spriteTransform != null)
            {
                spriteTransform.localScale = baseScale;
                spriteTransform.localPosition = baseLocalPos;
                appliedCompensation = false;
            }
        }
    }
}

using UnityEngine;

/// <summary>
/// Cuts a hole through walls standing between the camera and the character.
///
/// Drives the _RevealCenter / _RevealRadius globals read by Custom/StencilWall. That shader
/// cuts a cone along the camera->character axis, so the opening stays centred on the character
/// on screen. The centre here is the character's visual bounds centre, not its transform
/// pivot, which sits at the feet.
/// </summary>
public class RevealSphere : MonoBehaviour
{
    public GameObject target;
    public LayerMask wallLayerMask;
    public float scaleDuration = 0.2f;

    [Tooltip("Hole radius measured at the character's distance. Raised automatically if it " +
             "would be too small to contain the character.")]
    public float revealedScale = 2.8f;

    [Tooltip("Multiplier on the character's own bounding radius, used as the minimum hole " +
             "size so the whole character always fits inside the opening.")]
    public float fitPadding = 1.6f;

    [Tooltip("How far past the character the cut continues. Should normally stay at 0. " +
             "A wall that actually occludes the character sits BETWEEN them and the camera, so " +
             "every part of it is already inside the cut - the pad buys nothing there, and any " +
             "value above 0 starts holing walls the character is merely standing near.")]
    public float revealDepthPad = 0f;

    [Range(0f, 1f)]
    [Tooltip("Off by default, and best left there. It drops walls PAST the character that are " +
             "seen edge-on, but it cannot tell a leftover sliver from a legitimate room wall - " +
             "so it also punches holes in the wall behind the character and in walls to the " +
             "side. Raise it only if you specifically want that trade.")]
    public float grazeCutoff = 0f;

    private static readonly int RevealCenterID   = Shader.PropertyToID("_RevealCenter");
    private static readonly int RevealRadiusID   = Shader.PropertyToID("_RevealRadius");
    private static readonly int RevealDepthPadID = Shader.PropertyToID("_RevealDepthPad");
    private static readonly int RevealGrazeID    = Shader.PropertyToID("_RevealGrazeCutoff");

    private Renderer[] characterRenderers;
    private float targetScale;
    private float scaleStartTime;
    private float startScale;
    private float previousTargetScale = -1f;
    private float currentScale;

    // Resolved once rather than per-frame: Camera.main is a tagged lookup, and Update needs
    // the camera twice on every frame the reveal is active.
    private Camera mainCamera;

    void Start()
    {
        CacheCharacterRenderers();
        mainCamera = Camera.main;
    }

    void OnDisable()
    {
        // Never leave a hole punched in the walls behind us
        Shader.SetGlobalFloat(RevealRadiusID, 0f);
    }

    private void CacheCharacterRenderers()
    {
        // The visible character is a sibling/parent renderer, not this object
        Transform root = transform.parent != null ? transform.parent : transform;
        characterRenderers = root.GetComponentsInChildren<Renderer>(false);
    }

    /// <summary>
    /// Combined world bounds of the visible character, so the hole can be centred on what the
    /// player actually sees rather than on the transform pivot at their feet.
    /// </summary>
    private bool TryGetCharacterBounds(out Bounds bounds)
    {
        bounds = new Bounds();
        bool found = false;

        if (characterRenderers == null)
            return false;

        for (int i = 0; i < characterRenderers.Length; i++)
        {
            Renderer r = characterRenderers[i];
            if (r == null || !r.enabled) continue;

            if (!found) { bounds = r.bounds; found = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return found;
    }

    void Update()
    {
        if (target == null)
            return;

        // The camera can be swapped at runtime (scene reload, cutscenes), so re-resolve if the
        // cached one has gone away.
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
                return;
        }

        // Renderers can be swapped at runtime (CharacterSpawner replaces the character)
        if (characterRenderers == null || characterRenderers.Length == 0)
            CacheCharacterRenderers();

        Vector3 cameraPosition = mainCamera.transform.position;

        // Centre on the visible character; fall back to the transform if it has no renderer
        Bounds characterBounds;
        bool haveBounds = TryGetCharacterBounds(out characterBounds);
        Vector3 revealCenter = haveBounds ? characterBounds.center : target.transform.position;

        // Big enough to contain the whole character, or the authored size if that is larger
        float fitRadius = haveBounds ? characterBounds.extents.magnitude * fitPadding : revealedScale;
        float openRadius = Mathf.Max(revealedScale, fitRadius);

        Vector3 directionToTarget = (revealCenter - cameraPosition).normalized;
        float distanceToTarget = Vector3.Distance(cameraPosition, revealCenter);

        RaycastHit hit;
        bool occluded = Physics.Raycast(cameraPosition, directionToTarget, out hit,
                                        distanceToTarget, wallLayerMask);

        targetScale = occluded ? openRadius : 0f;

        if (targetScale != previousTargetScale)
        {
            startScale = currentScale;
            scaleStartTime = Time.time;
            previousTargetScale = targetScale;
        }

        float elapsed = Time.time - scaleStartTime;
        float progress = scaleDuration > 0f ? Mathf.Clamp01(elapsed / scaleDuration) : 1f;
        currentScale = Mathf.Lerp(startScale, targetScale, progress);

        Shader.SetGlobalVector(RevealCenterID, revealCenter);
        Shader.SetGlobalFloat(RevealRadiusID, currentScale);
        Shader.SetGlobalFloat(RevealDepthPadID, revealDepthPad);
        Shader.SetGlobalFloat(RevealGrazeID, grazeCutoff);
    }
}

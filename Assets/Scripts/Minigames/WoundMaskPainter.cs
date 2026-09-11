using UnityEngine;

/// <summary>
/// Owns the runtime wound mask for the first-aid minigame and rubs it out where the
/// player swabs.
///
/// The authored Mask.png is never touched. A working copy is built in memory at a smaller
/// resolution and handed to the material, so the same asset comes back untouched on the
/// next run and nothing is written to disk.
///
/// The copy is single channel and only the brush's own footprint is rewritten per stroke,
/// because Texture2D.Apply uploads the whole texture and doing that at the source's
/// 1024 square every frame of a drag would cost more than the rest of the minigame put
/// together on a phone.
/// </summary>
public class WoundMaskPainter : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The arm. Its material must use Custom/ArmWound.")]
    [SerializeField] private Renderer armRenderer;
    [Tooltip("Authored black-and-white mask. Needs Read/Write enabled in its importer, " +
             "since the working copy is read out of it at startup.")]
    [SerializeField] private Texture2D sourceMask;

    [Header("Working Copy")]
    [Tooltip("Side length of the in-memory mask. Half the source is plenty — the bruises " +
             "are soft blobs, and this is what gets uploaded on every brush stroke.")]
    [SerializeField] private int workingResolution = 512;

    [Header("Brush")]
    [Tooltip("Radius of one swab dab, as a fraction of the mask's width.")]
    [SerializeField, Range(0.005f, 0.25f)] private float brushRadius = 0.055f;
    [Tooltip("0 is a hard-edged dab, 1 fades out across the whole radius. A soft edge " +
             "keeps a stroke from leaving a visible circle rim behind.")]
    [SerializeField, Range(0f, 1f)] private float brushSoftness = 0.65f;
    [Tooltip("How much a single frame of contact removes. Below 1 the bruise fades out " +
             "over a few passes rather than vanishing the instant it is touched.")]
    [SerializeField, Range(0.05f, 1f)] private float brushStrength = 0.5f;

    private static readonly int WoundMaskId = Shader.PropertyToID("_WoundMask");

    private Texture2D workingMask;

    // One byte per texel, matching the R8 working copy. Kept as raw bytes rather than
    // Color32 because SetPixels32 is not dependable on a single-channel format.
    private byte[] pixels;
    private Material armMaterial;

    // Total mask brightness at the start, and what is left of it. Kept as a running sum
    // rather than rescanned, so progress costs nothing per frame.
    private float initialDirt = 0f;
    private float remainingDirt = 0f;

    /// <summary>0 when the arm is as bruised as it started, 1 once it is wiped clean.</summary>
    public float Cleanliness =>
        initialDirt <= 0f ? 1f : Mathf.Clamp01(1f - remainingDirt / initialDirt);

    private void Awake()
    {
        BuildWorkingCopy();
    }

    private void OnDestroy()
    {
        // Both are created here rather than loaded, so nothing else will free them
        if (workingMask != null)
            Destroy(workingMask);

        if (armMaterial != null)
            Destroy(armMaterial);
    }

    /// <summary>
    /// Rebuilds the mask from the authored asset, putting every bruise back. Call before
    /// each run so a second play does not start on an arm the last player already cleaned.
    /// </summary>
    public void ResetMask()
    {
        if (workingMask == null)
        {
            BuildWorkingCopy();
            return;
        }

        CopyFromSource();
        workingMask.SetPixelData(pixels, 0);
        workingMask.Apply(false);
        remainingDirt = initialDirt;
    }

    /// <summary>
    /// Wipes at one spot on the arm, given in the mesh's own UV space. Returns true if
    /// this dab actually removed anything, so the caller can hold off on the swabbing
    /// sound when the player is scrubbing clean skin.
    /// </summary>
    public bool PaintAt(Vector2 uv, float deltaTime)
    {
        if (workingMask == null)
            return false;

        int size = workingMask.width;
        float cx = uv.x * size;
        float cy = uv.y * size;
        float radius = brushRadius * size;

        int minX = Mathf.Max(0, Mathf.FloorToInt(cx - radius));
        int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(cx + radius));
        int minY = Mathf.Max(0, Mathf.FloorToInt(cy - radius));
        int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(cy + radius));

        if (minX > maxX || minY > maxY)
            return false;

        // Framerate independent, so a swipe removes the same amount on a slow phone
        float strength = Mathf.Clamp01(brushStrength * deltaTime * 60f);
        float inner = radius * (1f - brushSoftness);
        float removed = 0f;

        for (int y = minY; y <= maxY; y++)
        {
            int row = y * size;

            for (int x = minX; x <= maxX; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                if (distance > radius)
                    continue;

                // 1 at the middle of the dab, easing to 0 at its rim
                float falloff = radius <= inner
                    ? 1f
                    : Mathf.Clamp01(1f - (distance - inner) / (radius - inner));

                int index = row + x;
                byte before = pixels[index];
                if (before == 0)
                    continue;

                byte after = (byte)Mathf.Max(0f, before - 255f * strength * falloff);
                if (after >= before)
                    continue;

                removed += (before - after) / 255f;
                pixels[index] = after;
            }
        }

        if (removed <= 0f)
            return false;

        remainingDirt = Mathf.Max(0f, remainingDirt - removed);

        // Only the touched block is rewritten; Apply still uploads the whole (small)
        // texture, which is why the working copy is kept well under the source's size.
        workingMask.SetPixelData(pixels, 0);
        workingMask.Apply(false);

        return true;
    }

    private void BuildWorkingCopy()
    {
        if (sourceMask == null)
        {
            Debug.LogWarning("[WoundMaskPainter] No source mask assigned — nothing to clean.", this);
            return;
        }

        int size = Mathf.Max(32, workingResolution);

        workingMask = new Texture2D(size, size, TextureFormat.R8, false)
        {
            name = "WoundMask (runtime)",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        pixels = new byte[size * size];

        CopyFromSource();

        initialDirt = 0f;
        foreach (byte p in pixels)
            initialDirt += p / 255f;

        remainingDirt = initialDirt;

        workingMask.SetPixelData(pixels, 0);
        workingMask.Apply(false);

        if (armRenderer != null)
        {
            // The instance, not the shared asset: writing _WoundMask on the shared
            // material would leave the mask pointing at a destroyed texture after a run.
            armMaterial = armRenderer.material;
            armMaterial.SetTexture(WoundMaskId, workingMask);
        }
    }

    /// <summary>
    /// Samples the authored mask down into the working buffer. Bilinear, so the soft edges
    /// of the painted bruises survive the smaller resolution.
    /// </summary>
    private void CopyFromSource()
    {
        int size = workingMask.width;

        for (int y = 0; y < size; y++)
        {
            float v = (y + 0.5f) / size;
            int row = y * size;

            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                pixels[row + x] = (byte)Mathf.RoundToInt(Mathf.Clamp01(sourceMask.GetPixelBilinear(u, v).r) * 255f);
            }
        }
    }
}

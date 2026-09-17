using System;
using UnityEngine;

/// <summary>
/// The "picked up the go-bag" moment: the game freezes while the character holds their pickup
/// pose (with the chosen bag drawn in their hands), then play resumes.
///
/// Added to the character's sprite object for the length of the pose and removed afterwards.
/// Counts unscaled time because the game itself is paused meanwhile.
/// </summary>
public class BagPickupPose : MonoBehaviour
{
    /// <summary>True while a pickup pose is on screen; the camera ignores drags meanwhile.</summary>
    public static bool IsPlaying => activeCount > 0;
    private static int activeCount;
    private bool countReleased;

    private SpriteRenderer spriteRenderer;
    private Animator animator;
    private GameObject bagOverlay;
    private float duration;
    private float elapsed;
    private float previousTimeScale;
    private Action onFinished;

    /// <summary>
    /// Starts the pose on <paramref name="spriteObject"/>. <paramref name="bagSprite"/> is drawn
    /// with its bottom-centre at <paramref name="bagBottom"/> (units from the pose sprite's
    /// centre), fitted into a <paramref name="bagWorldSize"/> world-unit square with its real
    /// proportions; pass null to show the pose on its own.
    /// </summary>
    public static BagPickupPose Play(GameObject spriteObject, Sprite poseSprite, Sprite bagSprite, Vector2 bagBottom,
                                     float bagWorldSize, float duration, Action onFinished)
    {
        BagPickupPose pose = spriteObject.AddComponent<BagPickupPose>();
        pose.Begin(poseSprite, bagSprite, bagBottom, bagWorldSize, duration, onFinished);
        return pose;
    }

    private void Begin(Sprite poseSprite, Sprite bagSprite, Vector2 bagBottom, float bagWorldSize,
                       float duration, Action onFinished)
    {
        this.duration = duration;
        this.onFinished = onFinished;
        activeCount++;

        spriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();

        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        // The walk animation would overwrite the pose on its next update
        if (animator != null)
            animator.enabled = false;

        if (spriteRenderer != null && poseSprite != null)
        {
            spriteBeforePose = spriteRenderer.sprite;

            // BillboardToCamera plants the bottom of the sprite's frame on the floor. The pose
            // images have empty rows under the feet (the walk frames are cropped tight), which
            // left the character floating - so show a copy cropped to the visible pixels.
            Vector2 trimmedCenter;
            float headCenterX;
            spriteRenderer.sprite = TrimToVisiblePixels(poseSprite, out trimmedCenter, out headCenterX);

            // bagBottom's y is measured from the original frame's centre, so it moves onto the
            // cropped one. Its x is an offset from the middle of the head, which isn't always the
            // middle of the frame (the pose art sits half a pixel to a pixel right of it).
            bagBottom = new Vector2(bagBottom.x + headCenterX, bagBottom.y - trimmedCenter.y);
        }

        if (bagSprite != null && spriteRenderer != null)
            bagOverlay = CreateBagOverlay(bagSprite, bagBottom, bagWorldSize);
    }

    private Sprite trimmedPose;
    private Sprite spriteBeforePose;

    // How many rows from the top of the head are measured to find its middle
    private const int HEAD_ROWS = 6;

    /// <summary>
    /// A copy of <paramref name="sprite"/> cut down to its visible pixels, pivoting at its
    /// centre. <paramref name="center"/> is where that centre sits relative to the original
    /// sprite's pivot, and <paramref name="headCenterX"/> is the middle of the top of the head
    /// relative to the copy's centre, both in local units. Needs a readable texture: the sprite
    /// mesh outline is a couple of pixels loose, which would still leave the feet floating.
    /// </summary>
    private Sprite TrimToVisiblePixels(Sprite sprite, out Vector2 center, out float headCenterX)
    {
        center = Vector2.zero;
        headCenterX = 0f;
        float ppu = sprite.pixelsPerUnit;
        Rect frame = sprite.rect;

        RectInt visible;
        if (!sprite.texture.isReadable || !FindVisiblePixels(sprite.texture, frame, out visible))
            return sprite;

        float x = visible.x, y = visible.y, w = visible.width, h = visible.height;

        // Re-measure from the rounded pixel rect so the bag lines up exactly
        center = new Vector2((x + w * 0.5f - frame.x - sprite.pivot.x) / ppu, (y + h * 0.5f - frame.y - sprite.pivot.y) / ppu);
        headCenterX = (HeadCenterPixelX(sprite.texture, visible) - (x + w * 0.5f)) / ppu;

        trimmedPose = Sprite.Create(sprite.texture, new Rect(x, y, w, h), new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect);
        trimmedPose.name = sprite.name + " (trimmed)";
        return trimmedPose;
    }

    /// <summary>
    /// The middle of the top of the head in texture pixels (a pixel's left edge is its x):
    /// the average centre of the top <see cref="HEAD_ROWS"/> rows of visible pixels. The top
    /// rows are hair only, so the arms don't pull it sideways.
    /// </summary>
    private static float HeadCenterPixelX(Texture2D texture, RectInt visible)
    {
        Color32[] pixels = texture.GetPixels32();
        float sum = 0f;
        int rows = 0;

        // Texture rows count up from the bottom, so the head is at the top of the rect
        for (int py = visible.yMax - 1; py >= visible.y && rows < HEAD_ROWS; py--)
        {
            int minX = int.MaxValue, maxX = int.MinValue;
            for (int px = visible.x; px < visible.xMax; px++)
            {
                if (pixels[py * texture.width + px].a == 0)
                    continue;
                if (px < minX) minX = px;
                if (px > maxX) maxX = px;
            }

            if (maxX < minX)
                continue;

            sum += (minX + maxX + 1) * 0.5f;
            rows++;
        }

        return rows > 0 ? sum / rows : visible.x + visible.width * 0.5f;
    }

    /// <summary>The smallest pixel rect inside <paramref name="frame"/> holding every non-transparent pixel.</summary>
    private static bool FindVisiblePixels(Texture2D texture, Rect frame, out RectInt visible)
    {
        int fx = Mathf.RoundToInt(frame.x), fy = Mathf.RoundToInt(frame.y);
        int fw = Mathf.RoundToInt(frame.width), fh = Mathf.RoundToInt(frame.height);
        Color32[] pixels = texture.GetPixels32();
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

        for (int py = fy; py < fy + fh; py++)
        {
            for (int px = fx; px < fx + fw; px++)
            {
                if (pixels[py * texture.width + px].a == 0)
                    continue;

                if (px < minX) minX = px;
                if (px > maxX) maxX = px;
                if (py < minY) minY = py;
                if (py > maxY) maxY = py;
            }
        }

        visible = maxX >= minX ? new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1) : new RectInt();
        return maxX >= minX;
    }

    private GameObject CreateBagOverlay(Sprite bagSprite, Vector2 bottom, float worldSize)
    {
        GameObject overlay = new GameObject("PickupBag");
        overlay.layer = gameObject.layer;
        overlay.transform.SetParent(transform, false);

        SpriteRenderer bag = overlay.AddComponent<SpriteRenderer>();
        bag.sprite = bagSprite;
        bag.sharedMaterial = spriteRenderer.sharedMaterial;
        bag.sortingLayerID = spriteRenderer.sortingLayerID;
        bag.sortingOrder = spriteRenderer.sortingOrder + 1;
        bag.shadowCastingMode = spriteRenderer.shadowCastingMode;

        // Size and centre on the bag's visible pixels, not its frame: the bag sheets pad each
        // frame with empty space. A tight sprite mesh's vertices wrap exactly those pixels.
        Vector2 min, max;
        GoBagFloater.VisibleBounds(bagSprite, out min, out max);

        Vector2 size = max - min;
        Vector2 center = (min + max) * 0.5f;
        if (size.x <= 0f || size.y <= 0f)
            return overlay;

        // Same world size as the floating bag. The character sprite is scaled unevenly (the
        // prefab's scale plus BillboardToCamera's height compensation), so each axis undoes its
        // own parent scale - otherwise the bag would inherit the character's stretch.
        float worldScale = worldSize / Mathf.Max(size.x, size.y);
        Vector3 parentScale = transform.lossyScale;
        float scaleX = worldScale / Mathf.Max(1e-5f, Mathf.Abs(parentScale.x));
        float scaleY = worldScale / Mathf.Max(1e-5f, Mathf.Abs(parentScale.y));

        // Bottom-centre of the visible bag lands on the given point
        overlay.transform.localScale = new Vector3(scaleX, scaleY, scaleX);
        overlay.transform.localPosition = new Vector3(bottom.x - center.x * scaleX,
                                                      bottom.y + size.y * scaleY * 0.5f - center.y * scaleY,
                                                      -0.01f);
        return overlay;
    }

    void Update()
    {
        elapsed += Time.unscaledDeltaTime;

        if (elapsed >= duration)
            Finish();
    }

    private void Finish()
    {
        if (bagOverlay != null)
            Destroy(bagOverlay);

        // Put a real frame back straight away: the cropped copy is destroyed with this
        // component, before the re-enabled animator has drawn its first frame
        if (spriteRenderer != null && spriteBeforePose != null)
            spriteRenderer.sprite = spriteBeforePose;

        if (animator != null)
            animator.enabled = true;

        Time.timeScale = previousTimeScale;

        Action callback = onFinished;
        onFinished = null;
        // Let the camera move again right away rather than when the component is destroyed
        activeCount = Mathf.Max(0, activeCount - 1);
        countReleased = true;
        Destroy(this);
        callback?.Invoke();
    }

    void OnDestroy()
    {
        if (!countReleased)
            activeCount = Mathf.Max(0, activeCount - 1);

        if (trimmedPose != null)
        {
            if (spriteRenderer != null && spriteRenderer.sprite == trimmedPose)
                spriteRenderer.sprite = spriteBeforePose;
            Destroy(trimmedPose);
        }

        // Left mid-pose (scene change): never leave the game frozen
        if (onFinished != null)
            Time.timeScale = previousTimeScale;
    }
}

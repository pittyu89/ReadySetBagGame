using UnityEngine;

/// <summary>
/// Floats a small billboarded arrow above a clickable prop.
///
/// Leaves the prop's own material untouched and borrows the language players already learn
/// from the go-bag: something that floats and bobs is something you can interact with.
///
/// The arrow keeps one constant colour. It marks "this can be searched" and nothing else -
/// changing colour by distance read as the marker flickering between states rather than as a
/// steady, trustworthy signal.
///
/// The icon lives at the scene root rather than as a child of the prop, so it cannot inherit a
/// prop's non-uniform scale or rotation and end up squashed.
/// </summary>
[DisallowMultipleComponent]
public class ClickableIndicator : MonoBehaviour
{
    private static Sprite defaultSprite;
    private static Transform sharedContainer;
    private static Material overlayMaterial;

    private Transform iconTransform;
    private SpriteRenderer iconRenderer;
    private Camera iconCamera;

    private Color iconColor = new Color(0.36f, 0.88f, 0.40f, 1f);
    private float iconSize = 0.5f;
    private float heightOffset = 0.55f;
    private float bobAmplitude = 0.08f;
    private float bobSpeed = 2.5f;
    private float fadeDuration = 0.25f;

    private Vector3 anchor;              // top of the prop, before the height offset
    private bool anchorResolved;
    private bool wantVisible;
    private float fade;                  // 0 = fully faded out, 1 = fully visible

    /// <summary>
    /// Pushes appearance settings down from ClickableHighlightManager, so all the tuning lives
    /// on one inspector rather than on 19 separate props.
    /// </summary>
    public void Configure(Sprite sprite, Color color, float worldSize, float height,
                          float bobHeight, float bobRate, float fadeSeconds)
    {
        iconColor = color;
        iconSize = worldSize;
        heightOffset = height;
        bobAmplitude = bobHeight;
        bobSpeed = bobRate;
        fadeDuration = fadeSeconds;

        EnsureIcon(sprite);
    }

    /// <summary>
    /// World point the marker hovers over. The manager raycasts to this to decide whether a
    /// wall is in the way, since the marker itself does not depth-test.
    /// </summary>
    public Vector3 AnchorPosition
    {
        get
        {
            if (!anchorResolved)
                ResolveAnchor();
            return anchor + new Vector3(0f, heightOffset, 0f);
        }
    }

    /// <summary>Shows or hides the marker. The change is faded, not instant.</summary>
    public void SetVisible(bool visible)
    {
        wantVisible = visible;
    }

    private void EnsureIcon(Sprite sprite)
    {
        if (iconRenderer != null)
        {
            if (sprite != null) iconRenderer.sprite = sprite;
            return;
        }

        if (sharedContainer == null)
        {
            GameObject container = GameObject.Find("ClickableIndicators");
            if (container == null)
                container = new GameObject("ClickableIndicators");
            container.transform.position = Vector3.zero;
            container.transform.rotation = Quaternion.identity;
            container.transform.localScale = Vector3.one;
            sharedContainer = container.transform;
        }

        GameObject iconObject = new GameObject("Indicator_" + gameObject.name);
        iconObject.transform.SetParent(sharedContainer, false);

        iconRenderer = iconObject.AddComponent<SpriteRenderer>();
        iconRenderer.sprite = sprite != null ? sprite : GetDefaultSprite();
        iconRenderer.sortingOrder = 100;

        // Drawn on top of the scene rather than depth-tested. Sitting just above a prop, a
        // depth-tested marker clips through the countertop or cabinet it belongs to and shows
        // up half-buried. Walls are handled by the manager's line-of-sight check instead.
        iconRenderer.sharedMaterial = GetOverlayMaterial();
        iconRenderer.enabled = false;

        iconTransform = iconObject.transform;
    }

    /// <summary>
    /// Resolves the world point the icon hovers over: the actual top of the prop's combined
    /// renderer bounds, so the marker sits above a fridge or bookshelf rather than partway up
    /// its front face.
    /// </summary>
    private void ResolveAnchor()
    {
        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        bool any = false;

        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }

        if (!any)
        {
            Collider c = GetComponent<Collider>();
            if (c == null) c = GetComponentInChildren<Collider>();
            if (c != null) { bounds = c.bounds; any = true; }
        }

        anchor = any
            ? new Vector3(bounds.center.x, bounds.max.y, bounds.center.z)
            : transform.position;

        anchorResolved = true;
    }

    void LateUpdate()
    {
        if (iconRenderer == null)
            return;

        float target = wantVisible ? 1f : 0f;
        if (!Mathf.Approximately(fade, target))
        {
            fade = fadeDuration > 0f
                ? Mathf.MoveTowards(fade, target, Time.deltaTime / fadeDuration)
                : target;
        }

        // Stay enabled through the fade-out, and only switch off once fully transparent.
        if (fade <= 0f)
        {
            if (iconRenderer.enabled)
                iconRenderer.enabled = false;
            return;
        }

        if (!iconRenderer.enabled)
            iconRenderer.enabled = true;

        if (!anchorResolved)
            ResolveAnchor();

        if (iconCamera == null)
            iconCamera = Camera.main;

        Color c = iconColor;
        c.a *= fade;
        iconRenderer.color = c;

        // One shared bob phase, so every marker rises and falls together and reads as a
        // deliberate game signal rather than assorted flickering objects.
        float bob = Mathf.Sin(Time.time * bobSpeed) * bobAmplitude;
        iconTransform.position = anchor + new Vector3(0f, heightOffset + bob, 0f);

        if (iconCamera != null)
            iconTransform.rotation = iconCamera.transform.rotation;

        iconTransform.localScale = Vector3.one * iconSize;
    }

    void OnDisable()
    {
        wantVisible = false;
        fade = 0f;
        if (iconRenderer != null)
            iconRenderer.enabled = false;
    }

    void OnDestroy()
    {
        if (iconTransform != null)
            Destroy(iconTransform.gameObject);
    }

    /// <summary>
    /// Shared draw-on-top material for every marker. Falls back to the stock sprite material
    /// if the shader is missing, so a failed lookup means depth-tested icons rather than the
    /// magenta "shader not found" quad.
    /// </summary>
    private static Material GetOverlayMaterial()
    {
        if (overlayMaterial != null)
            return overlayMaterial;

        Shader shader = Shader.Find("ReadySetBag/ClickableIndicator");
        if (shader == null)
        {
            Debug.LogWarning("ClickableIndicator: overlay shader not found, markers will be " +
                             "depth-tested and may clip into furniture.");
            shader = Shader.Find("Sprites/Default");
        }

        overlayMaterial = new Material(shader);
        return overlayMaterial;
    }

    /// <summary>
    /// Builds a downward arrow with softly rounded corners and no outline.
    ///
    /// Drawn white so SpriteRenderer.color decides the final colour, and generated from a
    /// signed distance field rather than by filling pixels, which is what lets the corners
    /// round off and the edges stay smooth instead of stair-stepping.
    /// Assign a sprite on ClickableHighlightManager to replace it with your own art.
    /// </summary>
    private static Sprite GetDefaultSprite()
    {
        if (defaultSprite != null)
            return defaultSprite;

        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;   // smooth, so the rounded corners read as round
        tex.wrapMode = TextureWrapMode.Clamp;

        // Triangle is inset so that expanding it by the corner radius still fits the texture.
        Vector2 apex = new Vector2(0.50f, 0.24f);
        Vector2 topLeft = new Vector2(0.25f, 0.73f);
        Vector2 topRight = new Vector2(0.75f, 0.73f);

        const float cornerRadius = 0.085f;
        const float edgeSoftness = 1.5f / size;

        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);

                // Expanding the outline distance by the radius rounds every corner at once.
                float d = SignedTriangleDistance(p, apex, topLeft, topRight) - cornerRadius;
                float alpha = 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(-edgeSoftness, edgeSoftness, d));

                pixels[y * size + x] = new Color32(255, 255, 255,
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        // pixelsPerUnit == size makes the sprite exactly one world unit, so localScale alone
        // controls its on-screen size.
        defaultSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return defaultSprite;
    }

    /// <summary>Distance to the triangle outline, negative inside.</summary>
    private static float SignedTriangleDistance(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d = Mathf.Min(DistanceToSegment(p, a, b),
                  Mathf.Min(DistanceToSegment(p, b, c), DistanceToSegment(p, c, a)));
        return IsInsideTriangle(p, a, b, c) ? -d : d;
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 pa = p - a;
        Vector2 ba = b - a;
        float lengthSq = Vector2.Dot(ba, ba);
        float h = lengthSq > 0f ? Mathf.Clamp01(Vector2.Dot(pa, ba) / lengthSq) : 0f;
        return (pa - ba * h).magnitude;
    }

    private static bool IsInsideTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(p - a, b - a);
        float d2 = Cross(p - b, c - b);
        float d3 = Cross(p - c, a - c);
        bool anyNegative = d1 < 0f || d2 < 0f || d3 < 0f;
        bool anyPositive = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(anyNegative && anyPositive);
    }

    private static float Cross(Vector2 u, Vector2 v)
    {
        return u.x * v.y - u.y * v.x;
    }
}

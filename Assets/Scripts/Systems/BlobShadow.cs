using UnityEngine;

/// <summary>
/// Drops a soft contact shadow beneath a sprite so it reads as standing on the floor rather
/// than floating in front of it.
///
/// The character and the GoBag are unlit sprites, so they neither cast nor receive real
/// shadows. This fills that gap: a quad is projected onto whatever surface is below, growing
/// larger and fainter as the object rises - the same cue a real soft shadow gives.
/// </summary>
[ExecuteAlways]
public class BlobShadow : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("What the shadow sits under. Defaults to this object.")]
    [SerializeField] private Transform followTarget;

    [Header("Ground Detection")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float castStartHeight = 0.6f;
    [SerializeField] private float maxDropDistance = 8f;
    [Tooltip("Lift off the surface to prevent z-fighting.")]
    [SerializeField] private float surfaceOffset = 0.02f;

    [Header("Appearance")]
    [SerializeField] private float baseSize = 1.6f;
    [SerializeField, Range(0f, 1f)] private float baseOpacity = 0.45f;
    [SerializeField] private Color shadowColor = new Color(0.08f, 0.07f, 0.06f, 1f);
    [Tooltip("Lower = broader, more solid disc. Above ~2 the shadow shrinks to a faint dot.")]
    [SerializeField, Range(0.2f, 4f)] private float edgeFalloff = 1.1f;

    [Header("Silhouette")]
    [Tooltip("Cast the character's own sprite as the shadow shape instead of a round blob. " +
             "Follows the current animation frame.")]
    [SerializeField] private bool useSpriteShape = true;
    [Tooltip("How far the silhouette is squashed along the ground. 1 = full height, lower = " +
             "more foreshortened, as if the light were more overhead.")]
    [SerializeField, Range(0.2f, 1.5f)] private float lengthSquash = 0.62f;
    [Tooltip("How much the far end of the silhouette dissolves. 0 = crisp cutout.")]
    [SerializeField, Range(0f, 1f)] private float silhouetteSoftness = 0.15f;

    [Tooltip("Lay the silhouette away from the sun rather than away from the camera.")]
    [SerializeField] private bool alignToSunDirection = true;

    [Tooltip("Which light the shadow falls away from. Leave empty to use the scene's sun, or " +
             "the brightest usable directional light if no sun is assigned.")]
    [SerializeField] private Light shadowLight;

    [Tooltip("Blurs the silhouette edge.")]
    [SerializeField, Range(0f, 0.25f)] private float edgeBlur = 0.03f;

    [Tooltip("How quickly the shadow washes out along its length, away from the feet.")]
    [SerializeField, Range(0f, 2f)] private float lengthFade = 0.55f;

    [Tooltip("Pulls the silhouette back under the feet.")]
    [SerializeField] private float contactBias = 0.18f;

    [Header("Height Response")]
    [SerializeField] private float growWithHeight = 0.35f;
    [SerializeField] private float fadeHeight = 3.5f;

    private Transform quad;
    private MeshRenderer quadRenderer;
    private Material quadMaterial;

    private static readonly int ColorID      = Shader.PropertyToID("_Color");
    private static readonly int FalloffID    = Shader.PropertyToID("_Falloff");
    private static readonly int ShadowTexID  = Shader.PropertyToID("_ShadowTex");
    private static readonly int SpriteRectID = Shader.PropertyToID("_SpriteRect");
    private static readonly int UseSpriteID  = Shader.PropertyToID("_UseSprite");
    private static readonly int SoftnessID   = Shader.PropertyToID("_Softness");
    private static readonly int BlurID       = Shader.PropertyToID("_BlurRadius");
    private static readonly int LengthFadeID = Shader.PropertyToID("_LengthFade");

    private SpriteRenderer spriteSource;

    private void OnEnable()
    {
        EnsureQuad();
    }

    private void OnDisable()
    {
        if (quadRenderer != null)
            quadRenderer.enabled = false;
    }

    private void OnDestroy()
    {
        if (quad == null) return;
        if (Application.isPlaying) Destroy(quad.gameObject);
        else DestroyImmediate(quad.gameObject);
    }

    private void EnsureQuad()
    {
        if (quad != null) return;

        var go = new GameObject("__BlobShadow");
        go.hideFlags = HideFlags.DontSave;
        quad = go.transform;

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = BuildGroundQuad();

        quadRenderer = go.AddComponent<MeshRenderer>();
        quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        quadRenderer.receiveShadows = false;
        quadRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        quadRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        Shader s = Shader.Find("Custom/BlobShadow");
        if (s != null)
        {
            quadMaterial = new Material(s) { hideFlags = HideFlags.DontSave };
            quadRenderer.sharedMaterial = quadMaterial;
        }
    }

    private static Mesh BuildGroundQuad()
    {
        var mesh = new Mesh { name = "BlobShadowQuad", hideFlags = HideFlags.DontSave };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, -0.5f),
            new Vector3( 0.5f, 0f, -0.5f),
            new Vector3( 0.5f, 0f,  0.5f),
            new Vector3(-0.5f, 0f,  0.5f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(1f, 1f), new Vector2(0f, 1f)
        };
        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateBounds();
        return mesh;
    }

    private Vector3 ResolveShadowDirection()
    {
        Vector3 dir = Vector3.zero;

        if (alignToSunDirection)
        {
            Light light = shadowLight != null ? shadowLight : RenderSettings.sun;

            if (light == null)
                light = FindBestDirectionalLight();

            if (light != null)
                dir = light.transform.forward;
        }

        dir.y = 0f;

        if (dir.sqrMagnitude < 1e-4f)
            dir = new Vector3(0.4f, 0f, -0.9f);

        return dir.normalized;
    }

    private static Light FindBestDirectionalLight()
    {
        if (cachedLight != null)
            return cachedLight;

        Light best = null;
        float bestIntensity = -1f;

        foreach (Light light in FindObjectsOfType<Light>())
        {
            if (light.type != LightType.Directional || !light.isActiveAndEnabled)
                continue;

            Vector3 flat = light.transform.forward;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f)
                continue;

            if (light.intensity > bestIntensity)
            {
                bestIntensity = light.intensity;
                best = light;
            }
        }

        cachedLight = best;
        return best;
    }

    private static Light cachedLight;

    private void LateUpdate()
    {
        EnsureQuad();
        if (quadRenderer == null) return;

        Transform target = followTarget != null ? followTarget : transform;
        Vector3 origin = target.position + Vector3.up * castStartHeight;

        RaycastHit hit;
        if (!Physics.Raycast(origin, Vector3.down, out hit, maxDropDistance + castStartHeight,
                             groundMask, QueryTriggerInteraction.Ignore))
        {
            quadRenderer.enabled = false;
            return;
        }

        if (spriteSource == null)
            spriteSource = GetComponentInChildren<SpriteRenderer>(true);

        float baseY = spriteSource != null ? spriteSource.bounds.min.y : target.position.y;
        float height = Mathf.Max(0f, baseY - hit.point.y);

        if (height >= fadeHeight)
        {
            quadRenderer.enabled = false;
            return;
        }

        quadRenderer.enabled = true;

        bool asSilhouette = useSpriteShape && spriteSource != null && spriteSource.sprite != null;
        float grow = height * growWithHeight;
        float fade = 1f - Mathf.Clamp01(height / fadeHeight);

        if (asSilhouette)
        {
            Sprite sp = spriteSource.sprite;

            Vector3 away = ResolveShadowDirection();
            quad.rotation = Quaternion.LookRotation(away, hit.normal);

            Vector3 spriteSize = spriteSource.bounds.size;
            float w = spriteSize.x + grow;
            float l = spriteSize.y * lengthSquash + grow;
            quad.localScale = new Vector3(w, 1f, l);

            quad.position = hit.point + hit.normal * surfaceOffset + away * (l * 0.5f - contactBias);

            if (quadMaterial != null)
            {
                Texture tex = sp.texture;
                quadMaterial.SetTexture(ShadowTexID, tex);

                Rect r = sp.textureRect;
                quadMaterial.SetVector(SpriteRectID, new Vector4(
                    r.x / tex.width, r.y / tex.height,
                    r.width / tex.width, r.height / tex.height));

                quadMaterial.SetFloat(UseSpriteID, 1f);
                quadMaterial.SetFloat(SoftnessID, silhouetteSoftness);
                quadMaterial.SetFloat(BlurID, edgeBlur);
                quadMaterial.SetFloat(LengthFadeID, lengthFade);
            }
        }
        else
        {
            quad.position = hit.point + hit.normal * surfaceOffset;
            quad.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
            float size = baseSize + grow;
            quad.localScale = new Vector3(size, 1f, size);

            if (quadMaterial != null)
                quadMaterial.SetFloat(UseSpriteID, 0f);
        }

        Color c = shadowColor;
        c.a = baseOpacity * fade;

        if (quadMaterial != null)
        {
            quadMaterial.SetColor(ColorID, c);
            quadMaterial.SetFloat(FalloffID, edgeFalloff);
        }
    }
}

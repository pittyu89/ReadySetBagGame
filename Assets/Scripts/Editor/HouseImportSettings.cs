using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Import settings for the house model and its textures, applied on every import so they
/// survive a new export from Blender.
///
/// The house textures are small 256px atlases with transparent space between the regions,
/// and Blender draws every textured house material with alpha cut out. Unity imports those
/// materials as plain opaque, so a decal (the utensils on the kitchen wall) turned into a
/// black rectangle and the posters grew black edges. Cutting alpha out here makes the game
/// match Blender. The textures are pixel art, so they're imported like the game's sprites.
/// The walls, floors and ceilings are also made matte (see MATTE_MATERIALS).
/// </summary>
public class HouseImportSettings : AssetPostprocessor
{
    private const string HOUSE_MODEL = "Assets/Models/Rooms/House.fbx";
    private const string HOUSE_TEXTURES = "Assets/Models/Textures/HouseTextures/v05/";

    private void OnPreprocessModel()
    {
        if (assetPath != HOUSE_MODEL)
            return;

        ModelImporter importer = (ModelImporter)assetImporter;

        // The house is lightmap static; its own UVs are atlas UVs that overlap, so bake needs its own set
        importer.generateSecondaryUV = true;

        // The Blender file can carry a menu camera, lights and empty actions - none belong in the game
        importer.importCameras = false;
        importer.importLights = false;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
    }

    // The outline shader (ReadySetBag/MeshOutline) pushes each vertex out along its normal. The
    // house is low poly with hard edges, so the vertices at a corner point different ways and the
    // outline would split open there. Store one averaged normal per corner position in UV3.
    private void OnPostprocessModel(GameObject root)
    {
        if (assetPath != HOUSE_MODEL)
            return;

        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh != null)
                BakeSmoothNormals(mesh);
        }
    }

    private static void BakeSmoothNormals(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        if (normals == null || normals.Length != vertices.Length)
            return;

        // Sum each distinct face direction once per position, so a corner shared by many triangles
        // of the same face doesn't pull the average towards that face
        var sums = new System.Collections.Generic.Dictionary<Vector3Int, Vector3>();
        var seen = new System.Collections.Generic.Dictionary<Vector3Int, System.Collections.Generic.List<Vector3>>();
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3Int key = Quantize(vertices[i]);
            if (!seen.TryGetValue(key, out var list))
            {
                list = new System.Collections.Generic.List<Vector3>();
                seen[key] = list;
                sums[key] = Vector3.zero;
            }
            bool duplicate = false;
            foreach (Vector3 n in list)
                if (Vector3.Dot(n, normals[i]) > 0.999f) { duplicate = true; break; }
            if (!duplicate)
            {
                list.Add(normals[i]);
                sums[key] += normals[i];
            }
        }

        var smooth = new Vector3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 s = sums[Quantize(vertices[i])];
            smooth[i] = s.sqrMagnitude > 1e-8f ? s.normalized : normals[i];
        }
        mesh.SetUVs(3, smooth);
    }

    private static Vector3Int Quantize(Vector3 v)
    {
        return new Vector3Int(Mathf.RoundToInt(v.x * 10000f), Mathf.RoundToInt(v.y * 10000f), Mathf.RoundToInt(v.z * 10000f));
    }

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(HOUSE_TEXTURES))
            return;

        TextureImporter importer = (TextureImporter)assetImporter;

        // Bleed colour into the transparent gaps so a region edge never samples black
        importer.alphaIsTransparency = true;

        // These are pixel art stretched a long way (the ground floor gets ~4 texels per metre),
        // so filtering smears the planks and the rug pattern. Match the game's sprites instead.
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        // Blender clamps this one (Extend); the kitchen decal's UVs run past the edge
        if (assetPath.EndsWith("texture walls.png"))
            importer.wrapMode = TextureWrapMode.Clamp;
    }

    // Bump when this postprocessor's output changes, so Unity reimports the house instead of
    // reporting an inconsistent import result. 2 = smoothed outline normals in UV3.
    public override uint GetVersion()
    {
        return 2;
    }

    // Run after URP's own FBX material importer, which would otherwise rebuild these as opaque
    public override int GetPostprocessOrder()
    {
        return 100;
    }

    // Walls, floors and ceilings. Blender exports them half glossy, and with no reflection probes
    // indoors they mirrored the bright sky as pale streaks down the walls. Paint is matte.
    private static readonly string[] MATTE_MATERIALS =
    {
        "wall texture", "Interior_wall_texture", "Light Grey", "Dark Brown",
        "firstfloor", "second floor", "garage color",
    };

    private void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
    {
        if (assetPath != HOUSE_MODEL)
            return;

        if (System.Array.IndexOf(MATTE_MATERIALS, description.materialName) >= 0 && material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 0f);

        if (!material.HasProperty("_BaseMap") || material.GetTexture("_BaseMap") == null)
            return;

        material.SetFloat("_AlphaClip", 1f);
        material.SetFloat("_Cutoff", 0.5f);
        material.EnableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)RenderQueue.AlphaTest;
    }
}

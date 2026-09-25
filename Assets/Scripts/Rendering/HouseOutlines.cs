using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Gives the house's props a dark outline, like the game's pixel-art sprites: a thick one around
/// furniture and a thin one around small items. Walls, floors, doors, windows, rugs and posters
/// get none.
///
/// Each prop gets a child renderer drawing its own mesh with the outline material
/// (ReadySetBag/MeshOutline), so the outline follows it when a door swings or the 2nd floor drops
/// in, and hides with it. The children are built whenever the component is enabled - in the
/// editor too, so the Scene view shows them - and are hidden from the Hierarchy and never saved.
///
/// A prop is sorted by its name in the house model (numbered copies like "Drawer A.012" count as
/// "Drawer A"). Parts of a bigger piece - drawer fronts, cupboard doors, car wheels - sit inside
/// another piece's bounds and get the thin outline, so furniture doesn't gain heavy inner lines.
/// </summary>
[ExecuteAlways]
public class HouseOutlines : MonoBehaviour
{
    [SerializeField] private Material furnitureOutline;
    [SerializeField] private Material itemOutline;

    [Tooltip("Names (without the .001 suffix) that get the thick furniture outline. " +
             "A name ending in * matches anything starting with it.")]
    [SerializeField] private string[] furniture =
    {
        "Bath", "Bed double", "Bunk bed", "Chair*", "Cupboard*", "Desk", "Dishwasher", "Drawer*",
        "Fridge*", "Medicine Cabinet", "Oven", "Radiator*", "Rounded table*", "Sedan*", "SUV*",
        "Shelf*", "Shower Door", "Sink", "Sofa*", "Standart Bookshelf", "Table*", "Television*",
        "Toilet", "Tool Chest", "Wall shelf*", "Washing machine", "box_*",
    };

    [Tooltip("Names that get the thin item outline.")]
    [SerializeField] private string[] items =
    {
        "Alarm clock", "Bin", "Books*", "Broom", "Clock", "coat hanger", "Console*", "Glass", "Globe",
        "Hoover", "Kettle", "Keyboard", "Kitchen Roll", "Knife Block", "Lamp*", "Laptop", "Microwave",
        "Mirror*", "Monitor", "Pan", "PC", "Phone", "Plant*", "Plate", "Pot", "Socket", "Speaker",
        "Tablet", "Wall Light",
    };

    private static readonly Regex NumberSuffix = new Regex(@"\.\d+$");
    private const string OUTLINE_NAME = "Outline";

    private void OnEnable()
    {
        Rebuild();
    }

    private void OnDisable()
    {
        Clear();
    }

#if UNITY_EDITOR
    // Editing the lists or materials in the Inspector redraws the outlines in the Scene view
    private void OnValidate()
    {
        if (!isActiveAndEnabled)
            return;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && isActiveAndEnabled)
                Rebuild();
        };
    }
#endif

    private void Rebuild()
    {
        Clear();
        if (furnitureOutline == null || itemOutline == null)
        {
            Debug.LogWarning("HouseOutlines has no outline materials assigned, so the house has no outlines.", this);
            return;
        }

        var furnitureRenderers = new List<MeshRenderer>();
        var itemRenderers = new List<MeshRenderer>();
        foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>(true))
        {
            string name = NumberSuffix.Replace(r.name, "");
            if (Matches(name, furniture))
                furnitureRenderers.Add(r);
            else if (Matches(name, items))
                itemRenderers.Add(r);
        }

        foreach (MeshRenderer r in furnitureRenderers)
            AddOutline(r, IsPartOfLargerPiece(r, furnitureRenderers) ? itemOutline : furnitureOutline);
        foreach (MeshRenderer r in itemRenderers)
            AddOutline(r, itemOutline);
    }

    private void Clear()
    {
        var old = new List<GameObject>();
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == OUTLINE_NAME && (t.gameObject.hideFlags & HideFlags.DontSave) != 0)
                old.Add(t.gameObject);
        }
        foreach (GameObject go in old)
        {
            if (Application.isPlaying)
                Destroy(go);
            else
                DestroyImmediate(go);
        }
    }

    private static bool Matches(string name, string[] patterns)
    {
        foreach (string p in patterns)
        {
            if (p.EndsWith("*") ? name.StartsWith(p.Substring(0, p.Length - 1)) : name == p)
                return true;
        }
        return false;
    }

    // A drawer front, cupboard door or wheel sits within the bounds of a bigger piece of furniture
    private static bool IsPartOfLargerPiece(MeshRenderer part, List<MeshRenderer> all)
    {
        Bounds b = WorldBounds(part);
        float volume = b.size.x * b.size.y * b.size.z;
        foreach (MeshRenderer other in all)
        {
            if (other == part)
                continue;
            Bounds o = WorldBounds(other);
            if (o.size.x * o.size.y * o.size.z <= volume)
                continue;
            o.Expand(0.2f);
            if (o.Contains(b.min) && o.Contains(b.max))
                return true;
        }
        return false;
    }

    // Renderer.bounds is empty for hidden objects (the 2nd floor starts hidden), so work from the mesh
    private static Bounds WorldBounds(MeshRenderer r)
    {
        MeshFilter f = r.GetComponent<MeshFilter>();
        if (f == null || f.sharedMesh == null)
            return new Bounds(r.transform.position, Vector3.zero);
        Bounds local = f.sharedMesh.bounds;
        Matrix4x4 m = r.transform.localToWorldMatrix;
        var world = new Bounds(m.MultiplyPoint3x4(local.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            Vector3 c = local.center + Vector3.Scale(local.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            world.Encapsulate(m.MultiplyPoint3x4(c));
        }
        return world;
    }

    private static void AddOutline(MeshRenderer source, Material material)
    {
        MeshFilter filter = source.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null)
            return;

        var outline = new GameObject(OUTLINE_NAME);
        // Built fresh in the editor and in play; hidden from the Hierarchy and never saved
        outline.hideFlags = HideFlags.HideAndDontSave;
        outline.layer = source.gameObject.layer;
        outline.transform.SetParent(source.transform, false);
        outline.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;

        var renderer = outline.AddComponent<MeshRenderer>();
        var materials = new Material[filter.sharedMesh.subMeshCount];
        for (int i = 0; i < materials.Length; i++)
            materials[i] = material;
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        renderer.enabled = source.enabled;
    }
}

using UnityEngine;

/// <summary>
/// Points a house prop at its outline mesh: a copy of the prop's mesh with all its material
/// slots merged into one, so HouseOutlines draws the prop's outline in one call rather than
/// one per slot. HouseImportSettings adds it on import, so it always matches the current model.
/// Props with a single material slot don't get one and are outlined with their own mesh.
/// </summary>
[DisallowMultipleComponent]
public class HouseOutlineMesh : MonoBehaviour
{
    public Mesh mesh;
}

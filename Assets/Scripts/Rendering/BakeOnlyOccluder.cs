using UnityEngine;

/// <summary>
/// A stand-in ceiling for the lightmap bake. The house is open-topped so the camera can look in,
/// but baked like that the sky and the lamps of the next room spill over every wall top, leaving
/// glows and streaks along the upper edge of the walls. These invisible slabs close the rooms off
/// for the bake only (Shadows Only, so the camera never draws them) and switch off in play so the
/// sun still reaches in.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class BakeOnlyOccluder : MonoBehaviour
{
    void Awake()
    {
        if (Application.isPlaying)
            GetComponent<MeshRenderer>().enabled = false;
    }
}

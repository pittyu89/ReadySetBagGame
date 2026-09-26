using UnityEngine;

/// <summary>
/// A door or drawer of a <see cref="StorageFurniture"/> that <see cref="StorageFocus"/> opens
/// while the furniture is searched.
///
/// The hinge and slide direction are worked out from the part's bounds and the side the camera
/// looks from, because the house model's pivots all sit at the house origin rather than on the
/// hinges. "Left" and "right" are as seen standing in front of the furniture.
/// </summary>
[System.Serializable]
public class StorageMovingPart
{
    public enum Motion { SwingHingeLeft, SwingHingeRight, SlideOut }

    public Transform part;
    public Motion motion = Motion.SwingHingeLeft;

    [Tooltip("How far it opens: degrees for a door, metres for a drawer.")]
    public float amount = 100f;
}

using UnityEngine;
using System.Collections;

/// <summary>
/// Swings a door open and shut about its hinge (the door's local Z axis) with a smooth animation.
/// Like a real door it only swings one way: toward its front, the side from which the knob is
/// on the left and the hinge on the right. It is pulled open from the front and pushed from behind.
/// Attach this script to any door GameObject.
/// </summary>
public class DoorToggle : MonoBehaviour
{
    private bool isOpen = false;
    public bool IsOpen => isOpen;
    [SerializeField] private float rotationDuration = 0.5f; // Time in seconds for door to rotate
    [Tooltip("How far the door swings open, in degrees.")]
    [SerializeField] private float openAngle = 80f;
    [SerializeField] private AudioClip openDoorSFX;
    [Tooltip("Difficulties this door can be opened on (beginner, intermediate, advanced). " +
             "Leave empty for a door that opens on every difficulty.")]
    [SerializeField] private string[] allowedDifficulties = new string[0];
    private Coroutine rotationCoroutine;

    // The shut rotation, read once. The house's door leaves sit at X = 270, where Euler angles
    // are ambiguous: rebuilding the target from localEulerAngles read back a different Y/Z split
    // after every swing, so each click aimed somewhere new and the door kept spinning.
    private Quaternion closedRotation;
    private Quaternion openRotation;

    private const string DIFFICULTY_PREF = "SessionDifficulty";

    void Awake()
    {
        closedRotation = transform.localRotation;
        openRotation = closedRotation * Quaternion.Euler(0f, 0f, FrontSwingAngle());
    }

    /// <summary>
    /// False when this session's difficulty is not one the door is allowed on. The door
    /// stays shut and DoorProximityHandler offers no button for it.
    /// </summary>
    public bool CanBeUsed
    {
        get
        {
            if (allowedDifficulties == null || allowedDifficulties.Length == 0)
                return true;

            string difficulty = PlayerPrefs.GetString(DIFFICULTY_PREF, "beginner");
            foreach (string allowed in allowedDifficulties)
            {
                if (string.Equals(allowed, difficulty, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }

    public void ToggleDoor()
    {
        if (!CanBeUsed)
            return;

        // Stop any existing rotation
        if (rotationCoroutine != null)
        {
            StopCoroutine(rotationCoroutine);
        }

        isOpen = !isOpen;
        SoundManager.Sfx(openDoorSFX);
        rotationCoroutine = StartCoroutine(RotateDoorTo(isOpen ? openRotation : closedRotation));
    }

    // Which of +openAngle / -openAngle swings the panel out to the door's front.
    // The hinge is the door's origin, so "across" runs from the hinge to the knob edge; the front
    // is the side a viewer stands on to see that edge on their left: Cross(across, up).
    private float FrontSwingAngle()
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
            return openAngle;

        Vector3 localCentre = meshFilter.sharedMesh.bounds.center;
        Vector3 hinge = transform.position;
        Vector3 across = transform.TransformPoint(localCentre) - hinge;
        across.y = 0f;
        Vector3 front = Vector3.Cross(across, Vector3.up);

        Vector3 swungPositive = PanelCentreAt(openAngle, localCentre) - hinge;
        return Vector3.Dot(swungPositive, front) >= 0f ? openAngle : -openAngle;
    }

    private Vector3 PanelCentreAt(float angle, Vector3 localCentre)
    {
        Matrix4x4 local = Matrix4x4.TRS(transform.localPosition, closedRotation * Quaternion.Euler(0f, 0f, angle), transform.localScale);
        Matrix4x4 parent = transform.parent != null ? transform.parent.localToWorldMatrix : Matrix4x4.identity;
        return (parent * local).MultiplyPoint3x4(localCentre);
    }

    private IEnumerator RotateDoorTo(Quaternion targetRotation)
    {
        Quaternion startRotation = transform.localRotation;
        float elapsedTime = 0f;

        while (elapsedTime < rotationDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / rotationDuration;
            transform.localRotation = Quaternion.Slerp(startRotation, targetRotation, t);
            yield return null;
        }

        // Ensure we end at exactly the target rotation
        transform.localRotation = targetRotation;
    }
}

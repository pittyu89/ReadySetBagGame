using UnityEngine;
using System.Collections;

/// <summary>
/// Toggles a door's rotation between open (Z = 75) and closed (Z = 0) with smooth animation.
/// Uses local rotation to avoid gimbal lock issues.
/// Attach this script to any door GameObject.
/// </summary>
public class DoorToggle : MonoBehaviour
{
    private bool isOpen = false;
    public bool IsOpen => isOpen;
    [SerializeField] private float rotationDuration = 0.5f; // Time in seconds for door to rotate
    [SerializeField] private AudioClip openDoorSFX;
    [Tooltip("Difficulties this door can be opened on (beginner, intermediate, advanced). " +
             "Leave empty for a door that opens on every difficulty.")]
    [SerializeField] private string[] allowedDifficulties = new string[0];
    private Coroutine rotationCoroutine;

    private const string DIFFICULTY_PREF = "SessionDifficulty";

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
        
        if (isOpen)
        {
            // Open the door (rotate Z to 80 degrees)
            SoundManager.Sfx(openDoorSFX);
            rotationCoroutine = StartCoroutine(RotateDoorTo(80f));
        }
        else
        {
            // Close the door (rotate Z to 0 degrees)
            SoundManager.Sfx(openDoorSFX);
            rotationCoroutine = StartCoroutine(RotateDoorTo(0f));
        }
    }

    private IEnumerator RotateDoorTo(float targetZRotation)
    {
        Quaternion startRotation = transform.localRotation;
        Quaternion targetRotation = Quaternion.Euler(
            transform.localEulerAngles.x,
            transform.localEulerAngles.y,
            targetZRotation
        );

        float elapsedTime = 0f;

        while (elapsedTime < rotationDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / rotationDuration;
            transform.localRotation = Quaternion.Lerp(startRotation, targetRotation, t);
            yield return null;
        }

        // Ensure we end at exactly the target rotation
        transform.localRotation = targetRotation;
    }
}

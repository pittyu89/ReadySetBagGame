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
    private Coroutine rotationCoroutine;

    public void ToggleDoor()
    {
        // Stop any existing rotation
        if (rotationCoroutine != null)
        {
            StopCoroutine(rotationCoroutine);
        }

        isOpen = !isOpen;
        
        if (isOpen)
        {
            // Open the door (rotate Z to 80 degrees)
            if (openDoorSFX != null && SoundManager.Instance != null)
                SoundManager.Instance.PlaySFX(openDoorSFX);
            rotationCoroutine = StartCoroutine(RotateDoorTo(80f));
        }
        else
        {
            // Close the door (rotate Z to 0 degrees)
            if (openDoorSFX != null && SoundManager.Instance != null)
                SoundManager.Instance.PlaySFX(openDoorSFX);
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

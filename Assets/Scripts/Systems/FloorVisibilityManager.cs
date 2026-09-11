using UnityEngine;

public class FloorVisibilityManager : MonoBehaviour
{
    [SerializeField] private GameObject stairs;
    [SerializeField] private GameObject secondFloor;

    private float stairsMidpointHeight;
    private Transform playerTransform;
    private bool isSecondFloorVisible;

    private void Start()
    {
        // Calculate stairs midpoint height
        if (stairs != null)
        {
            Bounds bounds = GetBounds(stairs);
            stairsMidpointHeight = bounds.center.y + bounds.extents.y / 2f;
        }

        // Find the player
        playerTransform = FindObjectOfType<PlayerMovement>()?.transform;
        
        if (playerTransform == null)
        {
            // Fallback: try to find by tag
            GameObject playerGO = GameObject.FindWithTag("Player");
            if (playerGO != null)
                playerTransform = playerGO.transform;
        }

        // Initially hide second floor
        if (secondFloor != null)
        {
            secondFloor.SetActive(false);
            isSecondFloorVisible = false;
        }
    }

    private void Update()
    {
        // Only check if we have a player reference
        if (playerTransform == null || secondFloor == null)
            return;

        float playerHeight = playerTransform.position.y;
        bool shouldBeVisible = playerHeight > stairsMidpointHeight;

        // Only update if visibility state changed
        if (shouldBeVisible != isSecondFloorVisible)
        {
            secondFloor.SetActive(shouldBeVisible);
            isSecondFloorVisible = shouldBeVisible;
        }
    }

    private Bounds GetBounds(GameObject obj)
    {
        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer != null)
            return renderer.bounds;

        // If no renderer, try to get bounds from children
        Bounds bounds = new Bounds(obj.transform.position, Vector3.zero);
        foreach (Renderer childRenderer in obj.GetComponentsInChildren<Renderer>())
        {
            bounds.Encapsulate(childRenderer.bounds);
        }

        return bounds;
    }
}

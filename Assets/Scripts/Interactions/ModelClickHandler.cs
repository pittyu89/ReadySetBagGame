using UnityEngine;
using UnityEngine.EventSystems;

public class ModelClickHandler : MonoBehaviour
{
    [SerializeField] private Camera mainCamera;
    [SerializeField] private float interactionDistance = 2f;
    [Tooltip("Max height difference between player and prop. Stops you reaching furniture on " +
             "another floor, since upstairs props sit directly above the ground-floor ones.")]
    [SerializeField] private float verticalReach = 3f;
    [Tooltip("How far the pointer may move, as a fraction of screen height, and still count " +
             "as a tap rather than a camera drag.")]
    [SerializeField] private float tapMaxMovement = 0.02f;
    private InventoryPanel inventoryPanelHandler;
    private Transform playerTransform;
    private Vector2 pressPosition;
    private bool pressStartedOverUI;

    /// <summary>Reach values are read by ClickableHighlightManager so the highlight and the
    /// click always agree on what is reachable.</summary>
    public float InteractionDistance => interactionDistance;
    public float VerticalReach => verticalReach;

    void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        // Find the InventoryPanel in the scene
        inventoryPanelHandler = FindFirstObjectByType<InventoryPanel>();

        // Players could not tell which props were clickable, so props advertise themselves.
        // Created here rather than required in the scene, so GameScene needs no extra setup;
        // add the component manually to tune its colours and ranges.
        if (FindFirstObjectByType<ClickableHighlightManager>() == null)
            gameObject.AddComponent<ClickableHighlightManager>();
    }

    void Update()
    {
        // Clicks fire on release, and only if the pointer barely moved: holding and dragging
        // the screen orbits the camera (CameraOrbitController), and a drag that happens to
        // start on a prop must not open it.
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);

            if (touch.phase == TouchPhase.Began)
            {
                pressPosition = touch.position;
                pressStartedOverUI = IsPointerOverUI(touch.fingerId);
            }
            else if (touch.phase == TouchPhase.Ended && IsTap(touch.position))
            {
                HandleTouchClick(touch.position);
            }
        }
        // Check for mouse input only on non-mobile platforms (for testing in editor)
        else if (Input.GetMouseButtonDown(0))
        {
            pressPosition = Input.mousePosition;
            pressStartedOverUI = IsPointerOverUI(-1);
        }
        else if (Input.GetMouseButtonUp(0) && IsTap(Input.mousePosition))
        {
            HandleTouchClick(Input.mousePosition);
        }
    }

    private bool IsTap(Vector2 releasePosition)
    {
        float maxMove = tapMaxMovement * Screen.height;
        return !pressStartedOverUI && (releasePosition - pressPosition).sqrMagnitude <= maxMove * maxMove;
    }

    private static bool IsPointerOverUI(int pointerId)
    {
        if (EventSystem.current == null)
            return false;
        return pointerId < 0
            ? EventSystem.current.IsPointerOverGameObject()
            : EventSystem.current.IsPointerOverGameObject(pointerId);
    }

    private void HandleTouchClick(Vector2 clickPosition)
    {
        // Can't click models until gobag is picked up
        if (!GoBagPickup.IsBagPickedUp())
            return;

        Ray ray = mainCamera.ScreenPointToRay(clickPosition);
        RaycastHit hit;
        
        // Create a layer mask that ignores the "Wall" and "Sphere" layers
        int layerMask = ~(LayerMask.GetMask("Wall") | LayerMask.GetMask("Sphere"));

        // Raycast and check if the hit object is storage furniture
        if (Physics.Raycast(ray, out hit, Mathf.Infinity, layerMask))
        {
            // Find player if not already found (for dynamically spawned player)
            if (playerTransform == null)
            {
                FindPlayer();
            }

            if (playerTransform == null)
                return;

            StorageFurniture furniture = StorageFurniture.FromCollider(hit.collider);
            if (furniture != null)
            {
                // Measured to the prop's collider surface, not its pivot. A wide cabinet's
                // pivot can sit further than interactionDistance while the player is standing
                // against its face, which made props look randomly unclickable.
                if (!furniture.IsWithinReach(playerTransform.position, interactionDistance, verticalReach))
                {
                    return;
                }
                OpenInventoryWithStorage(furniture);
            }
        }
    }

    private void FindPlayer()
    {
        // Try to find by tag
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            playerTransform = playerObject.transform;
            return;
        }

        // Try to find by PlayerController component
        PlayerController playerMovement = FindFirstObjectByType<PlayerController>();
        if (playerMovement != null)
        {
            playerTransform = playerMovement.transform;
        }
    }

    private void OpenInventory(Sprite sprite)
    {
        if (inventoryPanelHandler != null)
        {
            inventoryPanelHandler.OpenInventory(sprite);
        }
    }

    private void OpenInventoryWithStorage(StorageFurniture furniture)
    {
        if (inventoryPanelHandler != null)
        {
            inventoryPanelHandler.OpenInventoryWithStorage(furniture);
        }
    }
}

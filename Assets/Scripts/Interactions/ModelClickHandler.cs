using UnityEngine;
using UnityEngine.EventSystems;

public class ModelClickHandler : MonoBehaviour
{
    [SerializeField] private Camera mainCamera;
    [SerializeField] private float interactionDistance = 2f;
    [Tooltip("Max height difference between player and prop. Stops you reaching furniture on " +
             "another floor, since upstairs props sit directly above the ground-floor ones.")]
    [SerializeField] private float verticalReach = 3f;
    private InventoryPanelHandler inventoryPanelHandler;
    private Transform playerTransform;

    /// <summary>Reach values are read by ClickableHighlightManager so the highlight and the
    /// click always agree on what is reachable.</summary>
    public float InteractionDistance => interactionDistance;
    public float VerticalReach => verticalReach;

    void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        // Find the InventoryPanelHandler in the scene
        inventoryPanelHandler = FindObjectOfType<InventoryPanelHandler>();

        // Players could not tell which props were clickable, so props advertise themselves.
        // Created here rather than required in the scene, so GameScene needs no extra setup;
        // add the component manually to tune its colours and ranges.
        if (FindObjectOfType<ClickableHighlightManager>() == null)
            gameObject.AddComponent<ClickableHighlightManager>();
    }

    void Update()
    {
        // Check for mobile touch input
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);

            if (touch.phase == TouchPhase.Began)
            {
                HandleTouchClick(touch.position);
            }
        }
        // Check for mouse input only on non-mobile platforms (for testing in editor)
        else if (Input.GetMouseButtonDown(0))
        {
            HandleTouchClick(Input.mousePosition);
        }
    }

    private void HandleTouchClick(Vector2 clickPosition)
    {
        // Can't click models until gobag is picked up
        if (!GoBagFloater.IsBagPickedUp())
            return;

        // Don't click through UI - check if pointer is over any UI element
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        Ray ray = mainCamera.ScreenPointToRay(clickPosition);
        RaycastHit hit;
        
        // Create a layer mask that ignores the "Wall" and "Sphere" layers
        int layerMask = ~(LayerMask.GetMask("Wall") | LayerMask.GetMask("Sphere"));

        // Raycast and check if hit object has ClickableModel component
        if (Physics.Raycast(ray, out hit, Mathf.Infinity, layerMask))
        {
            // Find player if not already found (for dynamically spawned player)
            if (playerTransform == null)
            {
                FindPlayer();
            }

            if (playerTransform == null)
                return;

            ClickableModel clickableModel = hit.collider.GetComponent<ClickableModel>();
            if (clickableModel != null)
            {
                // Measured to the prop's collider surface, not its pivot. A wide cabinet's
                // pivot can sit further than interactionDistance while the player is standing
                // against its face, which made props look randomly unclickable.
                if (!clickableModel.IsWithinReach(playerTransform.position, interactionDistance, verticalReach))
                {
                    return;
                }
                // Open the storage view instead of collecting the model.
                Sprite modelSprite = clickableModel.GetModelSprite();
                OpenInventoryWithStorage(modelSprite, clickableModel);
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

        // Try to find by PlayerMovement component
        PlayerMovement playerMovement = FindObjectOfType<PlayerMovement>();
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

    private void OpenInventoryWithStorage(Sprite modelSprite, ClickableModel model)
    {
        if (inventoryPanelHandler != null)
        {
            inventoryPanelHandler.OpenInventoryWithStorage(modelSprite, model);
        }
    }
}

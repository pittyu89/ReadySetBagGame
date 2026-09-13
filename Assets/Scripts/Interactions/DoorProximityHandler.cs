using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows a popup "Open Door" / "Close Door" button when the player is near a door.
/// Attach to any persistent GameObject in the GameScene (e.g. the Canvas or a manager object).
/// If no button is assigned in the inspector, one is created automatically at runtime.
/// </summary>
public class DoorProximityHandler : MonoBehaviour
{
    [Header("UI References (optional — auto-created if empty)")]
    [SerializeField] private Button doorButton;

    [Header("Button Sprites")]
    [Tooltip("The sprites carry their own OPEN / CLOSE label, so the button has no text.")]
    [SerializeField] private Sprite openSprite;
    [SerializeField] private Sprite openPressedSprite;
    [SerializeField] private Sprite closeSprite;
    [SerializeField] private Sprite closePressedSprite;

    [Header("Button Layout")]
    [Tooltip("Offset from the middle of the screen, beside the player, in canvas units.")]
    [SerializeField] private Vector2 buttonPosition = new Vector2(155f, 5f);
    [Tooltip("Twice-ish the 96x32 sprite, kept at its 3:1 shape so the pixels stay even.")]
    [SerializeField] private Vector2 buttonSize = new Vector2(156f, 52f);

    [Header("Settings")]
    [Tooltip("Distance from the door's surface, so touching the door reads as ~0 " +
             "whether it is open or closed.")]
    [SerializeField] private float interactionDistance = 2f;
    [Tooltip("Max height difference between player and door. Stops you reaching a door on " +
             "another floor, since upstairs doors sit directly above the ground-floor ones.")]
    [SerializeField] private float verticalReach = 3f;

    [Header("Audio")]
    [SerializeField] private AudioClip buttonClickAudio;

    private DoorToggle[] doors;
    private Collider[] doorColliders;   // cached per door, index-matched to `doors`
    private DoorToggle nearestDoor;
    private Transform playerTransform;
    private GameObject buttonRoot;
    private Image buttonImage;

    void Start()
    {
        // Include inactive doors: upper floors start hidden and are revealed later, so a
        // scan of active objects only would miss them permanently.
        doors = FindObjectsOfType<DoorToggle>(true);

        // Cache each door's collider so the proximity test can measure to the door surface
        // without a GetComponent per door per frame.
        doorColliders = new Collider[doors.Length];
        for (int i = 0; i < doors.Length; i++)
            doorColliders[i] = doors[i] != null ? doors[i].GetComponent<Collider>() : null;

        // Find the player
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
            playerTransform = playerObj.transform;

        // Build the popup button if none was assigned
        if (doorButton == null)
            CreateDoorButton();
        else
        {
            buttonRoot = doorButton.gameObject;
            buttonImage = doorButton.targetGraphic as Image;
        }

        // Wire up the click listener
        doorButton.onClick.AddListener(OnDoorButtonClicked);

        // Start hidden
        buttonRoot.SetActive(false);
    }

    void LateUpdate()
    {
        // Re-find player if lost (dynamically spawned)
        if (playerTransform == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
                playerTransform = playerObj.transform;
            else
            {
                HideButton();
                return;
            }
        }

        // Hide door button when the game is paused
        if (Time.timeScale == 0f)
        {
            HideButton();
            return;
        }

        // Doors stay usable before the bag is picked up — spawns are randomised, so either
        // the player or the bag can start behind a closed door.

        // Find the nearest door within range
        nearestDoor = null;
        float closestDist = Mathf.Infinity;

        for (int i = 0; i < doors.Length; i++)
        {
            DoorToggle door = doors[i];
            if (door == null) continue;

            // A door on a hidden floor is not reachable
            if (!door.gameObject.activeInHierarchy) continue;

            // Reject doors on another floor before the flat distance check, which cannot
            // tell them apart: upstairs doors share the exact X/Z of the ones below.
            if (Mathf.Abs(playerTransform.position.y - door.transform.position.y) > verticalReach)
                continue;

            // Measure to the door's surface rather than its transform. The pivot sits on the
            // hinge and does not move when the door swings, so a pivot-based check leaves an
            // open door unreachable from the panel you are actually standing against.
            Vector3 measureFrom = door.transform.position;
            if (doorColliders[i] != null)
                measureFrom = doorColliders[i].bounds.ClosestPoint(playerTransform.position);

            float dist = Vector2.Distance(
                new Vector2(playerTransform.position.x, playerTransform.position.z),
                new Vector2(measureFrom.x, measureFrom.z)
            );

            if (dist <= interactionDistance && dist < closestDist)
            {
                closestDist = dist;
                nearestDoor = door;
            }
        }

        if (nearestDoor != null)
            ShowButton();
        else
            HideButton();
    }

    private void ShowButton()
    {
        if (!buttonRoot.activeSelf)
            buttonRoot.SetActive(true);

        // Keep the button behind every other element on the canvas. Setting this once at
        // creation is not enough: UI built or re-parented later lands after it in the sibling
        // order and would draw underneath, letting the button cover panels and popups.
        if (buttonRoot.transform.GetSiblingIndex() != 0)
            buttonRoot.transform.SetAsFirstSibling();

        // Swap between the OPEN and CLOSE art based on door state
        bool open = nearestDoor.IsOpen;
        Sprite normal = open ? closeSprite : openSprite;
        Sprite pressed = open ? closePressedSprite : openPressedSprite;

        if (buttonImage != null && normal != null && buttonImage.sprite != normal)
            buttonImage.sprite = normal;

        SpriteState state = doorButton.spriteState;
        if (state.pressedSprite != pressed)
        {
            state.pressedSprite = pressed;
            doorButton.spriteState = state;
        }
    }

    private void HideButton()
    {
        if (buttonRoot.activeSelf)
            buttonRoot.SetActive(false);
    }

    private void OnDoorButtonClicked()
    {
        if (nearestDoor == null) return;

        if (buttonClickAudio != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(buttonClickAudio);

        nearestDoor.ToggleDoor();
    }

    /// <summary>
    /// Creates the door button from the DoorSheet sprites, beside the player in the middle of
    /// the screen. The art already has its bevel and label, so the button is a single image
    /// that swaps to its pressed frame while held.
    /// </summary>
    private void CreateDoorButton()
    {
        // Find an existing screen-space Canvas in the scene, or create one
        Canvas canvas = FindScreenSpaceCanvas();
        if (canvas == null)
        {
            GameObject canvasObj = new GameObject("DoorProximityCanvas");
            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;
            canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObj.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
            canvasObj.AddComponent<GraphicRaycaster>();
        }

        buttonRoot = new GameObject("OpenDoorButton");
        buttonRoot.transform.SetParent(canvas.transform, false);
        // Render behind all other UI on this canvas
        buttonRoot.transform.SetAsFirstSibling();

        RectTransform rootRect = buttonRoot.AddComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.anchoredPosition = buttonPosition;
        rootRect.sizeDelta = buttonSize;

        buttonImage = buttonRoot.AddComponent<Image>();
        buttonImage.sprite = openSprite;
        buttonImage.preserveAspect = true;

        // Sprite swap rather than a colour tint: the sheet has its own pressed frame
        doorButton = buttonRoot.AddComponent<Button>();
        doorButton.targetGraphic = buttonImage;
        doorButton.transition = Selectable.Transition.SpriteSwap;
        SpriteState state = new SpriteState();
        state.pressedSprite = openPressedSprite;
        doorButton.spriteState = state;
    }

    /// <summary>
    /// Finds the backmost screen-space canvas to host the button.
    ///
    /// Picks the lowest sortingOrder rather than simply the first match. The first match was
    /// DebugCanvas, which sits at sortingOrder 30000 - so the door button rendered on top of
    /// every panel and popup in the game no matter where it sat in its own sibling order,
    /// because sibling order only sorts within a single canvas.
    /// </summary>
    private Canvas FindScreenSpaceCanvas()
    {
        Canvas best = null;

        foreach (Canvas c in FindObjectsOfType<Canvas>())
        {
            if (!c.isRootCanvas) continue;
            if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;

            if (best == null || c.sortingOrder < best.sortingOrder)
                best = c;
        }

        return best;
    }
}

using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Shows a popup "Open Door" / "Close Door" button when the player is near a door.
/// Attach to any persistent GameObject in the GameScene (e.g. the Canvas or a manager object).
/// If no button is assigned in the inspector, one is created automatically at runtime.
/// </summary>
public class DoorProximityHandler : MonoBehaviour
{
    [Header("UI References (optional — auto-created if empty)")]
    [SerializeField] private Button doorButton;
    [SerializeField] private TextMeshProUGUI doorButtonText;

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
    private TextMeshProUGUI doorButtonTextShadow;

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
            buttonRoot = doorButton.gameObject;

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

        // Update label based on door state
        string label = nearestDoor.IsOpen ? "Close Door" : "Open Door";
        if (doorButtonText != null)
            doorButtonText.text = label;
        if (doorButtonTextShadow != null)
            doorButtonTextShadow.text = label;
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
    /// Creates a retro-styled popup button anchored to the bottom-center of the screen.
    /// Layered panels give a beveled 3D look with warm retro colors.
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

        // Load Jersey 25 font from Resources
        TMP_FontAsset jerseyFont = Resources.Load<TMP_FontAsset>("Fonts/Jersey25-Regular SDF");

        // --- Retro color palette (white button, black text) ---
        Color darkBorder    = new Color(0.15f, 0.15f, 0.15f, 1f);   // dark gray border
        Color shadowColor   = new Color(0.70f, 0.70f, 0.70f, 1f);   // light gray (bottom/right bevel)
        Color faceColor     = new Color(1f, 1f, 1f, 1f);             // white face
        Color highlightEdge = new Color(0.95f, 0.95f, 0.95f, 1f);   // near-white highlight (top/left bevel)
        Color textColor     = new Color(0.1f, 0.1f, 0.1f, 1f);      // near-black text
        Color textShadowCol = new Color(0.6f, 0.6f, 0.6f, 0.5f);    // subtle gray text shadow

        // --- Outer container (root) ---
        buttonRoot = new GameObject("OpenDoorButton");
        buttonRoot.transform.SetParent(canvas.transform, false);
        // Render behind all other UI on this canvas
        buttonRoot.transform.SetAsFirstSibling();

        RectTransform rootRect = buttonRoot.AddComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 0f);
        rootRect.anchorMax = new Vector2(0.5f, 0f);
        rootRect.pivot = new Vector2(0.5f, 0f);
        rootRect.anchoredPosition = new Vector2(0f, 50f);
        rootRect.sizeDelta = new Vector2(220f, 58f);

        // Layer 1: Dark outer border
        Image borderImage = buttonRoot.AddComponent<Image>();
        borderImage.color = darkBorder;

        // Layer 2: Highlight edge (top-left bevel) — inset 2px
        GameObject highlightObj = CreateUIChild("HighlightEdge", buttonRoot.transform, highlightEdge, 2f);

        // Layer 3: Shadow edge (bottom-right bevel) — inset 3px from root
        GameObject shadowObj = CreateUIChild("ShadowEdge", buttonRoot.transform, shadowColor, 3f);
        RectTransform shadowRect = shadowObj.GetComponent<RectTransform>();
        shadowRect.offsetMin = new Vector2(5f, 3f);
        shadowRect.offsetMax = new Vector2(-3f, -5f);

        // Layer 4: Button face — inset 4px
        GameObject faceObj = CreateUIChild("ButtonFace", buttonRoot.transform, faceColor, 4f);

        // The Button component targets the face for color tinting
        doorButton = buttonRoot.AddComponent<Button>();
        doorButton.targetGraphic = faceObj.GetComponent<Image>();
        ColorBlock colors = doorButton.colors;
        colors.normalColor = faceColor;
        colors.highlightedColor = new Color(0.90f, 0.90f, 0.90f, 1f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        colors.selectedColor = faceColor;
        doorButton.colors = colors;

        // --- Text shadow (offset 1px down-right) ---
        GameObject textShadowObj = new GameObject("TextShadow");
        textShadowObj.transform.SetParent(faceObj.transform, false);
        RectTransform tShadowRect = textShadowObj.AddComponent<RectTransform>();
        tShadowRect.anchorMin = Vector2.zero;
        tShadowRect.anchorMax = Vector2.one;
        tShadowRect.offsetMin = new Vector2(1f, -1f);
        tShadowRect.offsetMax = new Vector2(1f, -1f);
        TextMeshProUGUI shadowText = textShadowObj.AddComponent<TextMeshProUGUI>();
        shadowText.text = "Open Door";
        shadowText.fontSize = 28;
        shadowText.fontStyle = FontStyles.UpperCase;
        shadowText.alignment = TextAlignmentOptions.Center;
        shadowText.color = textShadowCol;
        shadowText.raycastTarget = false;
        if (jerseyFont != null) shadowText.font = jerseyFont;

        // --- Main text ---
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(faceObj.transform, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        doorButtonText = textObj.AddComponent<TextMeshProUGUI>();
        doorButtonText.text = "Open Door";
        doorButtonText.fontSize = 28;
        doorButtonText.fontStyle = FontStyles.UpperCase;
        doorButtonText.alignment = TextAlignmentOptions.Center;
        doorButtonText.color = textColor;
        doorButtonText.raycastTarget = false;
        if (jerseyFont != null) doorButtonText.font = jerseyFont;

        // Keep a reference to the shadow text so we can update it
        doorButtonTextShadow = shadowText;
    }

    /// <summary>
    /// Helper to create a child UI panel with a solid color, inset by a uniform margin.
    /// </summary>
    private GameObject CreateUIChild(string name, Transform parent, Color color, float inset)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);

        Image img = obj.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;

        return obj;
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

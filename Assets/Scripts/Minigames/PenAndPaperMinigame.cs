using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The pen and paper minigame that runs after the quiz's evacuation plan question.
///
/// A street map is laid on the table with the player's own house marked in the top left and
/// the evacuation field marked in green in the bottom right. Holding the pen on the road
/// outside the house and dragging draws the route; reaching the green field is the whole of
/// the round.
///
/// The route has to keep to the roads, and that is the only rule. There is no single line to
/// copy — the streets are a grid, so a dozen different routes all get there, which is what
/// the reference sheet asks for when it says this is like the rope "kaso may mga different
/// ways para ma-draw yung lines". Wandering off the tarmac hands the stroke back to the start
/// rather than failing anything, exactly the way <see cref="RopeKnotMinigame"/> treats a
/// finger that strays off the guide.
///
/// Like the other minigames there is nothing to fail. It runs whether the quiz answer was
/// right or wrong, and there is no clock.
///
/// None of the map is authored here. The roads, the field and the spot outside the house are
/// all read off the map sprite's own pixels when the panel opens — see <see cref="BuildMap"/>
/// — so a redrawn map is followed rather than having to be re-measured by hand. That is why
/// the map's texture is imported readable.
///
/// The gestures are the ones the rope already owns: <see cref="KnotTraceArea"/> reports where
/// the finger is in the map's own space, and <see cref="UILine"/> draws the stroke. The pen
/// is carried along so its nib sits on the line being drawn.
///
/// <see cref="Play"/> is a coroutine so QuizHandler can simply yield on it and carry on with
/// the next question once it returns.
/// </summary>
public class PenAndPaperMinigame : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private CanvasGroup panelGroup;
    [Tooltip("Blurs the quiz behind the minigame, the same way the correct / wrong overlay " +
             "does. Optional.")]
    [SerializeField] private ScreenBlurBackdrop backdrop;

    [Header("Instruction")]
    [SerializeField] private CanvasGroup instructionCard;
    [SerializeField] private TextMeshProUGUI instructionLabel;
    [SerializeField, TextArea] private string instructionText =
        "Draw a way to the green field to reach the evacuation area on the paper";

    [Header("Map")]
    [Tooltip("The map. Its pixels are what say where the roads, the field and the house are, " +
             "so its texture has to be imported with Read/Write on and its rect kept square " +
             "to the sprite — the route is mapped straight onto texture pixels.")]
    [SerializeField] private Image mapImage;
    [Tooltip("The rect the route is drawn in. Normally the map's own rect.")]
    [SerializeField] private RectTransform mapRect;
    [SerializeField] private KnotTraceArea traceArea;
    [SerializeField] private UILine routeLine;
    [Tooltip("Marks the road outside the house — where the route has to start. Optional.")]
    [SerializeField] private Image startMarker;

    [Header("Route")]
    [Tooltip("Where the route sets off from — the street at the foot of the marked house, as " +
             "a fraction of the sprite across and up. Snapped to the nearest road when the " +
             "panel opens, so it only has to be roughly right, and so it can never end up " +
             "somewhere the route is not allowed to be.\n\n" +
             "The house fronts onto the road below its block, not the one along the top of " +
             "the paper, so this sits under the building rather than over it.")]
    [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("houseNormalised")]
    private Vector2 doorstepNormalised = new Vector2(0.145f, 0.672f);
    [Tooltip("How near the marked start the pen has to come to begin a route, in map units. " +
             "Generous: the point is the route, not the precision of the first touch.")]
    [SerializeField] private float startRadius = 44f;
    [Tooltip("How far off the tarmac the pen may stray before the route is handed back, in " +
             "map units. Roughly a road's width, so cutting a corner is forgiven but cutting " +
             "through a block is not.")]
    [SerializeField] private float strayTolerance = 16f;
    [Tooltip("How near the green field counts as arriving, in map units.")]
    [SerializeField] private float goalReach = 14f;

    [Header("Pen")]
    [Tooltip("The pen. Carried so its nib sits on the end of the line while a route is being " +
             "drawn, and glides back to its resting spot when the pen is lifted.")]
    [SerializeField] private RectTransform pen;
    [Tooltip("Where the nib sits relative to the middle of the pen's rect, in panel units. " +
             "Read off the 32x64 pen sprite, whose nib is the bottom pixel of the art.")]
    [SerializeField] private Vector2 penNibOffset = new Vector2(0f, -167f);
    [Tooltip("How quickly the pen glides back to its resting spot. Higher is snappier.")]
    [SerializeField] private float penReturnSpeed = 9f;

    [Header("Colours")]
    [SerializeField] private Color routeColor = new Color(0.16f, 0.78f, 0.22f, 1f);
    [SerializeField] private Color arrivedColor = new Color(0.42f, 0.85f, 0.35f, 1f);
    [SerializeField] private Color startMarkerColor = new Color(1f, 0.83f, 0.25f, 1f);

    [Header("Timing")]
    [SerializeField] private float panelFadeDuration = 0.25f;
    [SerializeField] private float instructionFadeDuration = 0.3f;
    [Tooltip("Pause once the field is reached, before the minigame closes.")]
    [SerializeField] private float finishDelay = 0.9f;

    [Header("Finish")]
    [Tooltip("Optional. Flashed once the route reaches the field.")]
    [SerializeField] private GameObject completedBanner;

    // Everything below is read off the map's own palette. The roads are the darkest ink on
    // the paper; the field is the one large patch of bright green.

    /// <summary>Brightest a pixel may be and still count as tarmac.</summary>
    private const int ROAD_INK_MAX = 100;

    /// <summary>
    /// How much greener than its other channels a dark pixel may be before it is read as
    /// foliage rather than tarmac. The map's hedges and tree canopies are dark enough to pass
    /// the brightness test on their own, and this is what keeps them out of the road network.
    /// </summary>
    private const int ROAD_INK_GREEN_BIAS = 25;

    /// <summary>The evacuation field's green, which nothing else on the paper comes near.</summary>
    private const int FIELD_GREEN_MIN = 120;
    private const int FIELD_RED_MAX = 110;
    private const int FIELD_BLUE_MAX = 80;
    private const int FIELD_GREEN_OVER_RED = 50;

    private bool isPlaying = false;
    private bool arrived = false;

    // The map, as read off the sprite. Indexed y * mapWidth + x, with y counted up from the
    // bottom the way texture pixels are.
    private bool[] road;
    private bool[] field;
    private int mapWidth;
    private int mapHeight;
    private bool mapBuilt = false;

    /// <summary>Where the route has to start, in the map rect's own space.</summary>
    private Vector2 startPoint;

    private readonly List<Vector2> stroke = new List<Vector2>();

    private Vector2 penRest;
    private bool penRestCaptured = false;

    public bool IsPlaying { get { return isPlaying; } }

    private void Awake()
    {
        if (panelGroup == null && panelRoot != null)
            panelGroup = panelRoot.GetComponent<CanvasGroup>();

        if (instructionLabel != null && !string.IsNullOrEmpty(instructionText))
            instructionLabel.text = instructionText;

        // Reset the contents but leave the panel's active state alone. Awake first runs
        // during the SetActive in Open, so deactivating here would switch the panel back off
        // underneath the very coroutine that just turned it on.
        ResetVisuals();
    }

    private void OnEnable()
    {
        if (traceArea != null)
        {
            traceArea.Pressed += OnPressed;
            traceArea.Moved += OnMoved;
            traceArea.Released += OnReleased;
        }
    }

    private void OnDisable()
    {
        if (traceArea != null)
        {
            traceArea.Pressed -= OnPressed;
            traceArea.Moved -= OnMoved;
            traceArea.Released -= OnReleased;
        }
    }

    /// <summary>
    /// Runs the whole minigame and returns once it has closed.
    /// Yield on this from the quiz; it never returns early or leaves the panel up.
    /// </summary>
    public IEnumerator Play()
    {
        if (isPlaying)
            yield break;

        if (mapImage == null || mapRect == null || traceArea == null || routeLine == null)
        {
            // Bail loudly rather than quietly: a half-wired panel would otherwise hang the
            // quiz on a map that can never be crossed.
            Debug.LogWarning("[PenAndPaperMinigame] Needs the map image, the map rect, a trace " +
                             "area and a line — skipping.", this);
            yield break;
        }

        isPlaying = true;

        Open();

        if (!mapBuilt)
        {
            // The roads could not be read, so there is no route to draw. Say so and let the
            // quiz carry on rather than parking it on an impossible screen.
            Debug.LogWarning("[PenAndPaperMinigame] Could not read the map's roads — skipping.", this);
            Close();
            isPlaying = false;
            yield break;
        }

        if (backdrop != null)
            yield return StartCoroutine(backdrop.CaptureIncludingUIRoutine());

        yield return FadeGroup(panelGroup, 0f, 1f, panelFadeDuration);
        yield return FadeGroup(instructionCard, 0f, 1f, instructionFadeDuration);

        traceArea.SetArmed(true);

        while (!arrived)
            yield return null;

        traceArea.SetArmed(false);

        if (completedBanner != null)
            completedBanner.SetActive(true);

        yield return new WaitForSecondsRealtime(finishDelay);

        yield return FadeGroup(instructionCard, 1f, 0f, instructionFadeDuration);
        yield return FadeGroup(panelGroup, 1f, 0f, panelFadeDuration);

        if (backdrop != null)
            yield return StartCoroutine(backdrop.FadeOutRoutine());

        Close();
        isPlaying = false;
    }

    // ------------------------------------------------------------------ drawing

    private void OnPressed(Vector2 p)
    {
        if (!isPlaying || arrived)
            return;

        // A route only starts outside the marked house, so it always reads as leaving home
        // rather than as a line drawn from wherever the finger happened to land.
        if (Vector2.Distance(p, startPoint) > startRadius)
        {
            ResetStroke();
            return;
        }

        // Snapped to the marked spot, so every route leaves from the same doorstep however
        // roughly it was tapped.
        stroke.Clear();
        stroke.Add(startPoint);
        routeLine.SetPoints(stroke);
    }

    private void OnMoved(Vector2 p)
    {
        if (!isPlaying || arrived || stroke.Count == 0)
            return;

        // Off the tarmac hands the route back rather than failing the round
        if (!IsOnRoad(p, strayTolerance) && !IsOnField(p, goalReach))
        {
            ResetStroke();
            return;
        }

        stroke.Add(p);
        routeLine.AddPoint(p);
        CarryPen(p);

        if (IsOnField(p, goalReach))
            Arrive();
    }

    private void OnReleased()
    {
        // Lifting the pen short of the field is not a failure, just a fresh sheet
        if (isPlaying && !arrived)
            ResetStroke();
    }

    private void Arrive()
    {
        arrived = true;
        routeLine.color = arrivedColor;
    }

    private void ResetStroke()
    {
        stroke.Clear();

        if (routeLine != null)
        {
            routeLine.Clear();
            routeLine.color = routeColor;
        }
    }

    // ------------------------------------------------------------------ the map

    /// <summary>
    /// Reads the roads, the field and the doorstep off the map sprite.
    ///
    /// The roads are every pixel dark enough to be tarmac and not green enough to be a hedge
    /// — but that alone also catches the black outlines drawn round the buildings, which
    /// would let a route be started in the middle of a block. So only the largest connected
    /// run of that ink is kept: the street network is one piece some thirty times the size of
    /// the biggest outline, and the islands drop out on their own.
    /// </summary>
    private void BuildMap()
    {
        mapBuilt = false;

        Sprite sprite = mapImage != null ? mapImage.sprite : null;
        if (sprite == null || sprite.texture == null)
            return;

        Texture2D tex = sprite.texture;
        if (!tex.isReadable)
        {
            Debug.LogWarning("[PenAndPaperMinigame] The map's texture is not readable — turn " +
                             "Read/Write on for " + tex.name + ".", this);
            return;
        }

        mapWidth = Mathf.RoundToInt(sprite.rect.width);
        mapHeight = Mathf.RoundToInt(sprite.rect.height);
        if (mapWidth <= 0 || mapHeight <= 0)
            return;

        int originX = Mathf.RoundToInt(sprite.rect.x);
        int originY = Mathf.RoundToInt(sprite.rect.y);

        Color32[] pixels = tex.GetPixels32();
        int texWidth = tex.width;

        bool[] ink = new bool[mapWidth * mapHeight];
        field = new bool[mapWidth * mapHeight];

        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                Color32 c = pixels[(originY + y) * texWidth + (originX + x)];
                int i = y * mapWidth + x;
                ink[i] = IsRoadInk(c);
                field[i] = IsField(c);
            }
        }

        road = LargestRun(ink);

        startPoint = PixelToLocal(NearestRoadPixel(doorstepNormalised));

        if (startMarker != null)
        {
            ((RectTransform)startMarker.transform).anchoredPosition = startPoint;
            startMarker.color = startMarkerColor;
        }

        mapBuilt = true;
    }

    private static bool IsRoadInk(Color32 c)
    {
        if (c.a < 128)
            return false;

        int brightest = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        if (brightest > ROAD_INK_MAX)
            return false;

        return c.g - Mathf.Max(c.r, c.b) < ROAD_INK_GREEN_BIAS;
    }

    private static bool IsField(Color32 c)
    {
        return c.a >= 128
            && c.g > FIELD_GREEN_MIN
            && c.r < FIELD_RED_MAX
            && c.b < FIELD_BLUE_MAX
            && c.g - c.r > FIELD_GREEN_OVER_RED;
    }

    /// <summary>
    /// The largest four-connected run in a mask, with everything else dropped. An explicit
    /// stack rather than recursion, since a mask this size would otherwise be deep enough to
    /// matter.
    /// </summary>
    private bool[] LargestRun(bool[] mask)
    {
        bool[] visited = new bool[mask.Length];
        bool[] best = new bool[mask.Length];
        int bestSize = 0;

        List<int> run = new List<int>();
        Stack<int> pending = new Stack<int>();

        for (int seed = 0; seed < mask.Length; seed++)
        {
            if (!mask[seed] || visited[seed])
                continue;

            run.Clear();
            pending.Clear();
            pending.Push(seed);
            visited[seed] = true;

            while (pending.Count > 0)
            {
                int i = pending.Pop();
                run.Add(i);

                int x = i % mapWidth;
                int y = i / mapWidth;

                if (x > 0) PushIf(mask, visited, pending, i - 1);
                if (x < mapWidth - 1) PushIf(mask, visited, pending, i + 1);
                if (y > 0) PushIf(mask, visited, pending, i - mapWidth);
                if (y < mapHeight - 1) PushIf(mask, visited, pending, i + mapWidth);
            }

            if (run.Count <= bestSize)
                continue;

            bestSize = run.Count;
            best = new bool[mask.Length];
            foreach (int i in run)
                best[i] = true;
        }

        return best;
    }

    private static void PushIf(bool[] mask, bool[] visited, Stack<int> pending, int i)
    {
        if (!mask[i] || visited[i])
            return;

        visited[i] = true;
        pending.Push(i);
    }

    /// <summary>
    /// The road pixel nearest a spot given as fractions of the map, which is how the doorstep
    /// is named. Brute force over the mask: it runs once when the panel opens, on sixteen
    /// thousand pixels.
    /// </summary>
    private Vector2 NearestRoadPixel(Vector2 normalised)
    {
        float wantX = normalised.x * mapWidth;
        float wantY = normalised.y * mapHeight;

        int bestIndex = -1;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < road.Length; i++)
        {
            if (!road[i])
                continue;

            float dx = (i % mapWidth) - wantX;
            float dy = (i / mapWidth) - wantY;
            float d = dx * dx + dy * dy;

            if (d < bestDistance)
            {
                bestDistance = d;
                bestIndex = i;
            }
        }

        // Nothing found only happens on a map with no roads at all, which BuildMap has
        // already refused to accept.
        return bestIndex < 0
            ? Vector2.zero
            : new Vector2(bestIndex % mapWidth, bestIndex / mapWidth);
    }

    private bool IsOnRoad(Vector2 p, float tolerance)
    {
        return IsNear(road, p, tolerance);
    }

    private bool IsOnField(Vector2 p, float tolerance)
    {
        return IsNear(field, p, tolerance);
    }

    /// <summary>
    /// Whether a point in the map's space is within <paramref name="tolerance"/> map units of
    /// any pixel in a mask. Only the pixels that could possibly be in reach are looked at, so
    /// this stays cheap enough to run on every frame of a drag.
    /// </summary>
    private bool IsNear(bool[] mask, Vector2 p, float tolerance)
    {
        if (mask == null)
            return false;

        Vector2 unit = UnitsPerPixel();
        if (unit.x <= 0f || unit.y <= 0f)
            return false;

        Vector2 at = LocalToPixel(p);
        int radius = Mathf.CeilToInt(tolerance / Mathf.Min(unit.x, unit.y)) + 1;
        int centreX = Mathf.RoundToInt(at.x);
        int centreY = Mathf.RoundToInt(at.y);
        float toleranceSq = tolerance * tolerance;

        for (int y = centreY - radius; y <= centreY + radius; y++)
        {
            if (y < 0 || y >= mapHeight)
                continue;

            for (int x = centreX - radius; x <= centreX + radius; x++)
            {
                if (x < 0 || x >= mapWidth || !mask[y * mapWidth + x])
                    continue;

                float dx = (x - at.x) * unit.x;
                float dy = (y - at.y) * unit.y;

                if (dx * dx + dy * dy <= toleranceSq)
                    return true;
            }
        }

        return false;
    }

    private Vector2 UnitsPerPixel()
    {
        Vector2 size = mapRect.rect.size;
        return new Vector2(
            mapWidth > 0 ? size.x / mapWidth : 0f,
            mapHeight > 0 ? size.y / mapHeight : 0f);
    }

    /// <summary>The centre of a map pixel, in the map rect's own space.</summary>
    private Vector2 PixelToLocal(Vector2 pixel)
    {
        Vector2 size = mapRect.rect.size;
        return new Vector2(
            ((pixel.x + 0.5f) / mapWidth - 0.5f) * size.x,
            ((pixel.y + 0.5f) / mapHeight - 0.5f) * size.y);
    }

    /// <summary>The map pixel a point in the map rect's space falls on, kept fractional.</summary>
    private Vector2 LocalToPixel(Vector2 p)
    {
        Vector2 size = mapRect.rect.size;
        return new Vector2(
            size.x != 0f ? (p.x / size.x + 0.5f) * mapWidth - 0.5f : 0f,
            size.y != 0f ? (p.y / size.y + 0.5f) * mapHeight - 0.5f : 0f);
    }

    // ------------------------------------------------------------------ the pen

    /// <summary>
    /// Puts the pen's nib on a point of the route. The point arrives in the map's space and
    /// the pen hangs off the panel, so it is carried across through the world rather than
    /// assuming the two rects line up.
    /// </summary>
    private void CarryPen(Vector2 mapPoint)
    {
        if (pen == null || mapRect == null)
            return;

        RectTransform panel = PanelRect();
        if (panel == null)
            return;

        Vector3 world = mapRect.TransformPoint(mapPoint);
        Vector2 local = panel.InverseTransformPoint(world);

        pen.anchoredPosition = local - penNibOffset;
    }

    private void Update()
    {
        // Idle, or between routes, the pen drifts back to where it was laid down. Unscaled,
        // so it keeps moving behind the blurred quiz.
        if (pen == null || !penRestCaptured)
            return;

        if (isPlaying && !arrived && traceArea != null && traceArea.IsHeld && stroke.Count > 0)
            return;

        pen.anchoredPosition = Vector2.Lerp(
            pen.anchoredPosition, penRest,
            1f - Mathf.Exp(-penReturnSpeed * Time.unscaledDeltaTime));
    }

    private RectTransform PanelRect()
    {
        return panelRoot != null
            ? panelRoot.transform as RectTransform
            : transform as RectTransform;
    }

    // ------------------------------------------------------------------ lifecycle

    private void Open()
    {
        if (panelRoot != null)
            panelRoot.SetActive(true);

        if (panelGroup != null)
        {
            panelGroup.alpha = 0f;
            panelGroup.blocksRaycasts = true;
        }

        if (instructionCard != null)
            instructionCard.alpha = 0f;

        if (completedBanner != null)
            completedBanner.SetActive(false);

        arrived = false;

        // Sizes are only real once the layout has been built, so the map is read here rather
        // than in Awake.
        Canvas.ForceUpdateCanvases();

        if (pen != null && !penRestCaptured)
        {
            penRest = pen.anchoredPosition;
            penRestCaptured = true;
        }

        BuildMap();

        traceArea.Configure(mapRect, panelRoot != null ? panelRoot.GetComponentInParent<Canvas>() : null);
        traceArea.SetArmed(false);

        ResetStroke();

        if (pen != null && penRestCaptured)
            pen.anchoredPosition = penRest;
    }

    private void Close()
    {
        ResetVisuals();

        if (backdrop != null)
            backdrop.Clear();

        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    /// <summary>
    /// Blanks the sheet and takes the pen out of play, without touching whether the panel
    /// itself is on — see the note in Awake.
    /// </summary>
    private void ResetVisuals()
    {
        if (traceArea != null)
            traceArea.SetArmed(false);

        arrived = false;
        ResetStroke();

        if (pen != null && penRestCaptured)
            pen.anchoredPosition = penRest;

        if (completedBanner != null)
            completedBanner.SetActive(false);

        if (panelGroup == null)
            return;

        panelGroup.alpha = 0f;
        panelGroup.blocksRaycasts = false;
    }

    private IEnumerator FadeGroup(CanvasGroup group, float from, float to, float duration)
    {
        if (group == null)
            yield break;

        if (duration <= 0f)
        {
            group.alpha = to;
            yield break;
        }

        group.alpha = from;

        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }

        group.alpha = to;
    }

    /// <summary>
    /// Shuts the minigame down mid-play, for when the quiz's minigame timer runs out.
    /// QuizHandler stops the Play coroutine itself; this clears everything it left up.
    /// </summary>
    public void ForceClose()
    {
        StopAllCoroutines();
        Close();
        isPlaying = false;
    }
}

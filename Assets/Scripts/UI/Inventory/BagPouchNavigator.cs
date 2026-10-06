using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Steps through the go-bag's pouches with the arrows above it, instead of tapping the pouches.
/// When the inventory comes up the view zooms in on the pouch last looked at and opens it; each
/// arrow tap then closes the open pouch, pans to the next one and opens that. The zoom stays the
/// same for the whole visit - the most that still fits the largest pouch - so moving only pans.
///
/// Only the bag art zooms. Each pouch's grid sits outside the zoom, at its normal size, and is
/// placed over the pouch as it opens.
///
/// Sits on the go-bag side of the inventory panel, which clips the zoomed bag to its side of the
/// screen. Pouches are framed below the header, though the art runs on behind it.
/// </summary>
public class BagPouchNavigator : MonoBehaviour
{
    [Header("View")]
    [Tooltip("Scaled and moved to zoom and pan. Holds the bag art and stretches over its parent.")]
    [SerializeField] private RectTransform zoomRoot;
    [Tooltip("How much of the view the largest pouch fills, across its longer side.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float pouchFill = 0.8f;
    [Tooltip("The furthest the bag zooms in, however small its pouches.")]
    [SerializeField] private float maxZoom = 4f;
    [Tooltip("Kept clear around the view's edge, in reference units.")]
    [SerializeField] private float padding = 16f;
    [Tooltip("Clips the zoomed bag, fading it out at its softness. Its sides are moved off-screen where they meet the screen edge, so the fade only shows where the bag meets the storage or quiz.")]
    [SerializeField] private RectMask2D viewportClip;
    [SerializeField] private float zoomDuration = 0.4f;
    [SerializeField] private float panDuration = 0.35f;

    [Header("Controls")]
    [Tooltip("The strip above the bag holding the arrows and label. Pouches are framed below it.")]
    [SerializeField] private RectTransform header;
    [SerializeField] private Button previousButton;
    [SerializeField] private Button nextButton;
    [SerializeField] private TextMeshProUGUI label;

    private IBagPouches bag;
    // The open pouch, -1 while none is (opening, closing or moving between them)
    private int current = -1;
    // Where the next visit starts: the pouch last moved to
    private int remembered;
    private Coroutine running;

    /// <summary>The open pouch, or -1 while none is open.</summary>
    public int CurrentPouch => current;
    public int PouchCount => bag != null ? bag.PouchCount : 0;

    /// <summary>The open pouch's grid, or null while none is open.</summary>
    public RectTransform OpenPouchGrid => bag != null && current >= 0 ? bag.GetPouchGrid(current) : null;

    /// <summary>The arrows and label, for the onboarding to point at.</summary>
    public RectTransform Controls => header;

    void Awake()
    {
        if (previousButton != null)
            previousButton.onClick.AddListener(() => Step(-1));
        if (nextButton != null)
            nextButton.onClick.AddListener(() => Step(1));
    }

    void OnDisable()
    {
        // Unity has already stopped the coroutines
        running = null;
        current = -1;
        ResetView();
    }

    // The go-bag side resizes on rotation and between full and half screen
    void OnRectTransformDimensionsChange()
    {
        UpdateClip();

        if (running == null && current >= 0 && bag != null && isActiveAndEnabled)
        {
            Rect view = Viewport();
            ApplyView(view, FitZoom(view), AreaCenter(current));
            PlaceGrid(current, view);
        }
    }

    /// <summary>
    /// Starts a visit to the bag: snaps it shut and back to the whole-bag view, then zooms in on
    /// the pouch last looked at and opens it.
    /// </summary>
    public void Show(IBagPouches newBag)
    {
        if (running != null)
        {
            StopCoroutine(running);
            running = null;
        }

        if (newBag != bag)
        {
            // A different bag starts at its first pouch, and the old one shouldn't stay open
            if (bag != null)
                bag.ResetClosed();
            remembered = 0;
        }

        bag = newBag;
        current = -1;
        ResetView();
        UpdateClip();

        if (bag != null)
            bag.ResetClosed();

        if (bag == null || bag.PouchCount == 0 || !isActiveAndEnabled)
        {
            UpdateControls(-1);
            return;
        }

        remembered = Mathf.Clamp(remembered, 0, bag.PouchCount - 1);
        UpdateControls(remembered);
        running = StartCoroutine(Enter(remembered));
    }

    private IEnumerator Enter(int pouch)
    {
        // The go-bag side was just laid out for this mode; let the bag fit itself to it first
        yield return null;

        Rect view = Viewport();
        yield return MoveView(view, FitZoom(view), AreaCenter(pouch), zoomDuration);

        PlaceGrid(pouch, view);
        yield return bag.OpenPouch(pouch, -1);

        current = pouch;
        running = null;
    }

    private void Step(int direction)
    {
        if (bag == null || running != null || current < 0 || bag.PouchCount < 2
            || InventoryItemDragHandler.IsAnyItemBeingDragged)
            return;

        int count = bag.PouchCount;
        int next = ((current + direction) % count + count) % count;
        running = StartCoroutine(MoveTo(next));
    }

    private IEnumerator MoveTo(int next)
    {
        int from = current;
        current = -1;
        remembered = next;
        UpdateControls(next);

        yield return bag.ClosePouch(from, next);

        Rect view = Viewport();
        yield return MoveView(view, zoomRoot.localScale.x, AreaCenter(next), panDuration);

        PlaceGrid(next, view);
        yield return bag.OpenPouch(next, from);

        current = next;
        running = null;
    }

    private void UpdateControls(int pouch)
    {
        int count = bag != null ? bag.PouchCount : 0;

        if (header != null)
            header.gameObject.SetActive(count > 0);

        bool several = count > 1;
        if (previousButton != null)
            previousButton.gameObject.SetActive(several);
        if (nextButton != null)
            nextButton.gameObject.SetActive(several);

        if (label != null && pouch >= 0 && pouch < count)
            label.text = bag.GetPouchName(pouch);
    }

    // --- View ------------------------------------------------------------------------------

    private IEnumerator MoveView(Rect view, float toZoom, Vector2 toCenter, float duration)
    {
        if (zoomRoot == null)
            yield break;

        float fromZoom = zoomRoot.localScale.x;
        Vector2 fromCenter = (view.center - (Vector2)zoomRoot.localPosition) / fromZoom;

        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float e = Mathf.SmoothStep(0f, 1f, t / duration);
            ApplyView(view, Mathf.Lerp(fromZoom, toZoom, e), Vector2.Lerp(fromCenter, toCenter, e));
            yield return null;
        }

        ApplyView(view, toZoom, toCenter);
    }

    /// <summary>Zooms so <paramref name="center"/> (in zoomRoot space) sits in the middle of the view.</summary>
    private void ApplyView(Rect view, float zoom, Vector2 center)
    {
        if (zoomRoot == null)
            return;

        zoomRoot.localScale = new Vector3(zoom, zoom, 1f);
        zoomRoot.localPosition = view.center - center * zoom;
    }

    private void ResetView()
    {
        if (zoomRoot == null)
            return;

        zoomRoot.localScale = Vector3.one;
        zoomRoot.anchoredPosition = Vector2.zero;
    }

    /// <summary>
    /// Fades the bag out where it meets the storage or quiz beside it, rather than cutting it
    /// off there. At the screen's own edges the clip is pushed off-screen by the fade's width.
    /// </summary>
    private void UpdateClip()
    {
        if (viewportClip == null)
            return;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
            return;

        RectTransform self = (RectTransform)transform;
        Rect screen = BoundsIn(self, (RectTransform)canvas.rootCanvas.transform);
        Rect side = self.rect;
        float fade = viewportClip.softness.x;

        RectTransform clip = viewportClip.rectTransform;
        clip.offsetMin = new Vector2(side.xMin <= screen.xMin + 1f ? -fade : 0f, clip.offsetMin.y);
        clip.offsetMax = new Vector2(side.xMax >= screen.xMax - 1f ? fade : 0f, clip.offsetMax.y);
    }

    /// <summary>
    /// Centers the pouch's grid over the pouch as it sits on screen now, nudged back inside the
    /// view where it would hang off an edge.
    /// </summary>
    private void PlaceGrid(int pouch, Rect view)
    {
        RectTransform grid = bag.GetPouchGrid(pouch);
        RectTransform area = bag.GetPouchArea(pouch);
        if (grid == null || area == null || zoomRoot == null)
            return;

        Transform space = zoomRoot.parent;
        Rect bounds = BoundsIn(space, grid);
        Vector2 target = BoundsIn(space, area).center;

        // Keep the whole grid in view; one bigger than the view stays centered on it
        Vector2 half = bounds.size * 0.5f;
        target.x = half.x * 2f >= view.width ? view.center.x : Mathf.Clamp(target.x, view.xMin + half.x, view.xMax - half.x);
        target.y = half.y * 2f >= view.height ? view.center.y : Mathf.Clamp(target.y, view.yMin + half.y, view.yMax - half.y);

        grid.position += space.TransformVector(target - bounds.center);
    }

    /// <summary>
    /// The view: inside the clip's fade, below the header and inside the padding, in zoomRoot's
    /// parent space.
    /// </summary>
    private Rect Viewport()
    {
        RectTransform self = (RectTransform)transform;
        Rect r = self.rect;

        if (viewportClip != null)
        {
            Rect clip = BoundsIn(self, viewportClip.rectTransform);
            Vector2Int fade = viewportClip.softness;

            // The fade is only where the bag meets the storage or quiz, so trim the same from
            // both sides: pouches stay centered under the header, which centers on this side
            float trim = Mathf.Max(clip.xMin + fade.x - r.xMin, r.xMax - (clip.xMax - fade.x), 0f);
            r.xMin += trim;
            r.xMax -= trim;
            r.yMin = Mathf.Max(r.yMin, clip.yMin + fade.y);
            r.yMax = Mathf.Min(r.yMax, clip.yMax - fade.y);
        }

        if (header != null && header.gameObject.activeInHierarchy)
            r.yMax = Mathf.Min(r.yMax, BoundsIn(self, header).yMin);

        r.xMin += padding;
        r.xMax -= padding;
        r.yMin += padding;
        r.yMax -= padding;

        Transform space = zoomRoot != null ? zoomRoot.parent : self;
        Vector2 min = space.InverseTransformPoint(self.TransformPoint(r.min));
        Vector2 max = space.InverseTransformPoint(self.TransformPoint(r.max));
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    /// <summary>
    /// The one zoom for the visit: the largest pouch fills <see cref="pouchFill"/> of the view,
    /// so the smaller ones fit too and moving between them never needs to zoom again.
    /// </summary>
    private float FitZoom(Rect view)
    {
        float zoom = maxZoom;

        for (int i = 0; i < bag.PouchCount; i++)
        {
            RectTransform area = bag.GetPouchArea(i);
            if (area == null)
                continue;

            Rect r = BoundsIn(zoomRoot, area);
            if (r.width > 0f)
                zoom = Mathf.Min(zoom, view.width * pouchFill / r.width);
            if (r.height > 0f)
                zoom = Mathf.Min(zoom, view.height * pouchFill / r.height);
        }

        return Mathf.Max(zoom, 0.1f);
    }

    /// <summary>The middle of the pouch on the bag art, in zoomRoot space (the same at any zoom).</summary>
    private Vector2 AreaCenter(int pouch)
    {
        RectTransform area = bag.GetPouchArea(pouch);
        return area != null ? BoundsIn(zoomRoot, area).center : Vector2.zero;
    }

    /// <summary>The rect's corners in another transform's space. Works on inactive objects too.</summary>
    private static Rect BoundsIn(Transform space, RectTransform rect)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector3 corner in corners)
        {
            Vector2 p = space.InverseTransformPoint(corner);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }

        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
}

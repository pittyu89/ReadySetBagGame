using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shrinks a VerticalLayoutGroup column just enough to fit the height it was given.
///
/// The menu is authored at a fixed pixel height (logo + buttons + icon row = 685 units), but
/// the CanvasScaler only hands out 720 units of height at 16:9. Every taller phone gets less:
/// 644 at 20:9, 624 at 21:9. The overflow falls off the bottom of the screen, which is why the
/// Tutorial and About buttons - the last row - were the ones getting cut.
///
/// Rather than re-authoring every element per aspect, this scales the whole column down
/// uniformly so the layout keeps its proportions and simply gets a little smaller on tall
/// phones. Scaling is anchored to the top, so the logo stays put and the slack is taken off
/// the bottom.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class VerticalMenuFitter : MonoBehaviour
{
    [Tooltip("Never shrink past this. Below roughly 0.7 the buttons get too small to tap " +
             "comfortably; better to let it clip than to render an untappable menu.")]
    [SerializeField, Range(0.3f, 1f)] private float minScale = 0.7f;

    [Tooltip("Extra breathing room kept below the last row, in reference units.")]
    [SerializeField] private float bottomMargin = 0f;

    private RectTransform self;
    private VerticalLayoutGroup layout;
    private float lastAvailable = -1f;
    private float lastNeeded = -1f;

    private void OnEnable()
    {
        self = (RectTransform)transform;
        layout = GetComponent<VerticalLayoutGroup>();

        // Scale from the top edge so shrinking pulls the column up off the bottom instead of
        // splitting the difference between top and bottom. With stretch anchors the rect is
        // driven by the offsets, so moving the pivot changes only the scaling origin.
        if (self.pivot != new Vector2(0.5f, 1f))
            self.pivot = new Vector2(0.5f, 1f);

        Fit();
    }

    // Fires when the canvas resizes - orientation changes, and the initial layout pass
    private void OnRectTransformDimensionsChange()
    {
        Fit();
    }

    private void Update()
    {
        // Children are shown/hidden at runtime, so the required height is not fixed at startup
        Fit();
    }

    private void Fit()
    {
        if (self == null)
            return;

        float available = self.rect.height - bottomMargin;
        float needed = MeasureContent();

        if (needed <= 0f || available <= 0f)
            return;

        // Nothing changed since the last pass - skip the write so we don't dirty the transform
        if (Mathf.Approximately(available, lastAvailable) && Mathf.Approximately(needed, lastNeeded))
            return;

        lastAvailable = available;
        lastNeeded = needed;

        float scale = needed > available
            ? Mathf.Max(minScale, available / needed)
            : 1f;

        if (!Mathf.Approximately(transform.localScale.x, scale))
            transform.localScale = new Vector3(scale, scale, 1f);
    }

    /// <summary>
    /// Total height the column wants: padding, every active child, and the gaps between them.
    ///
    /// Which height counts depends on the layout group. With childControlHeight off - how this
    /// menu is set up - the group leaves every child at its authored rect height, so that is
    /// what to measure. Asking for the preferred height instead would be wrong here: Image
    /// reports its sprite's native pixel height, which for the logo is far larger than the
    /// 206pt it is actually drawn at, and the column would shrink to fit a size nothing uses.
    /// </summary>
    private float MeasureContent()
    {
        bool controlsHeight = layout != null && layout.childControlHeight;

        float total = 0f;
        int counted = 0;

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (!child.gameObject.activeSelf)
                continue;

            RectTransform rt = child as RectTransform;
            if (rt == null)
                continue;

            total += controlsHeight ? LayoutUtility.GetPreferredHeight(rt) : rt.rect.height;
            counted++;
        }

        if (counted == 0)
            return 0f;

        if (layout != null)
        {
            total += layout.padding.top + layout.padding.bottom;
            total += layout.spacing * (counted - 1);
        }

        return total;
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A rect that follows what its parent actually draws, for the onboarding to frame and keep
/// its cards off. Some UI is authored around an anchor that doesn't match its picture - the
/// item description popup's children are larger than its own rect and stick out above it, and
/// a minigame objective's text runs past its card - so framing the rect itself misses.
///
/// Covers every visible graphic under the parent: images as drawn (a sprite kept to its
/// proportions is measured as it shows, not its box) and text by the words it renders.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class HighlightBounds : MonoBehaviour
{
    private RectTransform rect;

    /// <summary>The bounds of what <paramref name="target"/> draws, made the first time it is asked for.</summary>
    public static RectTransform For(RectTransform target)
    {
        if (target == null)
            return null;

        foreach (Transform child in target)
        {
            if (child.TryGetComponent(out HighlightBounds existing))
            {
                existing.Fit();
                return existing.rect;
            }
        }

        GameObject go = new GameObject("HighlightBounds", typeof(RectTransform));
        go.transform.SetParent(target, false);

        // Under a layout group it would otherwise be laid out like the parent's real children
        go.AddComponent<LayoutElement>().ignoreLayout = true;
        HighlightBounds bounds = go.AddComponent<HighlightBounds>();
        bounds.Fit();
        return bounds.rect;
    }

    // The popup animates and texts change, so keep up every frame while shown
    void LateUpdate()
    {
        Fit();
    }

    private void Fit()
    {
        if (rect == null)
        {
            rect = (RectTransform)transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        }

        Transform parent = rect.parent;

        // A popup scaled to nothing as it opens has no inverse yet; keep the last fit
        Vector3 scale = parent.lossyScale;
        if (Mathf.Abs(scale.x) < 1e-4f || Mathf.Abs(scale.y) < 1e-4f)
            return;

        bool any = false;
        Vector2 min = Vector2.zero, max = Vector2.zero;

        foreach (Graphic graphic in parent.GetComponentsInChildren<Graphic>(false))
        {
            if (!graphic.enabled || graphic.color.a <= 0.01f)
                continue;

            if (!TryGetDrawnRect(graphic, out Rect drawn))
                continue;

            Transform from = graphic.transform;
            foreach (Vector2 corner in new[] { drawn.min, drawn.max, new Vector2(drawn.xMin, drawn.yMax), new Vector2(drawn.xMax, drawn.yMin) })
            {
                Vector2 p = parent.InverseTransformPoint(from.TransformPoint(corner));
                if (!any)
                {
                    min = max = p;
                    any = true;
                }
                else
                {
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
            }
        }

        if (!any)
        {
            // Nothing to measure: fall back to the parent's own rect
            Rect own = ((RectTransform)parent).rect;
            min = own.min;
            max = own.max;
        }

        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.localPosition = (min + max) * 0.5f;
        rect.sizeDelta = max - min;
    }

    /// <summary>Where the graphic draws, in its own local space.</summary>
    private static bool TryGetDrawnRect(Graphic graphic, out Rect drawn)
    {
        drawn = graphic.rectTransform.rect;

        if (graphic is TMP_Text text)
        {
            if (string.IsNullOrEmpty(text.text))
                return false;

            Bounds bounds = text.textBounds;
            if (bounds.size.x <= 0f || bounds.size.y <= 0f)
                return false;

            drawn = new Rect(bounds.min, bounds.size);
            return true;
        }

        if (graphic is Image image && image.preserveAspect && image.sprite != null && image.type == Image.Type.Simple)
        {
            Vector2 sprite = image.sprite.rect.size;
            if (sprite.x > 0f && sprite.y > 0f)
            {
                // Fit inside the box, centred, as Image draws it
                float fit = Mathf.Min(drawn.width / sprite.x, drawn.height / sprite.y);
                Vector2 size = sprite * fit;
                drawn = new Rect(drawn.center - size * 0.5f, size);
            }
        }

        return drawn.width > 0f && drawn.height > 0f;
    }
}

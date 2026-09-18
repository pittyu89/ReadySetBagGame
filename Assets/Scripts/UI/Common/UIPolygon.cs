using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A flat-colored convex polygon drawn inside its RectTransform. Used for the slanted
/// shapes of the difficulty panel (bases, difficulty bars, arrows) so they can be built
/// without exported sprites. Also works as a Mask graphic and only catches clicks inside
/// the polygon, so neighbouring slanted buttons don't steal each other's taps.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class UIPolygon : MaskableGraphic, ICanvasRaycastFilter
{
    [Tooltip("Corners in normalized rect space ((0,0) bottom-left, (1,1) top-right), in order around the shape. Must be convex.")]
    [SerializeField] private Vector2[] points =
    {
        new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f)
    };

    [Tooltip("Optional opacity per corner (same order as Points) for simple gradients such as soft shadows. Leave empty for a flat color.")]
    [SerializeField] private float[] pointAlphas = new float[] { };

    public Vector2[] Points
    {
        get => points;
        set
        {
            points = value;
            SetVerticesDirty();
        }
    }

    public float[] PointAlphas
    {
        get => pointAlphas;
        set
        {
            pointAlphas = value;
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        if (points == null || points.Length < 3)
            return;

        Rect rect = GetPixelAdjustedRect();
        bool fade = pointAlphas != null && pointAlphas.Length == points.Length;

        for (int i = 0; i < points.Length; i++)
        {
            Vector2 p = points[i];
            Color32 vertexColor = color;
            if (fade)
                vertexColor.a = (byte)Mathf.RoundToInt(vertexColor.a * Mathf.Clamp01(pointAlphas[i]));

            vh.AddVert(new Vector3(rect.x + p.x * rect.width, rect.y + p.y * rect.height), vertexColor, p);
        }

        // Convex, so a fan from the first corner covers it
        for (int i = 1; i < points.Length - 1; i++)
            vh.AddTriangle(0, i, i + 1);
    }

    public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
    {
        if (points == null || points.Length < 3)
            return false;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, eventCamera, out Vector2 local))
            return false;

        Rect rect = rectTransform.rect;
        Vector2 n = new Vector2((local.x - rect.x) / rect.width, (local.y - rect.y) / rect.height);

        // Inside a convex polygon when the point sits on the same side of every edge
        bool? sign = null;
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[(i + 1) % points.Length];
            float cross = (b.x - a.x) * (n.y - a.y) - (b.y - a.y) * (n.x - a.x);

            if (Mathf.Approximately(cross, 0f))
                continue;

            if (sign == null)
                sign = cross > 0f;
            else if (sign != cross > 0f)
                return false;
        }

        return true;
    }
}

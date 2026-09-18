using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws a thick polyline inside a Canvas.
///
/// Unity's LineRenderer lives in world space and sorts against the 3D scene, which is no use
/// over a Screen Space UI panel — so the stroke the player traces is built here instead, as
/// a normal UI Graphic. Points are in this rect's local space.
///
/// Each segment is a quad, with a round-ish cap dropped at every joint so corners on the
/// figure-eight do not show a notch.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class UILine : MaskableGraphic
{
    [SerializeField] private float thickness = 10f;
    [Tooltip("Corners drawn at each joint. 0 leaves bare mitres; 6 is plenty for a smooth curve.")]
    [SerializeField, Range(0, 12)] private int jointSegments = 6;

    private readonly List<Vector2> points = new List<Vector2>();

    public int PointCount { get { return points.Count; } }

    public float Thickness
    {
        get { return thickness; }
        set { thickness = value; SetVerticesDirty(); }
    }

    public void Clear()
    {
        points.Clear();
        SetVerticesDirty();
    }

    public void SetPoints(IList<Vector2> pts)
    {
        points.Clear();
        if (pts != null)
            points.AddRange(pts);
        SetVerticesDirty();
    }

    /// <summary>
    /// Appends a point, unless it is so close to the last one that it would only add
    /// zero-length geometry.
    /// </summary>
    public void AddPoint(Vector2 p, float minStep = 2f)
    {
        if (points.Count > 0 && Vector2.Distance(points[points.Count - 1], p) < minStep)
            return;

        points.Add(p);
        SetVerticesDirty();
    }

    public Vector2 LastPoint
    {
        get { return points.Count > 0 ? points[points.Count - 1] : Vector2.zero; }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        if (points.Count == 0)
            return;

        float half = Mathf.Max(0.01f, thickness * 0.5f);

        // A single point still deserves a dot, so the stroke appears the moment it starts
        if (points.Count == 1)
        {
            AddDisc(vh, points[0], half);
            return;
        }

        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[i + 1];
            Vector2 dir = b - a;

            if (dir.sqrMagnitude < 0.0001f)
                continue;

            Vector2 n = new Vector2(-dir.y, dir.x).normalized * half;

            int root = vh.currentVertCount;
            vh.AddVert(a - n, color, Vector2.zero);
            vh.AddVert(a + n, color, Vector2.zero);
            vh.AddVert(b + n, color, Vector2.zero);
            vh.AddVert(b - n, color, Vector2.zero);
            vh.AddTriangle(root + 0, root + 1, root + 2);
            vh.AddTriangle(root + 2, root + 3, root + 0);
        }

        // Joints, so the curve does not show notches where segments meet
        if (jointSegments > 0)
        {
            for (int i = 1; i < points.Count - 1; i++)
                AddDisc(vh, points[i], half);

            AddDisc(vh, points[0], half);
            AddDisc(vh, points[points.Count - 1], half);
        }
    }

    private void AddDisc(VertexHelper vh, Vector2 centre, float radius)
    {
        int segments = Mathf.Max(3, jointSegments);
        int centreIndex = vh.currentVertCount;
        vh.AddVert(centre, color, Vector2.zero);

        for (int s = 0; s <= segments; s++)
        {
            float a = (s / (float)segments) * Mathf.PI * 2f;
            vh.AddVert(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, color, Vector2.zero);
        }

        for (int s = 0; s < segments; s++)
            vh.AddTriangle(centreIndex, centreIndex + 1 + s, centreIndex + 2 + s);
    }
}

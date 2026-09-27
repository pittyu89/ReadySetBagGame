using UnityEngine;

/// <summary>
/// Keeps an edge-anchored HUD element clear of notches, punch-holes and rounded corners.
///
/// The HUD pieces sit directly under the root Canvas (onboarding finds them by that path), so
/// rather than reparenting them into a safe-area container this nudges each one inward by the
/// inset on the side(s) it is anchored to. Centre-anchored axes are left alone.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SafeAreaOffset : MonoBehaviour
{
    private RectTransform rect;
    private Canvas canvas;
    private Vector2 basePosition;
    private Rect lastSafeArea;
    private Vector2Int lastScreen;
    private float lastScale;

    private void Awake()
    {
        rect = (RectTransform)transform;
        canvas = GetComponentInParent<Canvas>();
        basePosition = rect.anchoredPosition;
    }

    private void OnEnable()
    {
        lastScreen = Vector2Int.zero;
        Apply();
    }

    private void Update()
    {
        // Rotation between the two landscape orientations swaps which side the notch is on
        Apply();
    }

    private void Apply()
    {
        Rect safe = Screen.safeArea;
        var screen = new Vector2Int(Screen.width, Screen.height);
        float scale = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
        if (safe == lastSafeArea && screen == lastScreen && Mathf.Approximately(scale, lastScale))
            return;
        lastSafeArea = safe;
        lastScreen = screen;
        lastScale = scale;

        if (scale <= 0f)
            return;

        float left = safe.xMin / scale;
        float right = (screen.x - safe.xMax) / scale;
        float bottom = safe.yMin / scale;
        float top = (screen.y - safe.yMax) / scale;

        Vector2 offset = Vector2.zero;
        if (rect.anchorMax.x <= 0f) offset.x += left;
        else if (rect.anchorMin.x >= 1f) offset.x -= right;
        if (rect.anchorMax.y <= 0f) offset.y += bottom;
        else if (rect.anchorMin.y >= 1f) offset.y -= top;

        rect.anchoredPosition = basePosition + offset;
    }
}

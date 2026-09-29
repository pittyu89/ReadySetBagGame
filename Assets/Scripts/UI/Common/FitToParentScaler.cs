using UnityEngine;

/// <summary>
/// Scales a group up (or down) so its authored footprint fills the space its parent gives it.
///
/// The go-bag and the quiz share the screen differently depending on what is open: the bag has
/// the whole width while exploring but only half of it next to storage or the quiz, and phones
/// give more width than 16:9. A fixed scale either wastes the room or pushes the side pockets
/// into the other half, so this recomputes whenever the parent's rect changes.
///
/// The group's RectTransform should stretch to fill its parent; only its localScale is driven.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class FitToParentScaler : MonoBehaviour
{
    [Tooltip("Width and height the children actually cover at scale 1, in reference units.")]
    [SerializeField] private Vector2 contentSize = new Vector2(600f, 470f);

    [Tooltip("Kept clear on every side, in reference units.")]
    [SerializeField] private float padding = 16f;

    [SerializeField] private float minScale = 1f;
    [SerializeField] private float maxScale = 1.5f;

    private RectTransform rect;

    private void OnEnable()
    {
        rect = (RectTransform)transform;
        Fit();
    }

    // Fires when the parent resizes: orientation, aspect, and the full/half-screen switches
    private void OnRectTransformDimensionsChange()
    {
        Fit();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        rect = (RectTransform)transform;
        Fit();
    }
#endif

    private void Fit()
    {
        if (rect == null || contentSize.x <= 0f || contentSize.y <= 0f)
            return;

        Vector2 available = rect.rect.size - Vector2.one * (padding * 2f);
        if (available.x <= 0f || available.y <= 0f)
            return;

        float scale = Mathf.Min(available.x / contentSize.x, available.y / contentSize.y);
        scale = Mathf.Clamp(scale, minScale, maxScale);

        if (!Mathf.Approximately(transform.localScale.x, scale))
            transform.localScale = new Vector3(scale, scale, 1f);
    }
}

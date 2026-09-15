using UnityEngine;

/// <summary>
/// Uniformly scales a fixed-size layout (e.g. 1280x720) so all of it fits inside its
/// parent, whatever the screen's aspect ratio. Anything the layout draws beyond its own
/// rect (such as the difficulty panel's bases) fills the leftover space on wide or tall
/// screens instead of the content being cropped.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class ScaleToFitParent : MonoBehaviour
{
    [SerializeField] private Vector2 designSize = new Vector2(1280f, 720f);

    private RectTransform rectTransform;
    private Vector2 lastParentSize;

    void OnEnable()
    {
        Fit();
    }

    void LateUpdate()
    {
        RectTransform parent = transform.parent as RectTransform;
        if (parent != null && parent.rect.size != lastParentSize)
            Fit();
    }

    public void Fit()
    {
        if (rectTransform == null)
            rectTransform = (RectTransform)transform;

        RectTransform parent = transform.parent as RectTransform;
        if (parent == null || designSize.x <= 0f || designSize.y <= 0f)
            return;

        Vector2 parentSize = parent.rect.size;
        lastParentSize = parentSize;

        if (parentSize.x <= 0f || parentSize.y <= 0f)
            return;

        float scale = Mathf.Min(parentSize.x / designSize.x, parentSize.y / designSize.y);

        rectTransform.anchorMin = rectTransform.anchorMax = rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.sizeDelta = designSize;
        rectTransform.localScale = new Vector3(scale, scale, 1f);
    }
}

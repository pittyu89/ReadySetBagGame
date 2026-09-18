using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// One of the papers in the pile the important-documents round asks the player to sort — the
/// birth certificate, the Form 137-A, the certificate of enrolment, the medical certificate,
/// the certificate of recognition.
///
/// It only reports drag events and remembers where it was authored. Which folder is open,
/// whether a drop landed over it and whether the paper belongs in it are all decided by
/// <see cref="ImportantDocumentsMinigame"/>, the way <see cref="ZiplockItem"/> leaves the
/// packing rules to the ziplock panel.
///
/// A paper stays draggable once it is filed, so anything put away can be pulled back out.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class DocumentCard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public event Action<DocumentCard> PickedUp;
    public event Action<DocumentCard> Dropped;

    [Header("Identity")]
    [Tooltip("Which folder this paper belongs in. Has to match the documentId on one of the " +
             "minigame's folders exactly — the match is what decides whether a drop is taken.")]
    [SerializeField] private string documentId = "";

    [Header("Art")]
    [Tooltip("How much of this sprite's sheet is empty margin around the drawn certificate, " +
             "as a fraction of the rect — x left, y right, z top, w bottom.\n\n" +
             "The certificates are all drawn inset in their sheets. Filing one by its rect " +
             "would post that blank margin into the folder's opening and leave the folder " +
             "looking empty, so the minigame lines up the art instead. Read off the sprite: " +
             "the portrait sheets are 128x174 with the art at x16-111, the landscape one is " +
             "174x128 with the art at x16-159.")]
    [SerializeField] private Vector4 artMargins = new Vector4(0.125f, 0.125f, 0.092f, 0.103f);

    private RectTransform rect;
    private RectTransform dragSpace;
    private Canvas canvas;
    private bool armed;

    /// <summary>Where this paper was authored in the pile, so a reset puts it back.</summary>
    public Vector2 HomePosition { get; private set; }

    /// <summary>Authored size, restored when a paper is taken back out of a folder.</summary>
    public Vector2 HomeSize { get; private set; }

    /// <summary>
    /// Authored tilt. The pile is drawn as a scattered heap rather than a neat stack, so
    /// unlike the ziplock's items these do not go home square.
    /// </summary>
    public Quaternion HomeRotation { get; private set; }

    /// <summary>
    /// Where this paper sat in the heap. Restored on the way home so pulling one out and
    /// putting it back does not quietly shuffle the pile's draw order.
    /// </summary>
    public int HomeSiblingIndex { get; private set; }

    public string DocumentId { get { return documentId; } }

    /// <summary>
    /// The blank border around the drawn certificate, as fractions of this paper's rect:
    /// x left, y right, z top, w bottom. See the field's tooltip for why it matters.
    /// </summary>
    public Vector4 ArtMargins { get { return artMargins; } }

    /// <summary>Set by the panel once the paper has come to rest inside a folder.</summary>
    public bool IsFiled { get; set; }

    /// <summary>Which folder it is filed in, or -1 while it is still in the pile.</summary>
    public int FolderIndex { get; set; }

    public RectTransform Rect
    {
        get
        {
            if (rect == null)
                rect = (RectTransform)transform;
            return rect;
        }
    }

    public void Configure(RectTransform space, Canvas owningCanvas)
    {
        dragSpace = space;
        canvas = owningCanvas;
        FolderIndex = -1;
    }

    /// <summary>
    /// Remembers where the paper was laid in the editor. Called once, before the first run
    /// moves anything, so repeated runs all start from the same heap.
    /// </summary>
    public void CaptureHome()
    {
        HomePosition = Rect.anchoredPosition;
        HomeSize = Rect.sizeDelta;
        HomeRotation = Rect.localRotation;
        HomeSiblingIndex = Rect.GetSiblingIndex();
    }

    public void GoHome()
    {
        Rect.anchoredPosition = HomePosition;
        Rect.sizeDelta = HomeSize;
        Rect.localRotation = HomeRotation;
        Rect.SetSiblingIndex(HomeSiblingIndex);
        IsFiled = false;
        FolderIndex = -1;
    }

    public void SetArmed(bool value)
    {
        armed = value;
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (!armed)
            return;

        if (PickedUp != null)
            PickedUp(this);

        MoveTo(e);
    }

    public void OnDrag(PointerEventData e)
    {
        if (!armed)
            return;

        MoveTo(e);
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (!armed)
            return;

        if (Dropped != null)
            Dropped(this);
    }

    private void MoveTo(PointerEventData e)
    {
        Vector2 local;
        if (!ToLocal(e, out local))
            return;

        Rect.anchoredPosition = local;
    }

    /// <summary>
    /// Screen point to a position inside the play area, so the paper sits under the finger
    /// whatever the canvas scale or screen size.
    /// </summary>
    private bool ToLocal(PointerEventData e, out Vector2 local)
    {
        local = Vector2.zero;
        if (dragSpace == null)
            return false;

        // A Screen Space - Overlay canvas takes a null camera here; anything else needs the
        // one actually rendering it, or the point comes back in the wrong space.
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera;

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            dragSpace, e.position, cam, out local);
    }
}

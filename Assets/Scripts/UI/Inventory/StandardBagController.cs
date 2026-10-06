using System.Collections;
using UnityEngine;

/// <summary>
/// The standard (orange) bag in the inventory panel. The BagPouchNavigator moves through its
/// five pockets: top, middle, bottom, left side and right side. Each opens with the bag's
/// Animator before its grid shows. Both side pockets open with the one animation, so moving
/// straight between them only swaps the grid.
/// </summary>
public class StandardBagController : MonoBehaviour, IBagPouches
{
    private const int TOP = 0, MIDDLE = 1, BOTTOM = 2, LEFT = 3, RIGHT = 4;

    private static readonly string[] PouchNames = { "Top Pocket", "Middle Pocket", "Bottom Pocket", "Left Pocket", "Right Pocket" };
    private static readonly string[] OpenStates = { "OpenTop", "OpenMiddle", "OpenBottom", "OpenSides", "OpenSides" };
    private static readonly string[] CloseStates = { "CloseTop", "CloseMiddle", "CloseBottom", "CloseSides", "CloseSides" };

    [SerializeField] private Animator bagAnimator;

    [Header("Pockets (where each is on the bag)")]
    [SerializeField] private RectTransform topArea;
    [SerializeField] private RectTransform middleArea;
    [SerializeField] private RectTransform bottomArea;
    [SerializeField] private RectTransform leftArea;
    [SerializeField] private RectTransform rightArea;

    [Header("Grids (outside the zoom, placed over their pocket when it opens)")]
    [SerializeField] private GameObject topPanel;
    [SerializeField] private GameObject middlePanel;
    [SerializeField] private GameObject bottomPanel;
    [SerializeField] private GameObject leftPanel;
    [SerializeField] private GameObject rightPanel;

    [Header("Audio")]
    [SerializeField] private AudioClip topAudio;
    [SerializeField] private AudioClip zipperAudio;

    // The pocket whose grid is showing, -1 for none
    private int openPouch = -1;

    /// <summary>True once a pocket has finished opening and its grid is showing.</summary>
    public bool IsOpen => openPouch >= 0;

    public int PouchCount => PouchNames.Length;

    public string GetPouchName(int pouch) => PouchNames[pouch];

    public RectTransform GetPouchArea(int pouch) => Area(pouch);

    public RectTransform GetPouchGrid(int pouch) => Panel(pouch) != null ? (RectTransform)Panel(pouch).transform : null;

    public IEnumerator OpenPouch(int pouch, int from)
    {
        if (!SharesOpening(pouch, from))
        {
            SoundManager.Sfx(pouch == TOP ? topAudio : zipperAudio);
            yield return PlayState(OpenStates[pouch]);
        }

        openPouch = pouch;
        if (Panel(pouch) != null)
            Panel(pouch).SetActive(true);
    }

    public IEnumerator ClosePouch(int pouch, int to)
    {
        openPouch = -1;
        if (Panel(pouch) != null)
            Panel(pouch).SetActive(false);
        InventoryItemDragHandler.ForceHideDescriptionPanel();

        if (SharesOpening(pouch, to))
            yield break;

        SoundManager.Sfx(pouch == TOP ? topAudio : zipperAudio);
        yield return PlayState(CloseStates[pouch]);
    }

    public void ResetClosed()
    {
        openPouch = -1;
        for (int i = 0; i < PouchCount; i++)
            if (Panel(i) != null)
                Panel(i).SetActive(false);

        // Back to the idle, closed bag
        if (bagAnimator != null && bagAnimator.isActiveAndEnabled)
        {
            bagAnimator.Rebind();
            bagAnimator.Update(0f);
        }
    }

    void OnDisable()
    {
        openPouch = -1;
    }

    private static bool SharesOpening(int a, int b)
    {
        return (a == LEFT || a == RIGHT) && (b == LEFT || b == RIGHT);
    }

    private IEnumerator PlayState(string state)
    {
        if (bagAnimator == null || !bagAnimator.isActiveAndEnabled)
            yield break;

        bagAnimator.Play(state);

        // Wait a frame for the state to start, then for it to finish
        yield return null;
        yield return new WaitForSeconds(bagAnimator.GetCurrentAnimatorStateInfo(0).length);
    }

    private RectTransform Area(int pouch)
    {
        switch (pouch)
        {
            case TOP: return topArea;
            case MIDDLE: return middleArea;
            case BOTTOM: return bottomArea;
            case LEFT: return leftArea;
            case RIGHT: return rightArea;
            default: return null;
        }
    }

    private GameObject Panel(int pouch)
    {
        switch (pouch)
        {
            case TOP: return topPanel;
            case MIDDLE: return middlePanel;
            case BOTTOM: return bottomPanel;
            case LEFT: return leftPanel;
            case RIGHT: return rightPanel;
            default: return null;
        }
    }
}

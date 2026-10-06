using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Medium Bag (yellow, shown to players as the Tactical Modular Duffel) in the inventory
/// panel. The BagPouchNavigator moves through its pouches:
///  - the top: a row of three single-item slots (a BagItemCarousel), shuffled on opening and
///    whenever a storage is opened, like the Small Bag
///  - the middle pocket: a normal grid over the bag's front
///  - the left and right side pockets: a normal grid each. Both open with the one animation, so
///    moving straight between them only swaps the grid.
/// Only one pouch is open at a time. The bag stays visible behind whatever is open.
/// </summary>
public class MediumBagController : MonoBehaviour, IBagPouches
{
    private const int TOP = 0, MIDDLE = 1, LEFT = 2, RIGHT = 3;

    private static readonly string[] PouchNames = { "Top Slots", "Middle Pocket", "Left Pocket", "Right Pocket" };

    [Header("Bag")]
    [SerializeField] private Image bagImage;
    [SerializeField] private float framesPerSecond = 24f;

    [Header("Pouches (where each is on the bag)")]
    [SerializeField] private RectTransform topArea;
    [SerializeField] private RectTransform middleArea;
    [SerializeField] private RectTransform leftSideArea;
    [SerializeField] private RectTransform rightSideArea;

    [Header("Animations (in order)")]
    [Tooltip("First frame is also the closed bag.")]
    [SerializeField] private Sprite[] sidesOpenFrames = new Sprite[] { };
    [SerializeField] private Sprite[] sidesCloseFrames = new Sprite[] { };
    [SerializeField] private Sprite[] middleOpenFrames = new Sprite[] { };
    [SerializeField] private Sprite[] middleCloseFrames = new Sprite[] { };
    [SerializeField] private Sprite[] topOpenFrames = new Sprite[] { };
    [SerializeField] private Sprite[] topCloseFrames = new Sprite[] { };

    [Header("Compartments (outside the zoom, placed over their pouch when it opens)")]
    [SerializeField] private GameObject leftPocketPanel;
    [SerializeField] private GameObject rightPocketPanel;
    [SerializeField] private GameObject middlePocketPanel;
    [Tooltip("Backing behind the three top slots.")]
    [SerializeField] private GameObject topPanel;
    [SerializeField] private BagItemCarousel topCarousel;
    [Tooltip("Holds the top's backing, slots and arrows, so they move over the top together.")]
    [SerializeField] private RectTransform topCompartment;

    [Header("Audio")]
    [SerializeField] private AudioClip zipperAudio;
    [SerializeField] private AudioClip topAudio;

    // The pouch whose compartment is showing, -1 for none
    private int openPouch = -1;

    /// <summary>True while one of the bag's compartments is open.</summary>
    public bool IsOpen => openPouch >= 0;

    public int PouchCount => PouchNames.Length;

    public string GetPouchName(int pouch) => PouchNames[pouch];

    void OnEnable()
    {
        // The inventory panel always shows the bag closed when it comes up
        ResetClosed();
    }

    void OnDisable()
    {
        openPouch = -1;
    }

    /// <summary>Snaps the bag shut with no animation.</summary>
    public void ResetClosed()
    {
        openPouch = -1;
        ShowCompartment(-1);

        if (bagImage != null && sidesOpenFrames.Length > 0)
            bagImage.sprite = sidesOpenFrames[0];
    }

    /// <summary>Called when a storage is opened next to the bag: an open top reshuffles.</summary>
    public void OnStorageOpened()
    {
        if (openPouch == TOP && topCarousel != null)
            topCarousel.Reshuffle();
    }

    public RectTransform GetPouchArea(int pouch)
    {
        switch (pouch)
        {
            case TOP: return topArea;
            case MIDDLE: return middleArea;
            case LEFT: return leftSideArea;
            case RIGHT: return rightSideArea;
            default: return null;
        }
    }

    public RectTransform GetPouchGrid(int pouch)
    {
        switch (pouch)
        {
            case TOP: return topCompartment;
            case MIDDLE: return middlePocketPanel != null ? (RectTransform)middlePocketPanel.transform : null;
            case LEFT: return leftPocketPanel != null ? (RectTransform)leftPocketPanel.transform : null;
            case RIGHT: return rightPocketPanel != null ? (RectTransform)rightPocketPanel.transform : null;
            default: return null;
        }
    }

    public IEnumerator OpenPouch(int pouch, int from)
    {
        if (!SharesOpening(pouch, from))
        {
            SoundManager.Sfx(pouch == TOP ? topAudio : zipperAudio);
            yield return PlayFrames(OpenFrames(pouch));
        }

        openPouch = pouch;
        ShowCompartment(pouch);
    }

    public IEnumerator ClosePouch(int pouch, int to)
    {
        openPouch = -1;
        ShowCompartment(-1);
        InventoryItemDragHandler.ForceHideDescriptionPanel();

        if (SharesOpening(pouch, to))
            yield break;

        SoundManager.Sfx(pouch == TOP ? topAudio : zipperAudio);
        yield return PlayFrames(CloseFrames(pouch));
    }

    private static bool SharesOpening(int a, int b)
    {
        return (a == LEFT || a == RIGHT) && (b == LEFT || b == RIGHT);
    }

    private Sprite[] OpenFrames(int pouch)
    {
        switch (pouch)
        {
            case LEFT:
            case RIGHT: return sidesOpenFrames;
            case MIDDLE: return middleOpenFrames;
            case TOP: return topOpenFrames;
            default: return new Sprite[0];
        }
    }

    private Sprite[] CloseFrames(int pouch)
    {
        switch (pouch)
        {
            case LEFT:
            case RIGHT: return sidesCloseFrames;
            case MIDDLE: return middleCloseFrames;
            case TOP: return topCloseFrames;
            default: return new Sprite[0];
        }
    }

    private void ShowCompartment(int pouch)
    {
        if (leftPocketPanel != null)
            leftPocketPanel.SetActive(pouch == LEFT);
        if (rightPocketPanel != null)
            rightPocketPanel.SetActive(pouch == RIGHT);
        if (middlePocketPanel != null)
            middlePocketPanel.SetActive(pouch == MIDDLE);
        if (topPanel != null)
            topPanel.SetActive(pouch == TOP);

        if (topCarousel != null)
        {
            if (pouch == TOP)
                topCarousel.Show(shuffle: true);
            else
                topCarousel.Hide();
        }
    }

    private IEnumerator PlayFrames(Sprite[] frames)
    {
        float frameTime = framesPerSecond > 0f ? 1f / framesPerSecond : 0f;

        foreach (Sprite frame in frames)
        {
            if (bagImage != null && frame != null)
                bagImage.sprite = frame;

            if (frameTime > 0f)
                yield return new WaitForSecondsRealtime(frameTime);
        }
    }
}

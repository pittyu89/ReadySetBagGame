using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Small Bag (blue, shown to players as the Roll-Top Waterproof Pack) in the inventory panel: a single slot that shows one packed item at a
/// time. This handles opening and closing the bag, its one pouch for the BagPouchNavigator; the
/// slot, arrows, shuffling and drag-and-drop are the BagItemCarousel's job. The contents are
/// shuffled every time the bag is opened and whenever a storage is opened next to it.
/// </summary>
public class SmallBagController : MonoBehaviour, IBagPouches
{
    [Header("Bag")]
    [SerializeField] private Image bagImage;
    [Tooltip("Closed bag to gray slot, in order.")]
    [SerializeField] private Sprite[] openFrames = new Sprite[] { };
    [Tooltip("Gray slot back to closed bag, in order.")]
    [SerializeField] private Sprite[] closeFrames = new Sprite[] { };
    [SerializeField] private float framesPerSecond = 24f;

    [Header("Contents")]
    [SerializeField] private BagItemCarousel carousel;
    [Tooltip("Where the slot is on the bag art: what the view zooms in on.")]
    [SerializeField] private RectTransform pouchArea;
    [Tooltip("Holds the slot and its arrows, outside the zoom, placed over the pouch when it opens.")]
    [SerializeField] private RectTransform compartment;

    [Header("Audio")]
    [SerializeField] private AudioClip openAudio;
    [SerializeField] private AudioClip closeAudio;

    private enum State { Closed, Opening, Open, Closing }
    private State state = State.Closed;

    /// <summary>True once the bag has finished opening and items can be dropped in.</summary>
    public bool IsOpen => state == State.Open;

    public int PouchCount => 1;

    public string GetPouchName(int pouch) => "Main Pocket";

    void OnEnable()
    {
        // The inventory panel always shows the bag closed when it comes up
        ResetClosed();
    }

    void OnDisable()
    {
        state = State.Closed;
    }

    /// <summary>Snaps the bag shut with no animation.</summary>
    public void ResetClosed()
    {
        state = State.Closed;

        if (carousel != null)
            carousel.Hide();

        if (bagImage != null && openFrames.Length > 0)
            bagImage.sprite = openFrames[0];
    }

    public RectTransform GetPouchArea(int pouch) => pouchArea;

    public RectTransform GetPouchGrid(int pouch) => compartment;

    public IEnumerator OpenPouch(int pouch, int from)
    {
        if (state != State.Closed)
            yield break;

        SoundManager.Sfx(openAudio);
        yield return PlayFrames(openFrames, State.Opening);

        state = State.Open;
        if (carousel != null)
            carousel.Show(shuffle: true);
    }

    public IEnumerator ClosePouch(int pouch, int to)
    {
        if (state != State.Open)
            yield break;

        SoundManager.Sfx(closeAudio);
        if (carousel != null)
            carousel.Hide();
        InventoryItemDragHandler.ForceHideDescriptionPanel();

        yield return PlayFrames(closeFrames, State.Closing);
        state = State.Closed;
    }

    /// <summary>
    /// Called when a storage is opened next to the bag. An open bag reshuffles, just like
    /// opening it does; a closed one is left alone since opening it will shuffle anyway.
    /// </summary>
    public void OnStorageOpened()
    {
        if (state == State.Open && carousel != null)
            carousel.Reshuffle();
    }

    private IEnumerator PlayFrames(Sprite[] frames, State playingState)
    {
        state = playingState;
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

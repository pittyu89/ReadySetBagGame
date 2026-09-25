using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Small Bag (blue) in the inventory panel: a single slot that shows one packed item at a
/// time. This handles opening and closing the bag; the slot, arrows, shuffling and drag-and-drop
/// are the BagItemCarousel's job. The contents are shuffled every time the bag is opened and
/// whenever a storage is opened next to it.
/// </summary>
public class SmallBagController : MonoBehaviour
{
    [Header("Bag")]
    [SerializeField] private Image bagImage;
    [SerializeField] private Button openButton;
    [Tooltip("Closed bag to gray slot, in order.")]
    [SerializeField] private Sprite[] openFrames = new Sprite[] { };
    [Tooltip("Gray slot back to closed bag, in order.")]
    [SerializeField] private Sprite[] closeFrames = new Sprite[] { };
    [SerializeField] private float framesPerSecond = 24f;

    [Header("Contents")]
    [SerializeField] private BagItemCarousel carousel;
    [SerializeField] private Button closeButton;

    [Header("Audio")]
    [SerializeField] private AudioClip openAudio;
    [SerializeField] private AudioClip closeAudio;

    private enum State { Closed, Opening, Open, Closing }
    private State state = State.Closed;
    private Coroutine animating;

    /// <summary>True once the bag has finished opening and items can be dropped in.</summary>
    public bool IsOpen => state == State.Open;

    void Awake()
    {
        if (openButton != null)
            openButton.onClick.AddListener(Open);

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
    }

    void OnEnable()
    {
        // The inventory panel always shows the bag closed when it comes up
        ResetClosed();
    }

    void OnDisable()
    {
        animating = null;
        state = State.Closed;
    }

    /// <summary>Snaps the bag shut with no animation.</summary>
    public void ResetClosed()
    {
        if (animating != null)
        {
            StopCoroutine(animating);
            animating = null;
        }

        state = State.Closed;

        if (carousel != null)
            carousel.Hide();
        if (closeButton != null)
            closeButton.gameObject.SetActive(false);

        if (bagImage != null && openFrames.Length > 0)
            bagImage.sprite = openFrames[0];

        if (openButton != null)
            openButton.interactable = true;
    }

    public void Open()
    {
        if (state != State.Closed || !isActiveAndEnabled)
            return;

        PlaySFX(openAudio);
        animating = StartCoroutine(PlayFrames(openFrames, State.Opening, () =>
        {
            state = State.Open;
            if (carousel != null)
                carousel.Show(shuffle: true);
            if (closeButton != null)
                closeButton.gameObject.SetActive(true);
        }));
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

    public void Close()
    {
        if (state != State.Open || !isActiveAndEnabled)
            return;

        PlaySFX(closeAudio);
        if (carousel != null)
            carousel.Hide();
        if (closeButton != null)
            closeButton.gameObject.SetActive(false);
        InventoryItemDragHandler.ForceHideDescriptionPanel();

        animating = StartCoroutine(PlayFrames(closeFrames, State.Closing, () =>
        {
            state = State.Closed;
            if (openButton != null)
                openButton.interactable = true;
        }));
    }

    private IEnumerator PlayFrames(Sprite[] frames, State playingState, System.Action onDone)
    {
        state = playingState;
        if (openButton != null)
            openButton.interactable = false;

        float frameTime = framesPerSecond > 0f ? 1f / framesPerSecond : 0f;

        foreach (Sprite frame in frames)
        {
            if (bagImage != null && frame != null)
                bagImage.sprite = frame;

            if (frameTime > 0f)
                yield return new WaitForSecondsRealtime(frameTime);
        }

        animating = null;
        onDone?.Invoke();
    }

    private static void PlaySFX(AudioClip clip)
    {
        SoundManager.Sfx(clip);
    }
}

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Medium Bag (yellow) in the inventory panel. Tapping a part of the bag opens it:
///  - the side pockets: two normal grids, one each side of the bag
///  - the middle pocket: a normal grid over the bag's front
///  - the top: a row of three single-item slots (a BagItemCarousel), shuffled on opening and
///    whenever a storage is opened, like the Small Bag
/// Only one part is open at a time. The bag stays visible behind whatever is open.
/// </summary>
public class MediumBagController : MonoBehaviour
{
    private enum Part { None, Sides, Middle, Top }

    [Header("Bag")]
    [SerializeField] private Image bagImage;
    [SerializeField] private float framesPerSecond = 24f;

    [Header("Tap Areas")]
    [SerializeField] private Button leftSideButton;
    [SerializeField] private Button rightSideButton;
    [SerializeField] private Button middleButton;
    [SerializeField] private Button topButton;
    [SerializeField] private Button closeButton;

    [Header("Animations (in order)")]
    [Tooltip("First frame is also the closed bag.")]
    [SerializeField] private Sprite[] sidesOpenFrames = new Sprite[] { };
    [SerializeField] private Sprite[] sidesCloseFrames = new Sprite[] { };
    [SerializeField] private Sprite[] middleOpenFrames = new Sprite[] { };
    [SerializeField] private Sprite[] middleCloseFrames = new Sprite[] { };
    [SerializeField] private Sprite[] topOpenFrames = new Sprite[] { };
    [SerializeField] private Sprite[] topCloseFrames = new Sprite[] { };

    [Header("Compartments")]
    [SerializeField] private GameObject leftPocketPanel;
    [SerializeField] private GameObject rightPocketPanel;
    [SerializeField] private GameObject middlePocketPanel;
    [Tooltip("Backing behind the three top slots.")]
    [SerializeField] private GameObject topPanel;
    [SerializeField] private BagItemCarousel topCarousel;

    [Header("Audio")]
    [SerializeField] private AudioClip zipperAudio;
    [SerializeField] private AudioClip topAudio;

    private Part openPart = Part.None;
    private bool animating;
    private Coroutine running;

    void Awake()
    {
        if (leftSideButton != null)
            leftSideButton.onClick.AddListener(() => Open(Part.Sides));
        if (rightSideButton != null)
            rightSideButton.onClick.AddListener(() => Open(Part.Sides));
        if (middleButton != null)
            middleButton.onClick.AddListener(() => Open(Part.Middle));
        if (topButton != null)
            topButton.onClick.AddListener(() => Open(Part.Top));
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
        running = null;
        animating = false;
        openPart = Part.None;
    }

    /// <summary>Snaps the bag shut with no animation.</summary>
    public void ResetClosed()
    {
        if (running != null)
        {
            StopCoroutine(running);
            running = null;
        }

        animating = false;
        openPart = Part.None;
        ShowCompartment(Part.None);

        if (bagImage != null && sidesOpenFrames.Length > 0)
            bagImage.sprite = sidesOpenFrames[0];

        SetTapAreasInteractable(true);
    }

    /// <summary>Called when a storage is opened next to the bag: an open top reshuffles.</summary>
    public void OnStorageOpened()
    {
        if (openPart == Part.Top && !animating && topCarousel != null)
            topCarousel.Reshuffle();
    }

    private void Open(Part part)
    {
        if (openPart != Part.None || animating || !isActiveAndEnabled)
            return;

        PlaySFX(part == Part.Top ? topAudio : zipperAudio);
        SetTapAreasInteractable(false);

        running = StartCoroutine(PlayFrames(OpenFrames(part), () =>
        {
            openPart = part;
            ShowCompartment(part);
        }));
    }

    public void Close()
    {
        if (openPart == Part.None || animating || !isActiveAndEnabled)
            return;

        Part closing = openPart;
        PlaySFX(closing == Part.Top ? topAudio : zipperAudio);
        ShowCompartment(Part.None);
        InventoryItemDragHandler.ForceHideDescriptionPanel();

        running = StartCoroutine(PlayFrames(CloseFrames(closing), () =>
        {
            openPart = Part.None;
            SetTapAreasInteractable(true);
        }));
    }

    private Sprite[] OpenFrames(Part part)
    {
        switch (part)
        {
            case Part.Sides: return sidesOpenFrames;
            case Part.Middle: return middleOpenFrames;
            case Part.Top: return topOpenFrames;
            default: return new Sprite[0];
        }
    }

    private Sprite[] CloseFrames(Part part)
    {
        switch (part)
        {
            case Part.Sides: return sidesCloseFrames;
            case Part.Middle: return middleCloseFrames;
            case Part.Top: return topCloseFrames;
            default: return new Sprite[0];
        }
    }

    private void ShowCompartment(Part part)
    {
        if (leftPocketPanel != null)
            leftPocketPanel.SetActive(part == Part.Sides);
        if (rightPocketPanel != null)
            rightPocketPanel.SetActive(part == Part.Sides);
        if (middlePocketPanel != null)
            middlePocketPanel.SetActive(part == Part.Middle);
        if (topPanel != null)
            topPanel.SetActive(part == Part.Top);

        if (topCarousel != null)
        {
            if (part == Part.Top)
                topCarousel.Show(shuffle: true);
            else
                topCarousel.Hide();
        }

        if (closeButton != null)
            closeButton.gameObject.SetActive(part != Part.None);
    }

    private void SetTapAreasInteractable(bool interactable)
    {
        foreach (Button b in new[] { leftSideButton, rightSideButton, middleButton, topButton })
            if (b != null)
                b.interactable = interactable;
    }

    private IEnumerator PlayFrames(Sprite[] frames, System.Action onDone)
    {
        animating = true;
        float frameTime = framesPerSecond > 0f ? 1f / framesPerSecond : 0f;

        foreach (Sprite frame in frames)
        {
            if (bagImage != null && frame != null)
                bagImage.sprite = frame;

            if (frameTime > 0f)
                yield return new WaitForSecondsRealtime(frameTime);
        }

        animating = false;
        running = null;
        onDone?.Invoke();
    }

    private static void PlaySFX(AudioClip clip)
    {
        SoundManager.Sfx(clip);
    }
}

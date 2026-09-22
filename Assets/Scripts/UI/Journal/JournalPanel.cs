using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The in-game Journal: a book of every supply item. Items start locked and unlock once
/// they were in the go bag at the end of a drill (see <see cref="JournalProgress"/>).
/// The book opens, flips and closes with the JournalSheet frames, and newly unlocked
/// containers break their chains one after another the first time they are shown.
/// Opening the Journal does not pause the timer.
/// </summary>
public class JournalPanel : MonoBehaviour
{
    private const int SLOTS_PER_PAGE = 12;

    [Header("Panel")]
    [SerializeField] private GameObject overlay;
    [SerializeField] private GameObject pageContent;
    [SerializeField] private GameObject openButtonGroup;
    [SerializeField] private Button openButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button previousButton;
    [SerializeField] private Button nextButton;

    [Header("Book Animation")]
    [SerializeField] private Image bookImage;
    [SerializeField] private Sprite[] openFrames;
    [SerializeField] private Sprite[] nextFrames;
    [SerializeField] private Sprite[] backFrames;
    [SerializeField] private Sprite[] closeFrames;
    [SerializeField] private float bookFrameDuration = 0.05f;

    [Header("Slots")]
    [SerializeField] private JournalSlot[] slots;
    [SerializeField] private Sprite lockedContainerSprite;
    [SerializeField] private Sprite unlockedContainerSprite;
    [SerializeField] private Sprite[] unlockFrames;
    [SerializeField] private float unlockFrameDuration = 0.05f;

    [Header("Selection")]
    [SerializeField] private Sprite[] selectionFrames;
    [SerializeField] private float selectionFrameDuration = 0.1f;

    [Header("Details")]
    [SerializeField] private Image detailContainerImage;
    [SerializeField] private Image detailIconImage;
    [SerializeField] private TextMeshProUGUI detailNameText;
    [SerializeField] private TextMeshProUGUI detailWeightText;
    [SerializeField] private TextMeshProUGUI detailImportanceText;
    [SerializeField] private TextMeshProUGUI detailDescriptionText;
    [SerializeField] private string lockedDescription = "Pack this item in your go bag and finish a drill to unlock it.";

    [Header("Audio")]
    [SerializeField] private AudioClip openSFX;
    [SerializeField] private AudioClip pageFlipSFX;
    [SerializeField] private AudioClip closeSFX;
    [SerializeField] private AudioClip unlockSFX;
    [SerializeField] private AudioClip selectSFX;

    private readonly List<SupplyItem> items = new List<SupplyItem>();
    private HashSet<string> unlocked = new HashSet<string>();
    private HashSet<string> seen = new HashSet<string>();
    private readonly HashSet<int> animatingSlots = new HashSet<int>();

    private int currentPage;
    private int selectedItemIndex = -1;
    private bool isBusy;
    private Coroutine selectionRoutine;

    private int PageCount => Mathf.Max(1, Mathf.CeilToInt(items.Count / (float)SLOTS_PER_PAGE));

    void Awake()
    {
        LoadItems();

        if (openButton != null) openButton.onClick.AddListener(Open);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (previousButton != null) previousButton.onClick.AddListener(PreviousPage);
        if (nextButton != null) nextButton.onClick.AddListener(NextPage);

        for (int i = 0; i < slots.Length; i++)
        {
            int slotIndex = i;
            if (slots[i] != null && slots[i].Button != null)
                slots[i].Button.onClick.AddListener(() => OnSlotClicked(slotIndex));
        }

        if (overlay != null)
            overlay.SetActive(false);

        if (openButtonGroup != null)
            openButtonGroup.SetActive(GoBagPickup.IsBagPickedUp());
    }

    void Update()
    {
        // The Journal only becomes available once the player has picked up the go bag
        if (openButtonGroup != null && !openButtonGroup.activeSelf && GoBagPickup.IsBagPickedUp())
            openButtonGroup.SetActive(true);
    }

    /// <summary>
    /// Every SupplyItem, most important first. Within a tier, the drill's essentials come
    /// first in the order the quiz teaches them, then the rest alphabetically.
    /// </summary>
    private void LoadItems()
    {
        items.Clear();
        foreach (SupplyItem supply in Resources.LoadAll<SupplyItem>("ItemData"))
        {
            if (supply != null && !string.IsNullOrEmpty(supply.ItemName))
                items.Add(supply);
        }

        items.Sort((a, b) =>
        {
            int byImportance = b.Importance.CompareTo(a.Importance);
            if (byImportance != 0)
                return byImportance;

            int byEssential = SortRank(a).CompareTo(SortRank(b));
            if (byEssential != 0)
                return byEssential;

            return string.Compare(a.ItemName, b.ItemName, System.StringComparison.OrdinalIgnoreCase);
        });
    }

    // Essentials in their ranked order, then everything else
    private static int SortRank(SupplyItem item) => item.IsEssential ? item.EssentialRank : int.MaxValue;

    public void Open()
    {
        if (isBusy || overlay == null || overlay.activeSelf || !GoBagPickup.IsBagPickedUp())
            return;

        unlocked = JournalProgress.GetUnlocked();
        seen = JournalProgress.GetSeen();

        // Start on the first page that has something new to reveal
        currentPage = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (IsNewUnlock(items[i]))
            {
                currentPage = i / SLOTS_PER_PAGE;
                break;
            }
        }

        overlay.SetActive(true);
        StartCoroutine(OpenRoutine());
    }

    public void Close()
    {
        if (isBusy || overlay == null || !overlay.activeSelf)
            return;

        StartCoroutine(CloseRoutine());
    }

    public void NextPage()
    {
        if (isBusy)
            return;

        StartCoroutine(FlipRoutine((currentPage + 1) % PageCount, nextFrames));
    }

    public void PreviousPage()
    {
        if (isBusy)
            return;

        StartCoroutine(FlipRoutine((currentPage - 1 + PageCount) % PageCount, backFrames));
    }

    private IEnumerator OpenRoutine()
    {
        isBusy = true;
        SetContentVisible(false);
        PlaySFX(openSFX);

        yield return PlayBookFrames(openFrames);

        ShowPage(currentPage);
        SetContentVisible(true);
        isBusy = false;

        yield return RevealNewUnlocks();
    }

    private IEnumerator CloseRoutine()
    {
        isBusy = true;
        StopAllUnlockAnimations();
        SetContentVisible(false);
        PlaySFX(closeSFX);

        yield return PlayBookFrames(closeFrames);

        overlay.SetActive(false);
        isBusy = false;
    }

    private IEnumerator FlipRoutine(int targetPage, Sprite[] frames)
    {
        isBusy = true;
        StopAllUnlockAnimations();
        SetContentVisible(false);
        PlaySFX(pageFlipSFX);

        yield return PlayBookFrames(frames);

        currentPage = targetPage;
        ShowPage(currentPage);
        SetContentVisible(true);
        isBusy = false;

        yield return RevealNewUnlocks();
    }

    private IEnumerator PlayBookFrames(Sprite[] frames)
    {
        if (bookImage == null || frames == null)
            yield break;

        foreach (Sprite frame in frames)
        {
            bookImage.sprite = frame;
            yield return new WaitForSecondsRealtime(bookFrameDuration);
        }
    }

    /// <summary>
    /// Breaks the chains on this page's new unlocks, one container after another.
    /// </summary>
    private IEnumerator RevealNewUnlocks()
    {
        int page = currentPage;

        for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
        {
            // Stop if the page was flipped or the book closed mid-reveal
            if (page != currentPage || !pageContent.activeSelf)
                yield break;

            int itemIndex = page * SLOTS_PER_PAGE + slotIndex;
            if (itemIndex >= items.Count || !IsNewUnlock(items[itemIndex]))
                continue;

            yield return PlayUnlock(slotIndex, items[itemIndex]);
        }
    }

    private IEnumerator PlayUnlock(int slotIndex, SupplyItem item)
    {
        JournalSlot slot = slots[slotIndex];
        animatingSlots.Add(slotIndex);
        PlaySFX(unlockSFX);

        if (unlockFrames != null)
        {
            foreach (Sprite frame in unlockFrames)
            {
                if (!pageContent.activeSelf)
                    yield break;

                slot.ContainerImage.sprite = frame;
                yield return new WaitForSecondsRealtime(unlockFrameDuration);
            }
        }

        seen.Add(item.ItemName);
        JournalProgress.MarkSeen(item.ItemName);
        animatingSlots.Remove(slotIndex);

        FillSlot(slotIndex);
        if (selectedItemIndex == currentPage * SLOTS_PER_PAGE + slotIndex)
            ShowDetails(selectedItemIndex);
    }

    private void StopAllUnlockAnimations()
    {
        // The reveal coroutines exit on their own once the content is hidden; forget their
        // slots so the next page draws cleanly. Unfinished reveals replay next time.
        animatingSlots.Clear();
    }

    private void ShowPage(int page)
    {
        for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            FillSlot(slotIndex);

        // Keep the selection on this page, preferring the first unlocked item
        int firstOnPage = page * SLOTS_PER_PAGE;
        int selection = firstOnPage;
        for (int i = firstOnPage; i < Mathf.Min(firstOnPage + SLOTS_PER_PAGE, items.Count); i++)
        {
            if (IsRevealed(items[i]))
            {
                selection = i;
                break;
            }
        }

        Select(selection);
    }

    private void FillSlot(int slotIndex)
    {
        JournalSlot slot = slots[slotIndex];
        if (slot == null)
            return;

        int itemIndex = currentPage * SLOTS_PER_PAGE + slotIndex;
        bool hasItem = itemIndex < items.Count;
        slot.gameObject.SetActive(hasItem);
        if (!hasItem)
            return;

        SupplyItem item = items[itemIndex];
        bool revealed = IsRevealed(item);

        slot.ContainerImage.sprite = revealed ? unlockedContainerSprite : lockedContainerSprite;
        slot.IconImage.sprite = item.ItemImage;
        slot.IconImage.enabled = revealed && item.ItemImage != null;
    }

    private void OnSlotClicked(int slotIndex)
    {
        if (isBusy)
            return;

        int itemIndex = currentPage * SLOTS_PER_PAGE + slotIndex;
        if (itemIndex >= items.Count || itemIndex == selectedItemIndex)
            return;

        PlaySFX(selectSFX);
        Select(itemIndex);
    }

    private void Select(int itemIndex)
    {
        selectedItemIndex = itemIndex;

        for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
        {
            if (slots[slotIndex] != null && slots[slotIndex].SelectionImage != null)
                slots[slotIndex].SelectionImage.gameObject.SetActive(currentPage * SLOTS_PER_PAGE + slotIndex == itemIndex);
        }

        if (selectionRoutine != null)
            StopCoroutine(selectionRoutine);

        int selectedSlot = itemIndex - currentPage * SLOTS_PER_PAGE;
        if (selectedSlot >= 0 && selectedSlot < slots.Length && slots[selectedSlot].SelectionImage != null)
            selectionRoutine = StartCoroutine(PlaySelection(slots[selectedSlot].SelectionImage));

        ShowDetails(itemIndex);
    }

    /// <summary>
    /// Loops the Clicked_state frames on the selected container.
    /// </summary>
    private IEnumerator PlaySelection(Image selection)
    {
        if (selectionFrames == null || selectionFrames.Length == 0)
            yield break;

        int frame = 0;
        while (true)
        {
            selection.sprite = selectionFrames[frame];
            frame = (frame + 1) % selectionFrames.Length;
            yield return new WaitForSecondsRealtime(selectionFrameDuration);
        }
    }

    private void ShowDetails(int itemIndex)
    {
        if (itemIndex < 0 || itemIndex >= items.Count)
            return;

        SupplyItem item = items[itemIndex];
        bool revealed = IsRevealed(item);

        if (detailContainerImage != null)
            detailContainerImage.sprite = revealed ? unlockedContainerSprite : lockedContainerSprite;

        if (detailIconImage != null)
        {
            detailIconImage.sprite = item.ItemImage;
            detailIconImage.enabled = revealed && item.ItemImage != null;
        }

        if (detailNameText != null)
            detailNameText.text = revealed ? item.ItemName : "???";

        if (detailWeightText != null)
            detailWeightText.text = revealed ? $"Weight: {item.WeightKg:0.##}kg" : "Weight: ???";

        if (detailImportanceText != null)
        {
            detailImportanceText.text = revealed ? $"Importance: {item.Importance}" : "Importance: ???";
            detailImportanceText.color = revealed ? ImportanceColor(item.Importance) : new Color(0.45f, 0.45f, 0.45f);
        }

        if (detailDescriptionText != null)
            detailDescriptionText.text = revealed ? item.Description : lockedDescription;
    }

    private static Color ImportanceColor(ItemImportance importance)
    {
        switch (importance)
        {
            case ItemImportance.Critical: return new Color(0.89f, 0.22f, 0.22f);
            case ItemImportance.Important: return new Color(0.91f, 0.50f, 0.13f);
            case ItemImportance.Useful: return new Color(0.30f, 0.62f, 0.25f);
            case ItemImportance.Conditional: return new Color(0.25f, 0.47f, 0.80f);
            default: return new Color(0.45f, 0.45f, 0.45f);
        }
    }

    private bool IsNewUnlock(SupplyItem item) => unlocked.Contains(item.ItemName) && !seen.Contains(item.ItemName);

    // Unlocked and already past its chain-breaking animation
    private bool IsRevealed(SupplyItem item)
    {
        return unlocked.Contains(item.ItemName) && seen.Contains(item.ItemName);
    }

    private void SetContentVisible(bool visible)
    {
        if (pageContent != null)
            pageContent.SetActive(visible);

        if (closeButton != null)
            closeButton.interactable = visible;
    }

    private void PlaySFX(AudioClip clip)
    {
        SoundManager.Sfx(clip);
    }
}

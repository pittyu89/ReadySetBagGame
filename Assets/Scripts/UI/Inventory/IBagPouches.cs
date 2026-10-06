using System.Collections;
using UnityEngine;

/// <summary>
/// A go-bag whose pouches the BagPouchNavigator steps through. Pouches are numbered in the order
/// the arrows visit them. Opening and closing are coroutines so the navigator can pan between
/// a pouch's close animation and the next one's open animation.
/// </summary>
public interface IBagPouches
{
    int PouchCount { get; }

    string GetPouchName(int pouch);

    /// <summary>Where the pouch is on the bag art: what the view zooms in on.</summary>
    RectTransform GetPouchArea(int pouch);

    /// <summary>
    /// The pouch's grid (or slots), kept out of the zoom so it shows at its normal size. The
    /// navigator places it over the pouch before it opens.
    /// </summary>
    RectTransform GetPouchGrid(int pouch);

    /// <summary>
    /// Opens the pouch and shows its grid. <paramref name="from"/> is the pouch just closed, or -1:
    /// pouches that share an opening (the side pockets) skip the animation when coming from each other.
    /// </summary>
    IEnumerator OpenPouch(int pouch, int from);

    /// <summary>Hides the pouch's grid and closes it, unless <paramref name="to"/> shares its opening.</summary>
    IEnumerator ClosePouch(int pouch, int to);

    /// <summary>Snaps every pouch shut with no animation.</summary>
    void ResetClosed();
}

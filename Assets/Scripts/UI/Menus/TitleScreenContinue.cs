using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Title screen tap: hands the bag logo to <see cref="MenuTransition"/> so the scene change
/// zooms through it, then lets the scene navigator pick Login or Main.
/// Hook the full-screen ContinueButton's OnClick to <see cref="Continue"/>.
/// </summary>
public class TitleScreenContinue : MonoBehaviour
{
    [SerializeField] private RectTransform bagLogo;
    [Tooltip("Drawn on the bag (e.g. the exclamation mark) - zoomed along with it. Must share its parent.")]
    [SerializeField] private RectTransform[] logoOverlays;
    [Tooltip("Faded out as the zoom starts, e.g. the title text and the press-any prompt.")]
    [SerializeField] private Graphic[] fadeOut;

    private bool continuing;

    public void Continue()
    {
        if (continuing || SceneNavigationManager.Instance == null)
            return;

        continuing = true;
        MenuTransition.ZoomFromNext(bagLogo, logoOverlays, fadeOut);
        SceneNavigationManager.Instance.NavigateToGameScene();
    }
}

using UnityEngine;

/// <summary>
/// Title screen tap: lets the scene navigator pick Login or Main, and cuts straight to it -
/// the title screen has no transition of its own.
/// Hook the full-screen ContinueButton's OnClick to <see cref="Continue"/>.
/// </summary>
public class TitleScreenContinue : MonoBehaviour
{
    private bool continuing;

    public void Continue()
    {
        if (continuing || SceneNavigationManager.Instance == null)
            return;

        continuing = true;
        SceneNavigationManager.Instance.NavigateToGameScene(transition: false);
    }
}

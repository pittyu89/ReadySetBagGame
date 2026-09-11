using UnityEngine;

public class FrameRateManager : MonoBehaviour
{
    private static FrameRateManager instance;

    private void Awake()
    {
        // This object survives scene loads, so a second copy arriving with a newly loaded scene
        // would stack up permanently - one more DontDestroyOnLoad object per scene change, each
        // re-applying the same settings. The first one wins and later arrivals delete themselves.
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;

        // On Android - this project's build target - QualitySettings.vSyncCount is ignored and
        // this is what actually governs the frame cap. On a desktop build the quality tiers set
        // vSyncCount (Performant 0, Balanced/High Fidelity 1), and where that is non-zero it
        // takes precedence and this cap has no effect.
        Application.targetFrameRate = 60;

        DontDestroyOnLoad(gameObject);
    }
}

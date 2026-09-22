using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The OBJECTIVE card every minigame shows in its top corner.
///
/// It only owns the marking off: the line of text is set by the minigame itself, the way it
/// always was. The diamond sits plain and turns the done colour the moment the minigame is
/// finished — which is to say the moment its COMPLETED banner goes up, so a minigame cut
/// short by the timer never gets marked.
///
/// The done state is a colour on the authored diamond rather than a second sprite, so the
/// card needs nothing beyond the art it was given.
///
/// Watching the banner rather than being told keeps this out of all twenty minigames: they
/// already raise that banner on their own, and none of them had to learn about this card.
/// </summary>
public class MinigameObjective : MonoBehaviour
{
    [Header("Diamond")]
    [SerializeField] private Image diamond;
    [Tooltip("The diamond's colour while the objective is still open.")]
    [SerializeField] private Color openColor = Color.white;
    [Tooltip("What it turns once the minigame is finished.")]
    [SerializeField] private Color doneColor = new Color(150f / 255f, 176f / 255f, 0f, 1f);

    [Header("Completion")]
    [Tooltip("The minigame's COMPLETED banner. The diamond is checked while this is on, " +
             "so it follows whatever the minigame already counts as finishing.")]
    [SerializeField] private GameObject completedBanner;

    [Header("Tick")]
    [Tooltip("How far the diamond swells when it is checked, before settling back.")]
    [SerializeField] private float popScale = 1.35f;
    [SerializeField] private float popDuration = 0.35f;

    private bool isChecked = false;
    private Coroutine popRoutine;

    /// <summary>
    /// True once the minigame has been seen through — the same moment the diamond is ticked,
    /// which is the moment its COMPLETED banner goes up.
    ///
    /// The quiz reads this to know the player is finished, which is not the same as the
    /// minigame being over: a minigame keeps running for a few seconds after the last piece
    /// is placed, to show the banner and fade its panel away. Those seconds should not be
    /// counted against the clock, so the countdown stops here rather than when Play returns.
    /// </summary>
    public bool IsComplete
    {
        get { return isChecked; }
    }

    /// <summary>
    /// The minigame's own COMPLETED banner. It no longer draws anything — the shared
    /// <see cref="MinigameResultBanner"/> is shown in its place — but it still marks the
    /// moment of finishing, and its panel is what the shared banner fades out with.
    /// </summary>
    public GameObject CompletedBanner
    {
        get { return completedBanner; }
    }

    private void OnEnable()
    {
        SetChecked(false);
    }

    private void Update()
    {
        if (completedBanner == null || isChecked)
            return;

        if (completedBanner.activeInHierarchy)
            SetChecked(true);
    }

    /// <summary>
    /// Ticks or clears the diamond. Public so a minigame can call it directly if it ever
    /// wants to, rather than waiting on its banner.
    /// </summary>
    public void SetChecked(bool value)
    {
        isChecked = value;

        if (diamond != null)
        {
            diamond.color = value ? doneColor : openColor;
            diamond.rectTransform.localScale = Vector3.one;
        }

        if (popRoutine != null)
        {
            StopCoroutine(popRoutine);
            popRoutine = null;
        }

        // Only the tick is worth a flourish; clearing happens off screen between runs
        if (value && isActiveAndEnabled && diamond != null && popDuration > 0f)
            popRoutine = StartCoroutine(Pop());
    }

    private IEnumerator Pop()
    {
        RectTransform rt = diamond.rectTransform;

        for (float t = 0f; t < popDuration; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.Clamp01(t / popDuration);

            // Out fast, back slowly, so the check lands rather than wobbles
            float s = k < 0.35f
                ? Mathf.Lerp(1f, popScale, k / 0.35f)
                : Mathf.Lerp(popScale, 1f, (k - 0.35f) / 0.65f);

            rt.localScale = new Vector3(s, s, 1f);
            yield return null;
        }

        rt.localScale = Vector3.one;
        popRoutine = null;
    }
}

using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The round clock's warnings, drawn as a glow that creeps in from the edges of the screen and
/// leaves the middle - where the player is looking - clear.
///
/// Half time gets one amber heartbeat: a heads-up, not an alarm. The last tenth of the round
/// gets a red heartbeat that keeps going, quickening and strengthening as the clock runs down,
/// so the urgency builds instead of strobing at one pace from the start.
///
/// The heartbeats run on scaled time; the fade when it stops runs on real time, so it still
/// clears off the screen under the pause menu.
/// </summary>
public class ScreenPulseEffect : MonoBehaviour
{
    [Tooltip("Full-screen image the glow is drawn on. Its sprite is made at runtime.")]
    [SerializeField] private Image screenOverlay;

    [Header("Glow")]
    [Tooltip("How far the glow reaches in from the edges, as a share of the screen's height.")]
    [Range(0.05f, 0.5f)]
    [SerializeField] private float edgeReach = 0.22f;

    [Header("Half Time")]
    [SerializeField] private Color halfTimeColor = new Color(1f, 0.62f, 0.12f);
    [Tooltip("Length of the single amber heartbeat.")]
    [SerializeField] private float halfTimeDuration = 1.4f;
    [Range(0f, 1f)]
    [SerializeField] private float halfTimeStrength = 0.45f;

    [Header("Running Out")]
    [SerializeField] private Color runningOutColor = new Color(0.92f, 0.1f, 0.06f);
    [Tooltip("Seconds between heartbeats as the warning starts, and as the clock reaches zero.")]
    [SerializeField] private float slowestBeat = 1.3f;
    [SerializeField] private float fastestBeat = 0.55f;
    [Tooltip("How strong a heartbeat is as the warning starts, and as the clock reaches zero.")]
    [Range(0f, 1f)]
    [SerializeField] private float weakestStrength = 0.35f;
    [Range(0f, 1f)]
    [SerializeField] private float strongestStrength = 0.65f;
    [Tooltip("Share of the round left when the running-out warning starts; GameTimer starts it at a tenth.")]
    [SerializeField] private float runningOutShare = 0.1f;

    [Header("Fade")]
    [SerializeField] private float stopFadeDuration = 0.3f;

    // Texture height the glow is drawn at; the width follows the screen's shape
    private const int GLOW_HEIGHT = 128;

    private enum State { Idle, HalfTime, RunningOut, Stopping }

    private State state = State.Idle;
    private float elapsed;
    private float beatPhase;
    private float stopFrom;
    private Texture2D glowTexture;
    private GameTimer timer;

    private void Start()
    {
        if (screenOverlay == null)
            screenOverlay = GetComponent<Image>();

        if (screenOverlay == null)
            return;

        screenOverlay.raycastTarget = false;
        screenOverlay.type = Image.Type.Simple;
        screenOverlay.preserveAspect = false;
        screenOverlay.sprite = CreateGlowSprite();
        SetGlow(Color.clear, 0f);

        timer = FindFirstObjectByType<GameTimer>();
    }

    /// <summary>Half the round is gone: one amber heartbeat.</summary>
    public void PulseOrange()
    {
        if (screenOverlay == null || state == State.RunningOut)
            return;

        state = State.HalfTime;
        elapsed = 0f;
    }

    /// <summary>The round is nearly over: a red heartbeat that keeps going until stopped.</summary>
    public void StartRedPulse()
    {
        if (screenOverlay == null || state == State.RunningOut)
            return;

        state = State.RunningOut;
        beatPhase = 0f;
    }

    /// <summary>Fades the glow out from wherever it is.</summary>
    public void StopRedPulse()
    {
        if (screenOverlay == null || state == State.Idle)
            return;

        stopFrom = screenOverlay.color.a;
        state = State.Stopping;
        elapsed = 0f;

        // Nothing would run the fade on an inactive object; just clear it
        if (!isActiveAndEnabled)
        {
            SetGlow(Color.clear, 0f);
            state = State.Idle;
        }
    }

    private void Update()
    {
        if (screenOverlay == null)
            return;

        float dt = Time.deltaTime;

        switch (state)
        {
            case State.HalfTime:
                elapsed += dt;
                float t = elapsed / Mathf.Max(0.01f, halfTimeDuration);
                SetGlow(halfTimeColor, halfTimeStrength * Heartbeat(t));
                if (t >= 1f)
                {
                    SetGlow(halfTimeColor, 0f);
                    state = State.Idle;
                }
                break;

            case State.RunningOut:
                // 0 as the warning starts, 1 as the clock reaches zero
                float urgency = Urgency();
                float period = Mathf.Lerp(slowestBeat, fastestBeat, urgency);
                beatPhase = (beatPhase + dt / Mathf.Max(0.05f, period)) % 1f;
                float strength = Mathf.Lerp(weakestStrength, strongestStrength, urgency);
                SetGlow(runningOutColor, strength * Heartbeat(beatPhase));
                break;

            case State.Stopping:
                // Real time: the pause menu stops the clock at timeScale 0, and the glow
                // must still clear off it
                elapsed += Time.unscaledDeltaTime;
                float k = stopFadeDuration > 0f ? Mathf.Clamp01(elapsed / stopFadeDuration) : 1f;
                SetGlow(screenOverlay.color, Mathf.Lerp(stopFrom, 0f, k));
                if (k >= 1f)
                    state = State.Idle;
                break;
        }
    }

    private float Urgency()
    {
        if (timer == null)
            return 0f;

        float window = timer.GetTotalTime() * runningOutShare;
        if (window <= 0f)
            return 1f;

        return 1f - Mathf.Clamp01(timer.GetTimeRemaining() / window);
    }

    /// <summary>
    /// One lub-dub over <paramref name="t"/> 0..1: a strong beat, a softer one just after,
    /// then rest. Each beat rises fast and eases off slowly, like a pulse.
    /// </summary>
    private static float Heartbeat(float t)
    {
        return Mathf.Max(Beat(t, 0.06f, 0.22f), 0.65f * Beat(t, 0.3f, 0.26f));
    }

    private static float Beat(float t, float start, float length)
    {
        float u = (t - start) / length;
        if (u <= 0f || u >= 1f)
            return 0f;

        // Quick attack over the first fifth, smooth decay over the rest
        return u < 0.2f ? Mathf.SmoothStep(0f, 1f, u / 0.2f) : Mathf.SmoothStep(1f, 0f, (u - 0.2f) / 0.8f);
    }

    private void SetGlow(Color color, float alpha)
    {
        color.a = Mathf.Clamp01(alpha);
        screenOverlay.color = color;
    }

    /// <summary>
    /// White, with alpha that is full at the screen's edges and falls away to nothing
    /// <see cref="edgeReach"/> in, rounding off at the corners so they glow strongest.
    /// </summary>
    private Sprite CreateGlowSprite()
    {
        float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
        int height = GLOW_HEIGHT;
        int width = Mathf.Clamp(Mathf.RoundToInt(height * aspect), height, height * 4);

        glowTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "ScreenPulseGlow",
        };

        float reach = edgeReach * height;
        var pixels = new Color32[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // How far inside the band around the edges this pixel is, per axis
                float dx = Mathf.Max(0f, reach - Mathf.Min(x + 0.5f, width - x - 0.5f));
                float dy = Mathf.Max(0f, reach - Mathf.Min(y + 0.5f, height - y - 0.5f));
                float g = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / reach);

                // Eased so it fades out softly rather than ending on a visible line
                float a = g * g * (3f - 2f * g);
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * a * 255f));
            }
        }

        glowTexture.SetPixels32(pixels);
        glowTexture.Apply(false, true);

        return Sprite.Create(glowTexture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f,
                             0, SpriteMeshType.FullRect);
    }

    private void OnDestroy()
    {
        if (glowTexture != null)
            Destroy(glowTexture);
    }
}

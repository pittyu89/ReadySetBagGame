using UnityEngine;

/// <summary>
/// Short vibrations for the moments that matter: an item landing in the bag, a minigame won,
/// a quiz verdict, the last seconds on the clock. Android only - elsewhere every call does
/// nothing. The player can switch them off from Options (<see cref="Enabled"/>).
///
/// Handheld.Vibrate is a fixed half-second buzz, far too long for a tap, so this goes to the
/// phone's Vibrator directly: the system's tuned click effects where the phone has them
/// (Android 10+), otherwise a short pulse of the given strength.
/// </summary>
public static class Haptics
{
    private const string ENABLED_PREF = "HapticsEnabled";

    // android.os.VibrationEffect predefined effects
    private const int EFFECT_CLICK = 0;
    private const int EFFECT_DOUBLE_CLICK = 1;
    private const int EFFECT_TICK = 2;
    private const int EFFECT_HEAVY_CLICK = 5;

    /// <summary>Whether vibrations play. On by default; saved per device.</summary>
    public static bool Enabled
    {
        get => PlayerPrefs.GetInt(ENABLED_PREF, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(ENABLED_PREF, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    /// <summary>The lightest touch: a countdown second ticking over.</summary>
    public static void Tick() => Play(EFFECT_TICK, 12, 60);

    /// <summary>Something set in place: an item dropped into the bag or a storage space.</summary>
    public static void Tap() => Play(EFFECT_CLICK, 20, 120);

    /// <summary>A win: a minigame completed, a quiz answer right, the go bag picked up.</summary>
    public static void Success() => Play(EFFECT_HEAVY_CLICK, 35, 200);

    /// <summary>A miss or a refusal: a wrong answer, time's up, the bag too full or too heavy.</summary>
    public static void Fail() => PlayPattern(EFFECT_DOUBLE_CLICK, new long[] { 0, 30, 70, 30 }, new[] { 0, 180, 0, 180 });

#if UNITY_ANDROID && !UNITY_EDITOR
    private static AndroidJavaObject vibrator;
    private static int sdk;
    private static bool resolved;
    private static bool failed;

    private static bool Resolve()
    {
        if (resolved)
            return !failed;

        resolved = true;
        try
        {
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                sdk = version.GetStatic<int>("SDK_INT");

            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");

            failed = vibrator == null || !vibrator.Call<bool>("hasVibrator");
        }
        catch (System.Exception)
        {
            failed = true;
        }

        return !failed;
    }

    private static void Play(int effect, long milliseconds, int amplitude)
    {
        if (!Enabled)
            return;

        if (!Resolve())
        {
            // Can't reach the Vibrator: the stock call is better than nothing for a win or a miss.
            // It is also what has Unity add the VIBRATE permission to the build.
            if (effect != EFFECT_TICK && effect != EFFECT_CLICK)
                Handheld.Vibrate();
            return;
        }

        try
        {
            if (sdk >= 29)
            {
                using (var effects = new AndroidJavaClass("android.os.VibrationEffect"))
                using (var e = effects.CallStatic<AndroidJavaObject>("createPredefined", effect))
                    vibrator.Call("vibrate", e);
            }
            else if (sdk >= 26)
            {
                using (var effects = new AndroidJavaClass("android.os.VibrationEffect"))
                using (var e = effects.CallStatic<AndroidJavaObject>("createOneShot", milliseconds, amplitude))
                    vibrator.Call("vibrate", e);
            }
            else
            {
                vibrator.Call("vibrate", milliseconds);
            }
        }
        catch (System.Exception)
        {
            failed = true;
        }
    }

    private static void PlayPattern(int effect, long[] timings, int[] amplitudes)
    {
        if (!Enabled)
            return;

        if (!Resolve())
        {
            Handheld.Vibrate();
            return;
        }

        try
        {
            if (sdk >= 29)
            {
                Play(effect, 0, 0);
            }
            else if (sdk >= 26)
            {
                using (var effects = new AndroidJavaClass("android.os.VibrationEffect"))
                using (var e = effects.CallStatic<AndroidJavaObject>("createWaveform", timings, amplitudes, -1))
                    vibrator.Call("vibrate", e);
            }
            else
            {
                vibrator.Call("vibrate", timings, -1);
            }
        }
        catch (System.Exception)
        {
            failed = true;
        }
    }
#else
    private static void Play(int effect, long milliseconds, int amplitude) { }

    private static void PlayPattern(int effect, long[] timings, int[] amplitudes) { }
#endif
}

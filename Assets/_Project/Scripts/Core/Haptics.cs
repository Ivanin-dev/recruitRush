using UnityEngine;

namespace RecruitRush
{
    /// <summary>Short vibration pulses on Android (VibrationEffect, API 26+). No-op elsewhere.</summary>
    public static class Haptics
    {
        public static bool Enabled { get; set; } = true;
        static float last;

        public static void Light() => Pulse(12, 70, 0.08f);
        public static void Medium() => Pulse(28, 150, 0.1f);
        public static void Heavy() => Pulse(60, 255, 0.15f);

        public static void Pulse(int milliseconds, int amplitude, float minInterval)
        {
            if (!Enabled || Time.unscaledTime - last < minInterval) return;
            last = Time.unscaledTime;
#if UNITY_ANDROID && !UNITY_EDITOR
            Android.Vibrate(milliseconds, amplitude);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static class Android
        {
            static AndroidJavaObject vibrator;
            static bool initialized;

            public static void Vibrate(int ms, int amplitude)
            {
                try
                {
                    if (!initialized)
                    {
                        initialized = true;
                        using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                        var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                        vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                    }
                    if (vibrator == null) return;
                    using var effects = new AndroidJavaClass("android.os.VibrationEffect");
                    using var effect = effects.CallStatic<AndroidJavaObject>("createOneShot", (long)ms, Mathf.Clamp(amplitude, 1, 255));
                    vibrator.Call("vibrate", effect);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[Haptics] disabled: " + e.Message);
                    vibrator = null;
                }
            }

            // Never called: referencing Handheld.Vibrate makes Unity add the VIBRATE permission to the manifest.
            static void RequestPermission() => Handheld.Vibrate();
        }
#endif
    }
}

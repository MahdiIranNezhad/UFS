#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;
#endif

namespace UFS
{
    public static class UnityForegroundService
    {
#if UNITY_ANDROID && !UNITY_EDITOR

        private static readonly AndroidJavaObject activity;
        private static bool isStarted;

        static UnityForegroundService()
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            isStarted = false;
        }

        public static void Start()
        {
            if (isStarted) return;

            using var svcClass = new AndroidJavaClass("com.unity.extension.UnityForegroundService");
            using var intent = new AndroidJavaObject("android.content.Intent", activity, svcClass);

            int sdk = new AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT");

            if (sdk >= 26)
                activity.Call<AndroidJavaObject>("startForegroundService", intent);
            else
                activity.Call<AndroidJavaObject>("startService", intent);

            isStarted = true;
        }

        public static void Stop()
        {
            using var svcClass = new AndroidJavaClass("com.unity.extension.UnityForegroundService");
            using var intent = new AndroidJavaObject("android.content.Intent", activity, svcClass);

            activity.Call<AndroidJavaObject>("stopService", intent);
            isStarted = false;
        }

#else

        public static void Start()
        { }

        public static void Stop()
        { }

#endif
    }
}
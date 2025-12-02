#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;
#endif

namespace UFS
{
    public static class UnityForegroundService
    {
#if UNITY_ANDROID && !UNITY_EDITOR

        private static readonly AndroidJavaObject activity;
        private static readonly AndroidJavaClass serviceClass;

        private static readonly string ACTION_UPDATE;
        private static readonly string ACTION_STOP;
        private static readonly string EXTRA_TITLE;
        private static readonly string EXTRA_MESSAGE;

        private static bool isStarted;

        static UnityForegroundService()
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

            serviceClass = new AndroidJavaClass("com.unity.extension.UnityForegroundService");

            ACTION_UPDATE = serviceClass.GetStatic<string>("ACTION_UPDATE");
            ACTION_STOP   = serviceClass.GetStatic<string>("ACTION_STOP");
            EXTRA_TITLE   = serviceClass.GetStatic<string>("EXTRA_TITLE");
            EXTRA_MESSAGE = serviceClass.GetStatic<string>("EXTRA_MESSAGE");

            isStarted = false;
        }

        public static void Start()
        {
            if (isStarted) return;

            using var intent = new AndroidJavaObject("android.content.Intent", activity, serviceClass);

            int sdk = new AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT");

            if (sdk >= 26)
                activity.Call<AndroidJavaObject>("startForegroundService", intent);
            else
                activity.Call<AndroidJavaObject>("startService", intent);

            isStarted = true;
        }

        public static void Stop()
        {
            using var intent = new AndroidJavaObject("android.content.Intent", activity, serviceClass);
            intent.Call<AndroidJavaObject>("setAction", ACTION_STOP);
            activity.Call<AndroidJavaObject>("startService", intent);

            isStarted = false;
        }

        public static void Update(string title, string message)
        {
            if (!isStarted)
                return;

            using var intent = new AndroidJavaObject("android.content.Intent", activity, serviceClass);

            intent.Call<AndroidJavaObject>("setAction", ACTION_UPDATE);
            intent.Call<AndroidJavaObject>("putExtra", EXTRA_TITLE,  title);
            intent.Call<AndroidJavaObject>("putExtra", EXTRA_MESSAGE, message);

            activity.Call<AndroidJavaObject>("startService", intent);
        }

#else

        public static void Start() { }
        public static void Stop() { }
        public static void Update(string title, string message) { }

#endif
    }
}
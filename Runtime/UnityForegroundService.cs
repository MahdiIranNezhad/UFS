#if UNITY_ANDROID && !UNITY_EDITOR
using Cysharp.Threading.Tasks;
using System;
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

        public static bool IsRunning { get; private set; } = false;

        private const int MaxKarmaTries = 6;
        private const int KarmaDelayMs = 300;

        static UnityForegroundService()
        {
            Debug.Log("[UFS] Initializing Foreground Service");

            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

            serviceClass = new AndroidJavaClass("com.unity.extension.UnityForegroundService");

            ACTION_UPDATE = serviceClass.GetStatic<string>("ACTION_UPDATE");
            ACTION_STOP = serviceClass.GetStatic<string>("ACTION_STOP");
            EXTRA_TITLE = serviceClass.GetStatic<string>("EXTRA_TITLE");
            EXTRA_MESSAGE = serviceClass.GetStatic<string>("EXTRA_MESSAGE");
        }

        public static void Start()
        {
            if (IsRunning)
            {
                Debug.Log("[UFS] Start() ignored → already running");
                return;
            }

            Debug.Log("[UFS] Start() → Sending startForegroundService()");

            using var intent = new AndroidJavaObject("android.content.Intent", activity, serviceClass);
            int sdk = new AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT");

            if (sdk >= 26)
                activity.Call<AndroidJavaObject>("startForegroundService", intent);
            else
                activity.Call<AndroidJavaObject>("startService", intent);

            ConfirmServiceStarted().Forget();
        }

        public static void Start(string title, string message)
        {
            if (IsRunning)
            {
                Debug.Log("[UFS] Start(title,message) ignored → already running");
                return;
            }

            Debug.Log($"[UFS] Start(title,message) → Starting with '{title}' / '{message}'");

            using var intent = new AndroidJavaObject("android.content.Intent", activity, serviceClass);
            intent.Call<AndroidJavaObject>("putExtra", EXTRA_TITLE, title);
            intent.Call<AndroidJavaObject>("putExtra", EXTRA_MESSAGE, message);

            int sdk = new AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT");
            if (sdk >= 26)
                activity.Call<AndroidJavaObject>("startForegroundService", intent);
            else
                activity.Call<AndroidJavaObject>("startService", intent);

            ConfirmServiceStarted().Forget();
        }


        private static async UniTaskVoid ConfirmServiceStarted()
        {
            Debug.Log("[UFS] ConfirmServiceStarted() checking...");

            for (int i = 0; i < MaxKarmaTries; i++)
            {
                if (CheckAndroidServiceRunning())
                {
                    Debug.Log("[UFS] → Android confirmed service is running");
                    IsRunning = true;
                    return;
                }

                Debug.Log($"[UFS] Karma retry {i + 1}/{MaxKarmaTries}");
                await UniTask.Delay(KarmaDelayMs);
            }

            Debug.LogWarning("[UFS] Android did NOT start service in time");
            IsRunning = false;
        }

        public static void Update(string title, string message)
        {
            Debug.Log($"[UFS] Update() requested: '{title}' / '{message}'");
            TryUpdate(title, message).Forget();
        }

        private static async UniTaskVoid TryUpdate(string title, string message)
        {
            if (!IsRunning)
            {
                Debug.LogWarning("[UFS] Update() ignored → service not running");
                return;
            }

            for (int i = 0; i < MaxKarmaTries; i++)
            {
                if (CheckAndroidServiceRunning())
                {
                    Debug.Log("[UFS] Update() → Sending UPDATE intent");
                    SendUpdateIntent(title, message);
                    return;
                }

                Debug.Log($"[UFS] Update() waiting for service... {i + 1}/{MaxKarmaTries}");
                await UniTask.Delay(KarmaDelayMs);
            }

            Debug.LogWarning("[UFS] Update() failed → service never became active");
            IsRunning = false;
        }

        private static void SendUpdateIntent(string title, string message)
        {
            using var intent = new AndroidJavaObject("android.content.Intent", activity, serviceClass);
            intent.Call<AndroidJavaObject>("setAction", ACTION_UPDATE);
            intent.Call<AndroidJavaObject>("putExtra", EXTRA_TITLE, title);
            intent.Call<AndroidJavaObject>("putExtra", EXTRA_MESSAGE, message);

            Debug.Log("[UFS] Dispatching UPDATE intent");
            activity.Call<AndroidJavaObject>("startService", intent);
        }

        public static void Stop()
        {
            Debug.Log("[UFS] Stop() called");
            TryStop().Forget();
        }

        private static async UniTaskVoid TryStop()
        {
            if (!IsRunning)
            {
                Debug.LogWarning("[UFS] Stop() ignored → service not running");
                return;
            }

            for (int i = 0; i < MaxKarmaTries; i++)
            {
                if (CheckAndroidServiceRunning())
                {
                    Debug.Log("[UFS] Stop() → Sending STOP intent");
                    SendStopIntent();
                    IsRunning = false;
                    return;
                }

                Debug.Log($"[UFS] Stop() waiting for service... {i + 1}/{MaxKarmaTries}");
                await UniTask.Delay(KarmaDelayMs);
            }

            Debug.LogWarning("[UFS] Stop() failed → Android never confirmed running state");
            IsRunning = false;
        }

        private static void SendStopIntent()
        {
            using var intent = new AndroidJavaObject("android.content.Intent", activity, serviceClass);
            intent.Call<AndroidJavaObject>("setAction", ACTION_STOP);
            Debug.Log("[UFS] Dispatching STOP intent");
            activity.Call<AndroidJavaObject>("startService", intent);
        }

        private static bool CheckAndroidServiceRunning()
        {
            try
            {
                bool r = serviceClass.CallStatic<bool>("isRunning");
                Debug.Log($"[UFS] Android isRunning() = {r}");
                return r;
            }
            catch (Exception e)
            {
                Debug.LogError("[UFS] Error calling isRunning(): " + e);
                return false;
            }
        }

#else
        public static bool IsRunning => false;
        public static void Start() { }
        public static void Start(string title, string message) { }
        public static void Stop() { }
        public static void Update(string title, string message) { }

#endif
    }
}
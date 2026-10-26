#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
#endif

namespace UFS
{
    public enum ShortTaskStartResult
    {
        Rejected = 0,
        Requested = 1,
        AlreadyRequested = 2,
        AlreadyRunning = 3
    }

    public static class UnityForegroundService
    {
        public const int DefaultDeadlineSeconds = 120;
        public const int MaxDeadlineSeconds = 150;

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject activity;
        private static AndroidJavaClass serviceClass;
        private static string actionStartShortTask;
        private static string actionUpdate;
        private static string actionStop;
        private static string actionCancelShortTask;
        private static string actionDisconnectConfirmed;
        private static string extraTitle;
        private static string extraMessage;
        private static string extraTaskId;
        private static string extraDeadlineMillis;
        private static string requestedTaskId;
        private static bool initialized;
        private static bool initializationFailed;

        public static bool IsRunning
        {
            get { return CheckAndroidServiceRunning(); }
        }

        public static void Start()
        {
            Start("Connection active", "Finishing background operation");
        }

        public static void Start(string title, string message)
        {
            StartShortTask(NewTaskId(), title, message, DefaultDeadlineSeconds);
        }

        public static ShortTaskStartResult StartShortTask(
            string taskId,
            string title,
            string message,
            int deadlineSeconds = DefaultDeadlineSeconds)
        {
            if (string.IsNullOrEmpty(taskId))
            {
                Debug.LogError("[UFS] StartShortTask rejected: task ID is empty");
                return ShortTaskStartResult.Rejected;
            }

            if (!EnsureInitialized())
                return ShortTaskStartResult.Rejected;

            if (string.Equals(requestedTaskId, taskId, StringComparison.Ordinal))
                return ShortTaskStartResult.AlreadyRequested;

            if (CheckAndroidServiceRunning())
            {
                Debug.LogWarning("[UFS] StartShortTask rejected: another native task is already running");
                return ShortTaskStartResult.AlreadyRunning;
            }

            int boundedSeconds = Math.Max(1, Math.Min(MaxDeadlineSeconds, deadlineSeconds));
            try
            {
                using (var intent = CreateIntent(actionStartShortTask))
                {
                    PutExtra(intent, extraTaskId, taskId);
                    PutExtra(intent, extraTitle, title ?? string.Empty);
                    PutExtra(intent, extraMessage, message ?? string.Empty);
                    PutExtra(intent, extraDeadlineMillis, (long)boundedSeconds * 1000L);
                    StartForegroundService(intent);
                }

                requestedTaskId = taskId;
                Debug.Log("[UFS] Short-task start requested; IsRunning becomes true only after native confirmation");
                ConfirmServiceStarted(taskId).Forget();
                return ShortTaskStartResult.Requested;
            }
            catch (Exception exception)
            {
                requestedTaskId = null;
                Debug.LogError("[UFS] Short-task start rejected by Android: " + exception);
                return ShortTaskStartResult.Rejected;
            }
        }

        public static void Update(string title, string message)
        {
            if (!EnsureInitialized() || !CheckAndroidServiceRunning())
            {
                Debug.LogWarning("[UFS] Update ignored: native service is not confirmed running");
                return;
            }

            try
            {
                using (var intent = CreateIntent(actionUpdate))
                {
                    PutExtra(intent, extraTitle, title ?? string.Empty);
                    PutExtra(intent, extraMessage, message ?? string.Empty);
                    SendServiceCommand(intent);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("[UFS] Notification update failed: " + exception);
            }
        }

        public static void CancelShortTask(string taskId)
        {
            if (!EnsureInitialized() || string.IsNullOrEmpty(taskId))
                return;

            try
            {
                if (CheckAndroidServiceRunning())
                {
                    using (var intent = CreateIntent(actionCancelShortTask))
                    {
                        PutExtra(intent, extraTaskId, taskId);
                        SendServiceCommand(intent);
                    }
                }
                else
                {
                    // If startForegroundService is still pending, do not issue a new
                    // background start merely to cancel it. stopService is idempotent.
                    StopServiceDirectly();
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("[UFS] Short-task cancellation failed: " + exception);
            }

            if (string.Equals(requestedTaskId, taskId, StringComparison.Ordinal))
                requestedTaskId = null;
        }

        public static void StopShortTaskAfterDisconnect(string taskId)
        {
            if (!EnsureInitialized() || string.IsNullOrEmpty(taskId))
                return;

            if (!CheckAndroidServiceRunning())
            {
                requestedTaskId = null;
                Debug.LogWarning("[UFS] Disconnect acknowledgement ignored: native service is no longer running");
                return;
            }

            try
            {
                using (var intent = CreateIntent(actionDisconnectConfirmed))
                {
                    PutExtra(intent, extraTaskId, taskId);
                    SendServiceCommand(intent);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("[UFS] Disconnect acknowledgement failed: " + exception);
            }

            if (string.Equals(requestedTaskId, taskId, StringComparison.Ordinal))
                requestedTaskId = null;
        }

        public static void Stop()
        {
            if (!EnsureInitialized())
                return;

            try
            {
                using (var intent = CreateIntent(actionStop))
                    SendServiceCommand(intent);
            }
            catch (Exception exception)
            {
                Debug.LogError("[UFS] Stop failed: " + exception);
            }

            requestedTaskId = null;
        }

        private static string NewTaskId()
        {
            return Guid.NewGuid().ToString("N");
        }

        private static bool EnsureInitialized()
        {
            if (initialized)
                return true;
            if (initializationFailed)
                return false;

            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

                if (activity == null)
                    throw new InvalidOperationException("Unity currentActivity is null");

                serviceClass = new AndroidJavaClass("com.unity.extension.UnityForegroundService");
                actionStartShortTask = serviceClass.GetStatic<string>("ACTION_START_SHORT_TASK");
                actionUpdate = serviceClass.GetStatic<string>("ACTION_UPDATE");
                actionStop = serviceClass.GetStatic<string>("ACTION_STOP");
                actionCancelShortTask = serviceClass.GetStatic<string>("ACTION_CANCEL_SHORT_TASK");
                actionDisconnectConfirmed = serviceClass.GetStatic<string>("ACTION_DISCONNECT_CONFIRMED");
                extraTitle = serviceClass.GetStatic<string>("EXTRA_TITLE");
                extraMessage = serviceClass.GetStatic<string>("EXTRA_MESSAGE");
                extraTaskId = serviceClass.GetStatic<string>("EXTRA_TASK_ID");
                extraDeadlineMillis = serviceClass.GetStatic<string>("EXTRA_DEADLINE_MILLIS");
                initialized = true;
                Debug.Log("[UFS] Android bridge initialized");
                return true;
            }
            catch (Exception exception)
            {
                initializationFailed = true;
                Debug.LogError("[UFS] Android bridge initialization failed: " + exception);
                return false;
            }
        }

        private static AndroidJavaObject CreateIntent(string action)
        {
            var intent = new AndroidJavaObject("android.content.Intent", activity, serviceClass);
            using (var ignored = intent.Call<AndroidJavaObject>("setAction", action)) { }
            return intent;
        }

        private static void PutExtra(AndroidJavaObject intent, string key, string value)
        {
            using (var ignored = intent.Call<AndroidJavaObject>("putExtra", key, value)) { }
        }

        private static void PutExtra(AndroidJavaObject intent, string key, long value)
        {
            using (var ignored = intent.Call<AndroidJavaObject>("putExtra", key, value)) { }
        }

        private static void StartForegroundService(AndroidJavaObject intent)
        {
            using (var sdkVersion = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                int sdk = sdkVersion.GetStatic<int>("SDK_INT");
                if (sdk >= 26)
                {
                    using (var ignored = activity.Call<AndroidJavaObject>("startForegroundService", intent)) { }
                }
                else
                    using (var ignored = activity.Call<AndroidJavaObject>("startService", intent)) { }
            }
        }

        private static void SendServiceCommand(AndroidJavaObject intent)
        {
            using (var ignored = activity.Call<AndroidJavaObject>("startService", intent)) { }
        }

        private static void StopServiceDirectly()
        {
            using (var intent = CreateIntent(actionStop))
                activity.Call<bool>("stopService", intent);
        }

        private static bool CheckAndroidServiceRunning()
        {
            if (!EnsureInitialized())
                return false;

            try
            {
                return serviceClass.CallStatic<bool>("isRunning");
            }
            catch (Exception exception)
            {
                Debug.LogError("[UFS] Native service-state query failed: " + exception);
                return false;
            }
        }

        private static async UniTaskVoid ConfirmServiceStarted(string taskId)
        {
            try
            {
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    if (!string.Equals(requestedTaskId, taskId, StringComparison.Ordinal))
                        return;
                    if (CheckAndroidServiceRunning())
                    {
                        Debug.Log("[UFS] Android confirmed the short task is running");
                        return;
                    }

                    await UniTask.Delay(250);
                }

                if (string.Equals(requestedTaskId, taskId, StringComparison.Ordinal))
                {
                    requestedTaskId = null;
                    Debug.LogWarning("[UFS] Android did not confirm the short task before the bounded confirmation window");
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("[UFS] Short-task confirmation failed: " + exception);
            }
        }

#else

        public const bool IsRunning = false;

        public static void Start()
        { }

        public static void Start(string title, string message)
        { }

        public static ShortTaskStartResult StartShortTask(
            string taskId,
            string title,
            string message,
            int deadlineSeconds = DefaultDeadlineSeconds)
        {
            return ShortTaskStartResult.Rejected;
        }

        public static void Update(string title, string message)
        { }

        public static void CancelShortTask(string taskId)
        { }

        public static void StopShortTaskAfterDisconnect(string taskId)
        { }

        public static void Stop()
        { }

#endif
    }
}

using Cysharp.Threading.Tasks;
using System;
using UnityEngine;

namespace UFS
{
    /// <summary>
    /// Owns the Unity half of one bounded background operation. The consuming game
    /// supplies the real SmartFox adapter through RegisterSmartFoxDisconnectHandler.
    /// </summary>
    public sealed class UFSShortTaskController : MonoBehaviour
    {
        public const string GameObjectName = "UFSShortTaskController";

        private static UFSShortTaskController instance;
        private static Func<string, bool> smartFoxDisconnectHandler;

        private string activeTaskId;
        private bool disconnectStarted;
        private bool applicationPaused;

        public static string NotificationTitle { get; private set; } = "Connection active";
        public static string NotificationMessage { get; private set; } = "Finishing background operation";
        public static int DeadlineSeconds { get; private set; } = UnityForegroundService.DefaultDeadlineSeconds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeAutomatically()
        {
            EnsureInstance();
        }

        public static void RegisterSmartFoxDisconnectHandler(Func<string, bool> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            smartFoxDisconnectHandler = handler;
        }

        public static void UnregisterSmartFoxDisconnectHandler()
        {
            smartFoxDisconnectHandler = null;
        }

        public static void Configure(string title, string message, int deadlineSeconds)
        {
            NotificationTitle = title ?? string.Empty;
            NotificationMessage = message ?? string.Empty;
            DeadlineSeconds = Math.Max(1, Math.Min(UnityForegroundService.MaxDeadlineSeconds, deadlineSeconds));
        }

        private static UFSShortTaskController EnsureInstance()
        {
            if (instance != null)
                return instance;

            var existing = FindObjectOfType<UFSShortTaskController>();
            if (existing != null)
            {
                instance = existing;
                return instance;
            }

            var gameObject = new GameObject(GameObjectName);
            instance = gameObject.AddComponent<UFSShortTaskController>();
            DontDestroyOnLoad(gameObject);
            return instance;
        }

        private void Awake()
        {
            if (instance != null && !ReferenceEquals(instance, this))
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private async void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus == applicationPaused)
                return;

            applicationPaused = pauseStatus;
            if (pauseStatus)
                BeginShortTask();
            else
            {
                await UniTask.Delay(500);
                CancelActiveTask("application resumed");
            }
        }

        private void OnApplicationQuit()
        {
            CancelActiveTask("application quit");
        }

        private void BeginShortTask()
        {
            if (!string.IsNullOrEmpty(activeTaskId))
            {
                Debug.Log("[UFS] Duplicate pause ignored for active short task");
                return;
            }

            string taskId = Guid.NewGuid().ToString("N");
            ShortTaskStartResult result = UnityForegroundService.StartShortTask(
                taskId,
                NotificationTitle,
                NotificationMessage,
                DeadlineSeconds);

            if (result == ShortTaskStartResult.Rejected || result == ShortTaskStartResult.AlreadyRunning)
            {
                Debug.LogWarning("[UFS] Could not start the bounded background task: " + result);
                return;
            }

            activeTaskId = taskId;
            disconnectStarted = false;
            Debug.Log("[UFS] Background short task registered: " + taskId);
        }

        /// <summary>
        /// Called from Java through UnitySendMessage. The method is public because
        /// UnitySendMessage cannot invoke a private method.
        /// </summary>
        public void OnNativeShortTaskDeadline(string taskId)
        {
            if (!IsCurrentTask(taskId) || disconnectStarted)
            {
                Debug.Log("[UFS] Ignoring stale or duplicate native deadline callback");
                return;
            }

            disconnectStarted = true;
            DisconnectForTask(taskId);
        }

        public void OnNativeShortTaskTimeout(string taskId)
        {
            if (!IsCurrentTask(taskId))
                return;

            Debug.LogWarning("[UFS] Android stopped the short task at its system timeout");
            DisconnectForTask(taskId);
            activeTaskId = null;
            disconnectStarted = false;
        }

        private void DisconnectForTask(string taskId)
        {
            var handler = smartFoxDisconnectHandler;
            if (handler == null)
            {
                Debug.LogWarning("[UFS] No SmartFox disconnect handler is registered; native fallback will stop the service");
                return;
            }

            bool success = false;
            try
            {
                success = handler.Invoke(taskId);
            }
            catch (TimeoutException)
            {
                Debug.LogWarning("[UFS] SmartFox disconnect handler reported a timeout");
            }
            catch (Exception exception)
            {
                Debug.LogError("[UFS] SmartFox disconnect failed: " + exception);
            }

            if (!success || !IsCurrentTask(taskId) || applicationPaused)
            {
                if (!success)
                    Debug.LogWarning("[UFS] SmartFox disconnect was not confirmed; native fallback remains authoritative");
                return;
            }

            UnityForegroundService.StopShortTaskAfterDisconnect(taskId);
            activeTaskId = null;
            disconnectStarted = false;
            Debug.Log("[UFS] SmartFox disconnect confirmed and native short task acknowledged");
        }

        private void CancelActiveTask(string reason)
        {
            if (string.IsNullOrEmpty(activeTaskId))
                return;

            string taskId = activeTaskId;
            activeTaskId = null;
            disconnectStarted = false;
            UnityForegroundService.CancelShortTask(taskId);
            Debug.Log("[UFS] Short task cancelled: " + reason);
        }

        private bool IsCurrentTask(string taskId)
        {
            return !string.IsNullOrEmpty(taskId) && string.Equals(activeTaskId, taskId, StringComparison.Ordinal);
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(instance, this))
            {
                instance = null;
            }
        }
    }
}

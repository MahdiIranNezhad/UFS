using System;
using System.Threading;
using Cysharp.Threading.Tasks;
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
        public const int DefaultDisconnectTimeoutMilliseconds = 4000;

        private static UFSShortTaskController instance;
        private static Func<string, CancellationToken, UniTask<bool>> smartFoxDisconnectHandler;

        private string activeTaskId;
        private CancellationTokenSource disconnectCancellation;
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

        /// <summary>
        /// Registers the consuming game's actual SmartFox disconnect operation.
        /// Return true only after the SDK confirms disconnect, or when it was already
        /// disconnected. The handler must honor cancellation and complete quickly.
        /// </summary>
        public static void RegisterSmartFoxDisconnectHandler(
            Func<string, CancellationToken, UniTask<bool>> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            smartFoxDisconnectHandler = handler;
        }

        public static void UnregisterSmartFoxDisconnectHandler(
            Func<string, CancellationToken, UniTask<bool>> handler)
        {
            if (ReferenceEquals(smartFoxDisconnectHandler, handler))
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

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus == applicationPaused)
                return;

            applicationPaused = pauseStatus;
            if (pauseStatus)
                BeginShortTask();
            else
                CancelActiveTask("application resumed");
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
            DisconnectForTaskAsync(taskId).Forget();
        }

        public void OnNativeShortTaskTimeout(string taskId)
        {
            if (!IsCurrentTask(taskId))
                return;

            Debug.LogWarning("[UFS] Android stopped the short task at its system timeout");
            CancelDisconnectOnly();
            activeTaskId = null;
            disconnectStarted = false;
        }

        private async UniTaskVoid DisconnectForTaskAsync(string taskId)
        {
            var handler = smartFoxDisconnectHandler;
            if (handler == null)
            {
                Debug.LogWarning("[UFS] No SmartFox disconnect handler is registered; native fallback will stop the service");
                return;
            }

            CancelDisconnectOnly();
            disconnectCancellation = new CancellationTokenSource();
            disconnectCancellation.CancelAfter(DefaultDisconnectTimeoutMilliseconds);

            bool success = false;
            try
            {
                success = await handler(taskId, disconnectCancellation.Token)
                    .Timeout(TimeSpan.FromMilliseconds(DefaultDisconnectTimeoutMilliseconds));
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("[UFS] SmartFox disconnect was cancelled or timed out");
            }
            catch (TimeoutException)
            {
                Debug.LogWarning("[UFS] SmartFox disconnect exceeded its bounded timeout");
            }
            catch (Exception exception)
            {
                Debug.LogError("[UFS] SmartFox disconnect failed: " + exception);
            }
            finally
            {
                if (disconnectCancellation != null)
                {
                    disconnectCancellation.Dispose();
                    disconnectCancellation = null;
                }
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
            CancelDisconnectOnly();
            UnityForegroundService.CancelShortTask(taskId);
            Debug.Log("[UFS] Short task cancelled: " + reason);
        }

        private void CancelDisconnectOnly()
        {
            if (disconnectCancellation == null)
                return;

            disconnectCancellation.Cancel();
            disconnectCancellation.Dispose();
            disconnectCancellation = null;
        }

        private bool IsCurrentTask(string taskId)
        {
            return !string.IsNullOrEmpty(taskId) && string.Equals(activeTaskId, taskId, StringComparison.Ordinal);
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(instance, this))
            {
                CancelDisconnectOnly();
                instance = null;
            }
        }
    }
}

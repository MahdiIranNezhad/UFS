package com.unity.extension;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.os.SystemClock;
import android.util.Log;

import java.util.UUID;

public class UnityForegroundService extends Service
{
    private static final String TAG = "UFS";

    public static final String CHANNEL_ID = "unity_fg_service_channel";
    public static final int NOTIFICATION_ID = 123456;

    // Existing public action/extra names are retained for callers that already use them.
    public static final String ACTION_UPDATE = "UFS_UPDATE";
    public static final String ACTION_STOP = "UFS_STOP";
    public static final String EXTRA_TITLE = "UFS_TITLE";
    public static final String EXTRA_MESSAGE = "UFS_MESSAGE";

    public static final String ACTION_START = "UFS_START";
    public static final String ACTION_START_SHORT_TASK = "UFS_START_SHORT_TASK";
    public static final String ACTION_CANCEL_SHORT_TASK = "UFS_CANCEL_SHORT_TASK";
    public static final String ACTION_DISCONNECT_CONFIRMED = "UFS_DISCONNECT_CONFIRMED";
    public static final String EXTRA_TASK_ID = "UFS_TASK_ID";
    public static final String EXTRA_DEADLINE_MILLIS = "UFS_DEADLINE_MILLIS";

    public static final long DEFAULT_DEADLINE_MILLIS = 120000L;
    // Android documents shortService as approximately three minutes. Keep a margin for
    // Unity delivery, cleanup, and device scheduling before the system timeout/ANR window.
    public static final long MAX_DEADLINE_MILLIS = 150000L;
    public static final long MIN_DEADLINE_MILLIS = 1000L;
    public static final long UNITY_FALLBACK_GRACE_MILLIS = 5000L;

    private static final String UNITY_GAME_OBJECT = "UFSShortTaskController";
    private static final String UNITY_DEADLINE_METHOD = "OnNativeShortTaskDeadline";
    private static final String UNITY_TIMEOUT_METHOD = "OnNativeShortTaskTimeout";

    private static volatile boolean isRunning = false;

    private String currentTitle = "Connection active";
    private String currentMessage = "Finishing background operation";

    private boolean isRunningForeground = false;
    private String activeTaskId;
    private long activeDeadlineElapsedRealtime;
    private boolean deadlineDelivered;

    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private Runnable deadlineRunnable;
    private Runnable fallbackRunnable;

    public static boolean isRunning()
    {
        return isRunning;
    }

    @Override
    public void onCreate()
    {
        super.onCreate();
        createNotificationChannel();
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId)
    {
        if (intent == null)
        {
            Log.w(TAG, "Ignoring null service intent");
            return START_NOT_STICKY;
        }

        String action = intent.getAction();

        if (ACTION_STOP.equals(action))
        {
            stopServiceSafely("explicit stop");
            return START_NOT_STICKY;
        }

        if (ACTION_UPDATE.equals(action))
        {
            if (!isRunningForeground)
            {
                Log.w(TAG, "Ignoring notification update while service is inactive");
                stopSelf();
                return START_NOT_STICKY;
            }

            String extraTitle = intent.getStringExtra(EXTRA_TITLE);
            String extraMessage = intent.getStringExtra(EXTRA_MESSAGE);
            if (extraTitle != null) currentTitle = extraTitle;
            if (extraMessage != null) currentMessage = extraMessage;
            updateNotification();
            return START_NOT_STICKY;
        }

        if (ACTION_CANCEL_SHORT_TASK.equals(action))
        {
            String taskId = intent.getStringExtra(EXTRA_TASK_ID);
            if (taskId == null || taskId.length() == 0 || taskMatches(taskId))
                stopServiceSafely("short task cancelled");
            else
            {
                Log.w(TAG, "Ignoring cancellation for stale task");
                if (!isRunningForeground)
                    stopSelf();
            }
            return START_NOT_STICKY;
        }

        if (ACTION_DISCONNECT_CONFIRMED.equals(action))
        {
            String taskId = intent.getStringExtra(EXTRA_TASK_ID);
            if (taskMatches(taskId))
                stopServiceSafely("disconnect confirmed");
            else
            {
                Log.w(TAG, "Ignoring disconnect acknowledgement for stale or missing task");
                if (!isRunningForeground)
                    stopSelf();
            }
            return START_NOT_STICKY;
        }

        // ACTION_START, ACTION_START_SHORT_TASK, and a null action retain the old
        // implicit-start behavior while assigning a bounded native task deadline.
        if (action == null || ACTION_START.equals(action) || ACTION_START_SHORT_TASK.equals(action))
            startShortTask(intent);

        return START_NOT_STICKY;
    }

    private void startShortTask(Intent intent)
    {
        String extraTitle = intent.getStringExtra(EXTRA_TITLE);
        String extraMessage = intent.getStringExtra(EXTRA_MESSAGE);
        String requestedTaskId = intent.getStringExtra(EXTRA_TASK_ID);
        if (requestedTaskId == null || requestedTaskId.length() == 0)
            requestedTaskId = UUID.randomUUID().toString();

        long requestedDeadline = intent.getLongExtra(EXTRA_DEADLINE_MILLIS, DEFAULT_DEADLINE_MILLIS);
        long deadlineMillis = clampDeadlineMillis(requestedDeadline);

        if (isRunningForeground)
        {
            if (taskMatches(requestedTaskId))
            {
                // Do not call startForeground again: doing so can extend Android's
                // shortService window and would violate the original task deadline.
                if (extraTitle != null) currentTitle = extraTitle;
                if (extraMessage != null) currentMessage = extraMessage;
                updateNotification();
                Log.i(TAG, "Duplicate start ignored for active task");
            }
            else
                Log.w(TAG, "Overlapping short task rejected while another task is active");
            return;
        }

        if (extraTitle != null) currentTitle = extraTitle;
        if (extraMessage != null) currentMessage = extraMessage;

        try
        {
            Notification notification = buildNotification();
            if (Build.VERSION.SDK_INT >= 34)
                startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_SHORT_SERVICE);
            else
                startForeground(NOTIFICATION_ID, notification);
        }
        catch (Exception exception)
        {
            Log.e(TAG, "Unable to promote service to shortService foreground state", exception);
            stopServiceSafely("foreground promotion failed");
            return;
        }

        activeTaskId = requestedTaskId;
        activeDeadlineElapsedRealtime = SystemClock.elapsedRealtime() + deadlineMillis;
        deadlineDelivered = false;
        isRunningForeground = true;
        isRunning = true;
        scheduleDeadline(requestedTaskId, deadlineMillis);
        Log.i(TAG, "Started bounded short task");
    }

    @Override
    public void onDestroy()
    {
        cancelScheduledCallbacks();
        isRunningForeground = false;
        isRunning = false;
        activeTaskId = null;
        activeDeadlineElapsedRealtime = 0L;
        deadlineDelivered = false;
        stopForegroundSafely();
        super.onDestroy();
    }

    @Override
    public void onTaskRemoved(Intent rootIntent)
    {
        // The manifest deliberately uses stopWithTask=true. This explicit cleanup also
        // covers launchers/devices that dispatch onTaskRemoved before destroying us.
        stopServiceSafely("application task removed");
        super.onTaskRemoved(rootIntent);
    }

    @Override
    public void onTimeout(int startId)
    {
        Log.w(TAG, "Android shortService timeout received");
        notifyUnity(UNITY_TIMEOUT_METHOD, activeTaskId);
        stopServiceSafely("Android shortService timeout");
    }

    @Override
    public void onTimeout(int startId, int fgsType)
    {
        Log.w(TAG, "Android typed foreground-service timeout received");
        notifyUnity(UNITY_TIMEOUT_METHOD, activeTaskId);
        stopServiceSafely("Android typed foreground-service timeout");
    }

    @Override
    public IBinder onBind(Intent intent)
    {
        return null;
    }

    private Notification buildNotification()
    {
        Intent notificationIntent = getPackageManager().getLaunchIntentForPackage(getPackageName());
        Notification.Builder builder;
        if (Build.VERSION.SDK_INT >= 26)
            builder = new Notification.Builder(this, CHANNEL_ID);
        else
            builder = new Notification.Builder(this);

        builder.setContentTitle(currentTitle)
                .setContentText(currentMessage)
                .setSmallIcon(android.R.drawable.stat_notify_sync)
                .setOngoing(true)
                .setShowWhen(false);

        if (notificationIntent != null)
        {
            int pendingIntentFlags = PendingIntent.FLAG_UPDATE_CURRENT;
            if (Build.VERSION.SDK_INT >= 23)
                pendingIntentFlags |= PendingIntent.FLAG_IMMUTABLE;
            PendingIntent pendingIntent = PendingIntent.getActivity(this, 0, notificationIntent, pendingIntentFlags);
            builder.setContentIntent(pendingIntent);
        }

        Notification notification = builder.build();

        if (Build.VERSION.SDK_INT < 26)
        {
            notification.sound = null;
            notification.vibrate = null;
            notification.defaults = 0;
        }

        return notification;
    }

    private void updateNotification()
    {
        NotificationManager manager = (NotificationManager) getSystemService(NOTIFICATION_SERVICE);
        if (manager != null)
            manager.notify(NOTIFICATION_ID, buildNotification());
    }

    private void createNotificationChannel()
    {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O)
        {
            NotificationChannel channel = new NotificationChannel(
                    CHANNEL_ID,
                    "Game Foreground Service",
                    NotificationManager.IMPORTANCE_LOW
            );

            channel.enableLights(false);
            channel.enableVibration(false);
            channel.setSound(null, null);
            channel.setShowBadge(false);
            channel.setLockscreenVisibility(Notification.VISIBILITY_PRIVATE);

            NotificationManager manager = getSystemService(NotificationManager.class);
            if (manager != null)
                manager.createNotificationChannel(channel);
        }
    }

    private long clampDeadlineMillis(long requestedMillis)
    {
        if (requestedMillis <= 0L)
            requestedMillis = DEFAULT_DEADLINE_MILLIS;
        return Math.max(MIN_DEADLINE_MILLIS, Math.min(MAX_DEADLINE_MILLIS, requestedMillis));
    }

    private boolean taskMatches(String taskId)
    {
        return taskId != null && taskId.length() > 0 && taskId.equals(activeTaskId);
    }

    private void scheduleDeadline(final String taskId, long delayMillis)
    {
        cancelScheduledCallbacks();
        deadlineRunnable = new Runnable()
        {
            @Override
            public void run()
            {
                if (!isRunningForeground || !taskMatches(taskId) || deadlineDelivered)
                    return;

                long remaining = activeDeadlineElapsedRealtime - SystemClock.elapsedRealtime();
                if (remaining > 0L)
                {
                    mainHandler.postDelayed(this, remaining);
                    return;
                }

                deadlineDelivered = true;
                notifyUnity(UNITY_DEADLINE_METHOD, taskId);
                fallbackRunnable = new Runnable()
                {
                    @Override
                    public void run()
                    {
                        if (isRunningForeground && taskMatches(taskId))
                            stopServiceSafely("Unity disconnect acknowledgement timeout");
                    }
                };
                mainHandler.postDelayed(fallbackRunnable, UNITY_FALLBACK_GRACE_MILLIS);
            }
        };
        mainHandler.postDelayed(deadlineRunnable, delayMillis);
    }

    private void cancelScheduledCallbacks()
    {
        if (deadlineRunnable != null)
            mainHandler.removeCallbacks(deadlineRunnable);
        if (fallbackRunnable != null)
            mainHandler.removeCallbacks(fallbackRunnable);
        deadlineRunnable = null;
        fallbackRunnable = null;
    }

    private void stopServiceSafely(String reason)
    {
        Log.i(TAG, "Stopping service: " + reason);
        cancelScheduledCallbacks();
        isRunningForeground = false;
        isRunning = false;
        activeTaskId = null;
        activeDeadlineElapsedRealtime = 0L;
        deadlineDelivered = false;
        stopForegroundSafely();
        stopSelf();
    }

    private void stopForegroundSafely()
    {
        if (Build.VERSION.SDK_INT >= 24)
            stopForeground(Service.STOP_FOREGROUND_REMOVE);
        else
            stopForeground(true);
    }

    private void notifyUnity(String methodName, String value)
    {
        if (value == null || value.length() == 0)
            return;
        try
        {
            Class<?> unityPlayerClass = Class.forName("com.unity3d.player.UnityPlayer");
            unityPlayerClass.getMethod("UnitySendMessage", String.class, String.class, String.class)
                    .invoke(null, UNITY_GAME_OBJECT, methodName, value);
        }
        catch (Exception exception)
        {
            // Unity may be suspended, not initialized, or already dead. The native
            // fallback remains authoritative and will stop the service.
            Log.w(TAG, "Unity callback unavailable; native fallback remains armed", exception);
        }
    }
}

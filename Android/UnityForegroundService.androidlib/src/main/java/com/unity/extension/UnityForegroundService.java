package com.unity.extension;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Intent;
import android.os.Build;
import android.os.IBinder;

public class UnityForegroundService extends Service
{

    public static final String CHANNEL_ID = "unity_fg_service_channel";
    public static final int NOTIFICATION_ID = 123456;

    public static final String ACTION_UPDATE = "UFS_UPDATE";
    public static final String ACTION_STOP = "UFS_STOP";
    public static final String EXTRA_TITLE = "UFS_TITLE";
    public static final String EXTRA_MESSAGE = "UFS_MESSAGE";

    private static boolean isRunning = false;

    private String currentTitle = "Connection active";
    private String currentMessage = "Keeping connection alive";

    private boolean isRunningForeground = false;

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
            return START_NOT_STICKY;

        String action = intent.getAction();

        // ----------------------
        // STOP (ignore if not running)
        // ----------------------
        if (ACTION_STOP.equals(action))
        {
            if (!isRunningForeground)
                return START_NOT_STICKY;

            stopForeground(true);
            stopSelf();
            isRunningForeground = false;
            isRunning = false;
            return START_NOT_STICKY;
        }

        // ----------------------
        // UPDATE (ignore if not running)
        // ----------------------
        if (ACTION_UPDATE.equals(action))
        {
            if (!isRunningForeground)
                return START_NOT_STICKY;

            currentTitle = intent.getStringExtra(EXTRA_TITLE);
            currentMessage = intent.getStringExtra(EXTRA_MESSAGE);
            updateNotification();
            return START_NOT_STICKY;
        }

        // ----------------------
        // NORMAL FOREGROUND START
        // ----------------------
        String extraTitle = intent.getStringExtra(EXTRA_TITLE);
        String extraMessage = intent.getStringExtra(EXTRA_MESSAGE);

        if (extraTitle != null) currentTitle = extraTitle;
        if (extraMessage != null) currentMessage = extraMessage;

        startForeground(NOTIFICATION_ID, buildNotification());
        isRunningForeground = true;
        isRunning = true;

        return START_NOT_STICKY;
    }

    @Override
    public void onDestroy()
    {
        isRunningForeground = false;
        isRunning = false;
        stopForeground(true);
        stopSelf();
        super.onDestroy();
    }

    @Override
    public void onTaskRemoved(Intent rootIntent)
    {
        stopForeground(true);
        stopSelf();
        super.onTaskRemoved(rootIntent);
    }

    @Override
    public IBinder onBind(Intent intent)
    {
        return null;
    }

    private Notification buildNotification()
    {
        Intent notificationIntent = getPackageManager().getLaunchIntentForPackage(getPackageName());
        PendingIntent pendingIntent = PendingIntent.getActivity(this, 0, notificationIntent, PendingIntent.FLAG_IMMUTABLE);

        Notification.Builder builder =
                new Notification.Builder(this, CHANNEL_ID)
                        .setContentTitle(currentTitle)
                        .setContentText(currentMessage)
                        .setSmallIcon(android.R.drawable.stat_notify_sync)
                        .setOngoing(true)
                        .setShowWhen(false)
                        .setContentIntent(pendingIntent);

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
}

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

    public static final String ACTION_UPDATE = "UNITY_FS_UPDATE";
    public static final String ACTION_STOP = "UNITY_FS_STOP";

    public static final String EXTRA_TITLE = "title";
    public static final String EXTRA_MESSAGE = "message";

    private String currentTitle = "Connection active";
    private String currentMessage = "Keeping connection alive";

    @Override
    public void onCreate()
    {
        super.onCreate();
        createNotificationChannel();
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId)
    {

        if (intent != null)
        {
            String action = intent.getAction();

            if (ACTION_UPDATE.equals(action))
            {
                currentTitle = intent.getStringExtra(EXTRA_TITLE);
                currentMessage = intent.getStringExtra(EXTRA_MESSAGE);
                updateNotification();
                return START_NOT_STICKY;
            }

            if (ACTION_STOP.equals(action))
            {
                stopForeground(true);
                stopSelf();
                return START_NOT_STICKY;
            }
        }

        startForeground(NOTIFICATION_ID, buildNotification());
        return START_NOT_STICKY;
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

        return new Notification.Builder(this, CHANNEL_ID)
                .setContentTitle(currentTitle)
                .setContentText(currentMessage)
                .setSmallIcon(android.R.drawable.stat_notify_sync)
                .setContentIntent(pendingIntent)
                .setOngoing(true)
                .build();
    }

    private void updateNotification()
    {
        NotificationManager manager = (NotificationManager) getSystemService(NOTIFICATION_SERVICE);
        if (manager != null)
        {
            manager.notify(NOTIFICATION_ID, buildNotification());
        }
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
            NotificationManager nm = getSystemService(NotificationManager.class);
            if (nm != null) nm.createNotificationChannel(channel);
        }
    }

    @Override
    public void onTaskRemoved(Intent rootIntent)
    {
        stopForeground(true);
        stopSelf();
        super.onTaskRemoved(rootIntent);
    }

    @Override
    public void onDestroy()
    {
        stopForeground(true);
        super.onDestroy();
    }
}

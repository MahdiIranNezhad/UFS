# Unity Foreground Service (Android) – UPM Package

## Overview
This package provides a production‑ready **Android Foreground Service integration for Unity**, delivered as a clean **UPM package** with full androidlib structure. It is designed specifically for scenarios where an Android app must maintain long‑running network operations (socket connections, SmartFoxServer, realtime multiplayer, background sync) without being killed by Android's aggressive background restrictions.

Android 13+ and Android 14+ enforce strict network limitations when an app is minimized, locked, or swipe‑closed. Without a foreground service, sockets terminate after ~60 seconds. This package solves that.

---

## Problem Summary
### Background Networking Issue (Android 13+ / 14)
Unity games relying on persistent networking (SmartFoxServer, WebSocket, TCP, UDP) experience:

- Disconnection after ~60 seconds when app goes to background
- Failed reconnection attempts
- `CONNECTION_RETRY` and `CONNECTION_RESUME` never firing
- Socket being fully terminated by the OS

**Root Cause:**  
Modern Android versions aggressively suspend or kill apps running background network tasks unless a **Foreground Service** is active.

---

## Package Solution
This package implements:

### 1. A complete, optimized Java Foreground Service
- Runs persistently while app is backgrounded
- Uses `remoteMessaging` and `dataSync` service types
- Handles Android 12–14 requirements
- Stops cleanly with Unity lifecycle events

### 2. Full androidlib structure merged automatically
- No manual editing of `Assets/Plugins/Android`
- No project‑level manifest hacking
- Everything contained inside the UPM package

### 3. Runtime‑updateable notification
You can dynamically change:
- Notification title
- Notification message

Useful for localization, game state, matchmaking status, reconnection attempts, etc.

### 4. C# wrapper API
Provides:
- `Start()`
- `Stop()`
- `Update(title, message)`

All without touching Java code.

---

## Project State Before Using This Package
### Typical problems:
- Socket disconnects when app minimized after ~65 seconds
- Reconnection logic never fires
- Background sync suspended and no reliable way to maintain networking during minimize or lock screen.

### After installing the package:
- Stable realtime networking in background
- Socket connection stays alive indefinitely.
- SmartFoxServer reconnection works normally.
- Network background execution is stable.

---

## Installation (UPM)
Add via Git URL:
```
https://github.com/MahdiIranNezhad/UFS.git
```
Or add to `Packages/manifest.json`:
```json
"com.unity.extension.unityforegroundservice": "https://github.com/MahdiIranNezhad/UFS.git"
```

---

## Usage
### Start the service
```csharp
UFS.UnityForegroundService.Start(); /// for example on `Awake()` in the first class.
```

### Stop the service
```csharp
UFS.UnityForegroundService.Stop(); /// for example on the accessable `OnApplicationQuit()` method.
```

### Update the notification text
```csharp
UFS.UnityForegroundService.Update(
    "Online Match",
    "Waiting for opponent..."
);
```

---

## Runtime Notification Behavior
The service shows a required foreground notification.

Default values:
- Title: `Connection active`
- Message: `Keeping connection alive`

Using `Update()` allows runtime changes, including localized strings.

---

## Android Permissions
Declared in the package’s manifest:
```xml
<uses-permission android:name="android.permission.FOREGROUND_SERVICE" />
<uses-permission android:name="android.permission.FOREGROUND_SERVICE_DATA_SYNC" />
<uses-permission android:name="android.permission.FOREGROUND_SERVICE_REMOTE_MESSAGING" />
```
Required on Android 12+ and Android 14+ for networking while backgrounded.

---

## ProGuard / R8 Integration
`proguard.txt` inside the androidlib ensures the service is not removed:
```
-keep class com.unity.extension.** { *; }
-keep class com.unity.extension.UnityForegroundService { *; }
```
No additional project setup required.

---

## Lifecycle Behavior
### When the app goes to background:
- Service stays alive
- Network stays alive
- Notification remains visible

### When the user swipes the app away:
The service is stopped automatically.

### When Unity quits:
`UnityForegroundService.Stop()` sends a STOP action to the service.

---

## Why This Package Matters
Android’s modern background policies kill background tasks aggressively. If your game uses:
- SmartFoxServer
- WebSockets
- Long‑running TCP connections
- Realtime multiplayer
- Background data sync

…you **must** use a foreground service to remain stable.

This package provides a clean, reusable, UPM‑friendly solution used across all projects without touching platform folders.

---

## Contributions
PRs and improvements are welcome.


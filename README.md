# Unity Foreground Service for bounded Android work

This UPM package starts a bounded Android foreground service using the `shortService`
foreground-service type. It is intended for a brief background operation, such as
asking the consuming Unity game to close a SmartFox connection cleanly after the app
is paused. It is not a socket-keepalive service and cannot keep a game connected
indefinitely.

The repository contains the Android library and Unity runtime wrapper, but it does
not contain a SmartFox client or a Unity game. The consuming game must register its
real disconnect implementation through the documented adapter contract below.

## Requirements

- Unity 2022.3 or newer.
- Android library compile/target SDK 36. The consuming project must be able to merge
  the package manifest and should use target SDK 34 or newer when testing `shortService`.
- UniTask 2.3.1, already declared by `package.json`.
- Android permission `FOREGROUND_SERVICE`. `shortService` does not require a
  type-specific foreground-service permission.

Android 12/API 31 and newer restrict foreground-service starts while the app is in
the background. This package requests the service from Unity's pause transition and
catches a rejected start; an Android exception or a device policy can still reject
the request. A request being issued is not reported as a running service: use
`UnityForegroundService.IsRunning` after native confirmation.

## Lifecycle

`UFSShortTaskController` is created automatically before the first scene and survives
normal scene changes. It performs this state transition:

1. `OnApplicationPause(true)` generates a process-unique task ID (`Guid`), requests
   one short task, and shows the existing low-importance
   foreground notification.
2. The native service starts an independent monotonic-deadline timer. The default
   deadline is 120 seconds; values are clamped to 1–150 seconds. Android documents
   `shortService` as approximately three minutes, so the package reserves time for
   callback delivery and cleanup.
3. At the deadline Java calls
   `UnityPlayer.UnitySendMessage("UFSShortTaskController", ... )` with the task ID.
   Unity ignores a stale ID or a duplicate callback.
4. The registered synchronous SmartFox handler checks the actual client, requests
   its real disconnect operation, and returns `true` only after confirmation or when
   already disconnected.
5. Only then does Unity send the task-specific acknowledgement. Java verifies the
   task ID, removes the notification, and stops the service.
6. If Unity is suspended, killed, unavailable, or the handler does not acknowledge
   within five seconds of the native deadline, Java stops the service. This fallback
   is bounded cleanup only; it does not prove that SmartFox completed a protocol-level
   disconnect.
7. `OnApplicationPause(false)` cancels the active task and removes the service. A
   resume race cannot acknowledge a task that is no longer current.

Repeated pause events are idempotent. A repeated start for the same task does not
reset the deadline, and a different overlapping task is rejected. The service uses
`START_NOT_STICKY`, cancels timers during destruction, and handles Android API 34's
`onTimeout(int)` and API 35+'s typed `onTimeout(int, int)` by stopping immediately.

The manifest sets `android:stopWithTask="true"`. Swiping/removing the application
task therefore deliberately cancels the bounded operation and removes the
notification; this is not a mechanism for surviving task removal.

## Public Unity API

Existing entry points remain available, but `Start()` and `Start(title, message)` now
start the same bounded 120-second operation instead of an unbounded service:

```csharp
UFS.UnityForegroundService.Start();
UFS.UnityForegroundService.Start("Match cleanup", "Closing the connection...");
UFS.UnityForegroundService.Update("Match cleanup", "Still closing...");
UFS.UnityForegroundService.Stop();

UFS.UnityForegroundService.StartShortTask(
    taskId: "game-generated-task-id",
    title: "Match cleanup",
    message: "Closing the connection...",
    deadlineSeconds: 120);
```

`StartShortTask` returns `ShortTaskStartResult`. `Requested` only means Android
accepted the start request; `IsRunning` becomes true only after the Java service
reports its active state. `Rejected`, `AlreadyRequested`, and `AlreadyRunning` are
explicit outcomes. `CancelShortTask(taskId)` is idempotent. The controller normally
owns these calls; direct callers must preserve task IDs and acknowledgement rules.

Notification defaults are `Connection active` and `Finishing background operation`.
For lifecycle-driven use, configure them with:

```csharp
UFS.UFSShortTaskController.Configure(
    "Match cleanup",
    "Closing the connection...",
    deadlineSeconds: 120);
```

## SmartFox integration contract

No SmartFox implementation is fabricated in this package. Register a delegate that
uses the SDK instance owned by the consuming game:

```csharp
using System;
using UFS;

// The adapter owns the real SDK-specific event subscription and disconnect call.
// It must return true if the client is already disconnected or after the SDK's
// confirmed disconnect event; return false for an explicit failure.
public static class GameSmartFoxIntegration
{
    public static void Install(IActualSmartFoxAdapter adapter)
    {
        UFSShortTaskController.RegisterSmartFoxDisconnectHandler(
            taskId => adapter.DisconnectSynchronously(taskId));
    }
}

public interface IActualSmartFoxAdapter
{
    bool DisconnectSynchronously(string taskId);
}
```

`IActualSmartFoxAdapter` is intentionally an application-owned seam: implement it
with the installed SmartFox SDK's real `IsConnected`/disconnect APIs. Do not call
the Unity wrapper's acknowledgement from the adapter. The controller performs that
only after the delegate returns `true`. Because this contract is synchronous, the
adapter must not block the Unity main thread; the native fallback remains available
if Unity cannot return to the callback path.

## Android configuration

The package manifest declares only:

```xml
<uses-permission android:name="android.permission.FOREGROUND_SERVICE" />
...
android:foregroundServiceType="shortService"
android:stopWithTask="true"
```

The old `FOREGROUND_SERVICE_SPECIAL_USE` permission and
`PROPERTY_SPECIAL_USE_FGS_SUBTYPE` property are intentionally removed. The service
class remains `com.unity.extension.UnityForegroundService`; the notification channel
ID and notification ID remain compatible with the previous package.

`shortService` has no type-specific permission, no sticky restart behavior, and no
guarantee of background network continuity. On API 34+ the Java service calls
`startForeground(..., FOREGROUND_SERVICE_TYPE_SHORT_SERVICE)`. On older supported
versions it uses the two-argument API and still enforces the package's own timer,
because the Android timeout callback does not exist on API 33 and lower.

After importing the package, inspect the consuming Unity/Gradle merged manifest and
confirm that another plugin has not reintroduced a conflicting service declaration or
removed `FOREGROUND_SERVICE`. This repository cannot produce a consuming Unity
merged manifest by itself.

## Installation and upgrade

Add the package through UPM, for example:

```json
"com.unity.extension.unityforegroundservice":
  "https://github.com/MahdiIranNezhad/UFS.git"
```

After upgrading from the old special-use release:

1. Remove any project-level copy of the old UFS service or special-use properties.
2. Rebuild the Android player so the package manifest is merged again.
3. Register the SmartFox adapter during application initialization.
4. Verify notification permission/channel behavior on the target device and exercise
   the manual plan in `Documentation/SHORT_SERVICE_DEVICE_TEST_PLAN.md`.

## Verification and limitations

The repository includes `Tests/Verify-UFS.ps1` for source, XML, manifest, API, and
package-structure checks. A Unity editor/player build and a physical API 33/34/35
device run require a consuming Unity project and devices; they are not present in
this package repository. The device test plan records the required logcat evidence.

Android may reject a start from a background state (`ForegroundServiceStartNotAllowedException`),
kill the process, or apply vendor-specific battery policy. A native fallback can
remove the foreground service before the system's short-service ANR window where
scheduled execution permits, but it cannot guarantee SmartFox server-side receipt of
a disconnect. This feature is a bounded shutdown aid, not a reconnection or
continuous-connectivity guarantee.

Official Android references:

- [Foreground service types: short service](https://developer.android.com/develop/background-work/services/fgs/service-types#short-service)
- [Foreground service API reference](https://developer.android.com/reference/android/app/Service#onTimeout(int))
- [Foreground-service launch restrictions](https://developer.android.com/develop/background-work/services/fgs/restrictions-bg-start)

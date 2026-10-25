# `shortService` device test plan

Run these tests in a consuming Unity player with a real SmartFox adapter registered.
Use release and development builds as appropriate, capture `adb logcat -v threadtime`
for the package process, and record device model, Android API, target SDK, result,
and timestamps.

## Matrix

Run the matrix on API 33, API 34, and API 35 or newer where available. API 34 checks
the one-argument short-service timeout callback; API 35 checks the typed timeout
callback. API 33 checks the package's own fallback because Android does not provide
the short-service callback there.

## Cases

1. While the Unity activity is visible, connect SmartFox, background the app, and
   verify the notification, `Requested` then `IsRunning`, task ID, and no duplicate
   service start.
2. Resume before the deadline. Verify cancellation, notification removal, no
   disconnect callback, and `IsRunning == false`.
3. Let the deadline expire while Unity remains responsive. Verify exactly one native
   deadline callback, one real SDK disconnect attempt, a confirmed completion, and
   notification/service cleanup.
4. Use an adapter that returns `false` and one that never completes. Verify an
   explicit failure/timeout log and native service cleanup after the fallback window;
   do not treat this as proof of a SmartFox protocol disconnect.
5. Prevent Unity callback delivery (force-stop the Unity process or use a controlled
   callback failure) and verify native cleanup before Android's short-service timeout
   where scheduling permits.
6. Deliver repeated pause/resume events and duplicate/stale task IDs. Verify the
   active task is not extended, stale acknowledgements are ignored, and only one
   disconnect attempt is made.
7. Exercise Android's system timeout with a test-only deadline configuration that
   leaves the service running long enough for the platform callback. Verify immediate
   service stop, notification removal, and the expected API-specific callback log.
8. Attempt a start after the app is already backgrounded or under a restricted OEM
   policy. Verify the Android exception is logged, no false running state is exposed,
   and the game remains usable.
9. Swipe the app task away. Verify the deliberate `stopWithTask=true` behavior and
   notification cleanup.

## Evidence rules

Source checks and local Java compilation do not prove Unity lifecycle behavior,
SmartFox completion, merged-manifest behavior, or device policy behavior. Record
those separately. Do not claim a device/API case passed without its logcat and
device metadata.

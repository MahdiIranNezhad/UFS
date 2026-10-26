$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$manifestPath = Join-Path $root 'Android/UnityForegroundService.androidlib/src/main/AndroidManifest.xml'
$javaPath = Join-Path $root 'Android/UnityForegroundService.androidlib/src/main/java/com/unity/extension/UnityForegroundService.java'
$servicePath = Join-Path $root 'Runtime/UnityForegroundService.cs'
$controllerPath = Join-Path $root 'Runtime/UFSShortTaskController.cs'
$packagePath = Join-Path $root 'package.json'

foreach ($path in @($manifestPath, $javaPath, $servicePath, $controllerPath, $packagePath)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing required file: $path" }
}

$java = Get-Content -LiteralPath $javaPath -Raw
$service = Get-Content -LiteralPath $servicePath -Raw
$controller = Get-Content -LiteralPath $controllerPath -Raw
$manifest = [xml](Get-Content -LiteralPath $manifestPath -Raw)
$serviceNode = $manifest.manifest.application.service

if ([string]::IsNullOrWhiteSpace($java) -or $java.Length -lt 1000) { throw 'Java service is empty or unexpectedly small' }
if ($serviceNode.foregroundServiceType -ne 'shortService') { throw 'Manifest service type is not shortService' }
if ($manifest.manifest.application.service.stopWithTask -ne 'true') { throw 'Manifest stopWithTask policy is missing' }
if ($manifest.OuterXml -match 'FOREGROUND_SERVICE_SPECIAL_USE|PROPERTY_SPECIAL_USE_FGS_SUBTYPE|specialUse') { throw 'Obsolete special-use declaration remains' }
if ($manifest.manifest.'uses-permission'.name -notcontains 'android.permission.FOREGROUND_SERVICE') { throw 'FOREGROUND_SERVICE permission is missing' }

foreach ($token in @(
    'ACTION_START_SHORT_TASK', 'ACTION_CANCEL_SHORT_TASK', 'ACTION_DISCONNECT_CONFIRMED',
    'FOREGROUND_SERVICE_TYPE_SHORT_SERVICE', 'DEFAULT_DEADLINE_MILLIS',
    'MAX_DEADLINE_MILLIS', 'START_NOT_STICKY', 'onTimeout(int startId)',
    'onTimeout(int startId, int fgsType)', 'UnitySendMessage', 'stopForegroundSafely')) {
    if ($java -notmatch [regex]::Escape($token)) { throw "Java token missing: $token" }
}

foreach ($token in @('Start()', 'Start(string title, string message)', 'Update(string title, string message)', 'Stop()', 'IsRunning', 'StartShortTask', 'CancelShortTask', 'StopShortTaskAfterDisconnect')) {
    if ($service -notmatch [regex]::Escape($token)) { throw "C# wrapper API missing: $token" }
}

foreach ($token in @('RuntimeInitializeOnLoadMethod', 'OnApplicationPause', 'OnNativeShortTaskDeadline', 'RegisterSmartFoxDisconnectHandler', 'StopShortTaskAfterDisconnect', 'Func<string, bool>', 'Guid.NewGuid')) {
    if ($controller -notmatch [regex]::Escape($token)) { throw "Controller token missing: $token" }
}

$package = Get-Content -LiteralPath $packagePath -Raw | ConvertFrom-Json
if ($package.unity -ne '2022.3') { throw 'Unexpected Unity package version constraint' }
if (-not $package.dependencies.'com.cysharp.unitask') { throw 'UniTask package dependency is missing' }

Write-Output 'UFS source and manifest checks passed.'

#requires -Version 7.0
[CmdletBinding()]
param(
    [switch]$VerifyOnly,
    [string]$ApiUrl,
    [string]$CapturePath,
    [ValidateRange(1024,65535)][int]$WorkerPort = 17865
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = $PSScriptRoot
$manifest = Get-Content -LiteralPath "$root/bundle.json" -Raw | ConvertFrom-Json -AsHashtable
if ($manifest.schema -ne 1 -or $manifest.kind -ne 'local-only') { throw 'Unsupported bundle manifest' }
foreach ($required in @('hud/OWMetaHUD.exe','hud/OWMetaHUD.dll','worker/OwmetaVisionWorker.exe','worker/Owmeta.Vision.dll','worker/model-pack/playtest-pack.json')) {
    if (!$manifest.checksums.ContainsKey($required)) { throw "Incomplete bundle: $required" }
}
foreach ($file in $manifest.checksums.Keys) {
    $path = [IO.Path]::GetFullPath((Join-Path $root $file))
    if (!$path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid bundle path' }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $manifest.checksums[$file]) { throw "Bundle checksum mismatch: $file" }
}
if ($VerifyOnly) { Write-Output 'Bundle checksums verified; no processes started.'; return }
$uri = $null
if (![Uri]::TryCreate($ApiUrl, [UriKind]::Absolute, [ref]$uri) -or !$uri.IsLoopback -or $uri.Scheme -notin @('http','https') -or $uri.UserInfo -or $uri.Query -or $uri.Fragment) {
    throw 'Supply -ApiUrl for the matching LOCAL backend (for example http://127.0.0.1:8001). This launcher never targets production.'
}
if (!$CapturePath) { throw 'Supply a local -CapturePath for logs/screenshots (kept outside the bundle).' }
$captures = [IO.Path]::GetFullPath($CapturePath)
if ($captures -eq $root -or $captures.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Captures must be outside the bundle' }
if (Get-Process -Name OWMetaHUD -ErrorAction SilentlyContinue) { throw 'A HUD is already running; existing session left untouched.' }
$workerUrl = "http://127.0.0.1:$WorkerPort"
function Get-Health { try { Invoke-RestMethod "$workerUrl/health" -TimeoutSec 2 } catch { $null } }
function Test-Health($health) {
    return $health -and $health.ready -eq $true -and $health.schema -eq 1 -and $health.legacyFallback -eq $false `
        -and $health.packId -eq $manifest.packId -and $health.workerBuildId -eq $manifest.workerBuildId -and $health.hostBuildId -eq $manifest.hostBuildId
}
$health = Get-Health
if ($health -and !(Test-Health $health)) { throw 'A different worker owns this port; existing process left untouched.' }
if (!$health -and (Get-NetTCPConnection -State Listen -LocalPort $WorkerPort -ErrorAction SilentlyContinue)) { throw 'Port is already occupied; existing service left untouched.' }
New-Item -ItemType Directory -Path $captures -Force | Out-Null
$ownedWorker = $null
$hud = $null
$saved = @{}
foreach ($name in @('OWMETA_VISION_PACK','OWMETA_VISION_PORT','OWMETA_CAPTURE_DIR')) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
try {
    $env:OWMETA_VISION_PACK = "$root/worker/model-pack"
    $env:OWMETA_VISION_PORT = "$WorkerPort"
    $env:OWMETA_CAPTURE_DIR = $captures
    if (!$health) {
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
        $ownedWorker = Start-Process -FilePath "$root/worker/OwmetaVisionWorker.exe" -WorkingDirectory "$root/worker" -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput "$captures/worker-$stamp.stdout.log" -RedirectStandardError "$captures/worker-$stamp.stderr.log"
        for ($attempt=0; $attempt -lt 60; $attempt++) {
            if ($ownedWorker.HasExited) { throw 'Worker startup failed; inspect local logs' }
            $health = Get-Health
            if ($health) { break }
            Start-Sleep -Milliseconds 200
        }
        if (!(Test-Health $health)) { throw 'Worker did not become ready with the bundled model/build' }
    }
    # Explicit interactive launch; worker alone stays hidden.
    $hud = Start-Process -FilePath "$root/hud/OWMetaHUD.exe" -WorkingDirectory "$root/hud" -ArgumentList "--api-url=$($uri.AbsoluteUri)" -PassThru
    Write-Output "HUD $($hud.Id), worker $workerUrl, captures $captures. Close the HUD to end this session."
    $hud.WaitForExit()
} finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    if ($ownedWorker -and !$ownedWorker.HasExited) { $ownedWorker.Kill(); $ownedWorker.WaitForExit() }
}

#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BundlePath,
    [Parameter(Mandatory)][string]$PreparedRequest,
    [ValidateRange(1024,65535)][int]$WorkerPort = 17866
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$bundle = (Resolve-Path -LiteralPath $BundlePath).Path
$request = (Resolve-Path -LiteralPath $PreparedRequest).Path
$manifest = Get-Content -LiteralPath "$bundle/bundle.json" -Raw | ConvertFrom-Json
& "$bundle/Start-LocalBundle.ps1" -VerifyOnly
$rejected = $false
try { & "$bundle/Start-LocalBundle.ps1" -ApiUrl https://api.owmeta.io -CapturePath "$env:TEMP/unused-owmeta-captures" }
catch { if ($_.Exception.Message -notlike '*LOCAL backend*') { throw }; $rejected = $true }
if (!$rejected) { throw 'Production URL was not rejected' }
# Check corrupt bundles without touching the real model or packaged originals.
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('owmeta-bundle-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
Copy-Item -LiteralPath "$bundle/Start-LocalBundle.ps1" -Destination "$fixture/Start-LocalBundle.ps1"
$fake = @{schema=1;kind='local-only';checksums=[ordered]@{}}
foreach ($file in @('hud/OWMetaHUD.exe','hud/OWMetaHUD.dll','worker/OwmetaVisionWorker.exe','worker/Owmeta.Vision.dll','worker/model-pack/playtest-pack.json')) {
    New-Item -ItemType Directory -Path (Split-Path "$fixture/$file") -Force | Out-Null
    [IO.File]::WriteAllText("$fixture/$file", 'test fixture, not executable')
    $fake.checksums[$file] = ('0' * 64)
}
$fake | ConvertTo-Json -Depth 5 | Set-Content "$fixture/bundle.json"
$rejected = $false
try { & "$fixture/Start-LocalBundle.ps1" -VerifyOnly }
catch { if ($_.Exception.Message -notlike '*checksum mismatch*') { throw }; $rejected = $true }
if (!$rejected) { throw 'Corrupt bundle was not rejected' }
if (Get-NetTCPConnection -State Listen -LocalPort $WorkerPort -ErrorAction SilentlyContinue) { throw 'Smoke-test port occupied; existing service left untouched' }
$savedPack = $env:OWMETA_VISION_PACK
$savedPort = $env:OWMETA_VISION_PORT
$ownedWorker = $null
try {
    $env:OWMETA_VISION_PACK = "$bundle/worker/model-pack"
    $env:OWMETA_VISION_PORT = "$WorkerPort"
    $ownedWorker = Start-Process -FilePath "$bundle/worker/OwmetaVisionWorker.exe" -WorkingDirectory "$bundle/worker" -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput "$fixture/worker.stdout.log" -RedirectStandardError "$fixture/worker.stderr.log"
    $health = $null
    $url = "http://127.0.0.1:$WorkerPort"
    for ($attempt=0; $attempt -lt 60; $attempt++) {
        if ($ownedWorker.HasExited) { throw "Packaged worker exited; see $fixture" }
        try { $health = Invoke-RestMethod "$url/health" -TimeoutSec 1; break } catch { Start-Sleep -Milliseconds 200 }
    }
    if (!$health -or !$health.ready -or $health.packId -ne $manifest.packId -or $health.workerBuildId -ne $manifest.workerBuildId -or $health.hostBuildId -ne $manifest.hostBuildId -or $health.legacyFallback) {
        throw 'Packaged worker health/build mismatch'
    }
    for ($attempt=0; $attempt -lt 2; $attempt++) {
        $result = Invoke-RestMethod "$url/recognize" -Method Post -ContentType application/json -InFile $request -TimeoutSec 2
        if (!$result.valid -or $result.slots.Count -ne 10 -or $result.packId -ne $manifest.packId) { throw 'Prepared supported regression frame failed' }
        Write-Output "Replay $attempt passed: identity $($result.identityMs) ms; worker $($result.workerMs) ms (not an accuracy measurement)."
    }
    $invalid = Invoke-WebRequest "$url/recognize" -Method Post -ContentType application/json -Body '{}' -SkipHttpErrorCheck -TimeoutSec 2
    if ($invalid.StatusCode -ne 400) { throw 'Malformed prepared frame did not return 400' }
    Write-Output "PASS: checksums, production guard, corruption rejection, native model/OCR warmup, repeated requests, malformed input. Logs: $fixture"
} finally {
    $env:OWMETA_VISION_PACK = $savedPack
    $env:OWMETA_VISION_PORT = $savedPort
    if ($ownedWorker -and !$ownedWorker.HasExited) { $ownedWorker.Kill(); $ownedWorker.WaitForExit() }
}

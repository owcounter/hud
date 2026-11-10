#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OwmetaSourceRoot,
    [Parameter(Mandatory)][string]$PackPath,
    [Parameter(Mandatory)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$hudRoot = Split-Path $PSScriptRoot
$sourceRoot = (Resolve-Path -LiteralPath $OwmetaSourceRoot).Path
$packRoot = (Resolve-Path -LiteralPath $PackPath).Path
$outputRoot = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $outputRoot) { throw 'Output already exists; choose a new directory. Nothing was overwritten.' }
if ($outputRoot.StartsWith($hudRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Choose output outside the HUD source tree so generated build files cannot enter its default compile glob.'
}
foreach ($project in @('Owmeta.Vision.Preparation', 'Owmeta.Vision.Worker')) {
    if (!(Test-Path -LiteralPath "$sourceRoot/csharp/$project/$project.csproj")) { throw "Missing matching source project: $project" }
}
# The HUD compiles synced copies of preparation/protocol; the bundled worker must speak the same version.
& pwsh -NoProfile -File "$sourceRoot/tools/portrait-prototype/sync-hud-vision.ps1" -HudRoot $hudRoot -Check
if ($LASTEXITCODE -ne 0) { throw 'HUD Vision/ sources differ from the worker sources; run sync-hud-vision.ps1 first.' }
$packFiles = @('manifest.json', 'gallery.bin', 'encoder.onnx', 'roles.bin', 'catalog.json', 'tessdata/eng.traineddata')
$metadata = Get-Content -LiteralPath "$packRoot/playtest-pack.json" -Raw | ConvertFrom-Json -AsHashtable
if ($metadata.schema -ne 1 -or $metadata.checksums.Count -ne $packFiles.Count) { throw 'Unsupported model pack' }
foreach ($file in $packFiles) {
    if (!$metadata.checksums.ContainsKey($file) -or (Get-FileHash -LiteralPath "$packRoot/$file" -Algorithm SHA256).Hash -ne $metadata.checksums[$file]) {
        throw "Model checksum mismatch: $file"
    }
}
# Copy only the published runtime and an explicit model allowlist, never a repo/bin
# directory which could contain OAuth tokens, screenshots or training outputs.
New-Item -ItemType Directory -Path $outputRoot | Out-Null
$stage = Join-Path $outputRoot '.build'
$bundle = Join-Path $outputRoot 'bundle'
New-Item -ItemType Directory -Path $bundle | Out-Null
foreach ($entry in @(
    @{ Project="$hudRoot/OwmetaHUD.csproj"; Destination='hud' },
    @{ Project="$sourceRoot/csharp/Owmeta.Vision.Worker/Owmeta.Vision.Worker.csproj"; Destination='worker' }
)) {
    & dotnet publish $entry.Project -c Release -r win-x64 --self-contained true --nologo -v minimal `
        "-p:OwmetaSourceRoot=$sourceRoot" "-p:ArtifactsPath=$stage" -o "$bundle/$($entry.Destination)"
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($entry.Destination). Partial output retained for diagnosis." }
}
$packDestination = "$bundle/worker/model-pack"
New-Item -ItemType Directory -Path "$packDestination/tessdata" -Force | Out-Null
foreach ($file in ($packFiles + 'playtest-pack.json')) {
    Copy-Item -LiteralPath "$packRoot/$file" -Destination "$packDestination/$file"
}
Copy-Item -LiteralPath "$PSScriptRoot/Start-LocalBundle.ps1" -Destination "$bundle/Start-LocalBundle.ps1"
Copy-Item -LiteralPath "$PSScriptRoot/LOCAL-BUNDLE.md" -Destination "$bundle/README.md"
function Get-BuildId([string]$path) {
    $stream = [IO.File]::OpenRead($path)
    $reader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $md = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($reader)
        return $md.GetGuid($md.GetModuleDefinition().Mvid).ToString('N')
    } finally { $reader.Dispose(); $stream.Dispose() }
}
$checksums = [ordered]@{}
foreach ($file in (Get-ChildItem -LiteralPath $bundle -File -Recurse | Sort-Object FullName)) {
    $relative = [IO.Path]::GetRelativePath($bundle, $file.FullName).Replace('\', '/')
    if ($relative -match '(?i)(^|/)([^/]*oauth[^/]*\.json|\.env[^/]*|source\.(png|jpg)|recognition-captures)(/|$)') { throw "Unexpected private artifact in publish: $relative" }
    $checksums[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
$manifest = [ordered]@{
    schema=1; kind='local-only'; runtime='win-x64-self-contained'; createdUtc=[DateTime]::UtcNow.ToString('o')
    packId=(Get-FileHash -LiteralPath "$packDestination/playtest-pack.json" -Algorithm SHA256).Hash.ToLowerInvariant()
    workerBuildId=(Get-BuildId "$bundle/worker/Owmeta.Vision.dll")
    hostBuildId=(Get-BuildId "$bundle/worker/OwmetaVisionWorker.dll")
    hudBuildId=(Get-BuildId "$bundle/hud/OWMetaHUD.dll")
    checksums=$checksums
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$bundle/bundle.json" -Encoding utf8
& "$bundle/Start-LocalBundle.ps1" -VerifyOnly
if (!$?) { throw 'Bundle verification failed' }
Compress-Archive -LiteralPath $bundle -DestinationPath "$outputRoot/owmeta-local-win-x64.zip" -CompressionLevel Fastest
Write-Output "Local bundle ready: $bundle"
Write-Output 'No upload, deployment or application launch was performed.'

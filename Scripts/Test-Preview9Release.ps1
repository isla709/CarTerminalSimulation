param([string]$ReleaseDirectory = 'artifacts/publish/preview9-20261008.1')

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repoDirectory = Split-Path -Parent $PSScriptRoot
$release = [IO.Path]::GetFullPath((Join-Path $repoDirectory $ReleaseDirectory))
$manifest = Get-Content -LiteralPath (Join-Path $release 'update-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$zipPath = Join-Path $release $manifest.package.fileName
if ($manifest.version -ne 'preview9-build.20261008.1' -or $manifest.channel -ne 'preview' -or $manifest.line -ne 'main' -or $manifest.compatibilityEpoch -ne 1) {
    throw 'Release version/channel/line/compatibility metadata mismatch.'
}
if ((Get-Item -LiteralPath $zipPath).Length -ne $manifest.package.size -or
    (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.package.sha256) {
    throw 'ZIP integrity check failed.'
}
$qaDirectory = Join-Path $repoDirectory ('artifacts/.qa/preview9-release-' + [Guid]::NewGuid().ToString('N'))
[IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $qaDirectory)
$dllText = [Text.Encoding]::Unicode.GetString([IO.File]::ReadAllBytes((Join-Path $qaDirectory 'TerminalSimulation.Wpf.dll')))
if (-not $dllText.Contains($manifest.version)) { throw 'Application embedded version does not match manifest.' }
if ([Reflection.AssemblyName]::GetAssemblyName((Join-Path $qaDirectory 'System.Text.Json.dll')).Version.Major -lt 9) {
    throw 'Release contains an incompatible System.Text.Json assembly.'
}
foreach ($executable in @('TerminalSimulation.Wpf', 'update')) {
    $runtime = Get-Content -LiteralPath (Join-Path $qaDirectory "$executable.runtimeconfig.json") -Raw | ConvertFrom-Json
    if (@($runtime.runtimeOptions.includedFrameworks).Count -ne 2) { throw "$executable is not self-contained." }
}
if (@(Get-ChildItem -LiteralPath $qaDirectory -Recurse -Filter '*.resources.dll').Count -gt 0) {
    throw 'Language satellite resources remain in ZIP.'
}
foreach ($privateFile in @('config.json','utility_settings.json','update-sources.json','KEY.txt','Workspaces','.claude')) {
    if (Test-Path -LiteralPath (Join-Path $qaDirectory $privateFile)) { throw "Unexpected user data: $privateFile" }
}
$process = Start-Process -FilePath (Join-Path $qaDirectory 'TerminalSimulation.Wpf.exe') -WorkingDirectory $qaDirectory -WindowStyle Hidden -PassThru
try {
    Start-Sleep -Seconds 10
    $process.Refresh()
    if ($process.HasExited) { throw "Application exited during startup: $($process.ExitCode)" }
    if (Test-Path -LiteralPath (Join-Path $qaDirectory 'startup-crash.log')) { throw 'Application reported a startup crash.' }
    if (-not (Test-Path -LiteralPath (Join-Path $qaDirectory 'config.json'))) { throw 'Application did not finish configuration initialization.' }
    $config = Get-Content -LiteralPath (Join-Path $qaDirectory 'config.json') -Raw | ConvertFrom-Json
    if ($config.MapProvider -ne 'new') { throw 'Clean startup did not use the new map by default.' }
    Write-Output "Startup smoke passed; clean ZIP extracted to $qaDirectory"
}
finally {
    $process.Refresh()
    if (-not $process.HasExited) {
        [void]$process.CloseMainWindow()
        if (-not $process.WaitForExit(8000)) { Stop-Process -Id $process.Id -Force }
    }
}
Write-Output "Verified version: $($manifest.version)"
Write-Output "Package bytes: $($manifest.package.size)"
Write-Output "SHA-256: $($manifest.package.sha256)"

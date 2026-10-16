param(
    [string]$ReleaseDirectory = 'artifacts/publish/preview10-20261014.1',
    [string]$ExpectedVersion = 'preview10-build.20261014.1'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repoDirectory = Split-Path -Parent $PSScriptRoot
$release = [IO.Path]::GetFullPath((Join-Path $repoDirectory $ReleaseDirectory))
$manifest = Get-Content -LiteralPath (Join-Path $release 'update-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$zipPath = Join-Path $release $manifest.package.fileName
if ($manifest.product -ne 'TerminalSimulation' -or $manifest.version -ne $ExpectedVersion -or $manifest.channel -ne 'preview' -or $manifest.line -ne 'main' -or $manifest.compatibilityEpoch -ne 1) {
    throw 'Release metadata mismatch.'
}
if ((Get-Item -LiteralPath $zipPath).Length -ne $manifest.package.size -or (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.package.sha256) {
    throw 'ZIP integrity check failed.'
}
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName -match '(^|/)(config\.json|utility_settings\.json|update-sources\.json|KEY\.txt|Workspaces|\.claude|bin|obj)(/|$)|XunjieCloud|\.resources\.dll$') {
            throw "Private data or unused resource in ZIP: $($entry.FullName)"
        }
    }
} finally { $archive.Dispose() }
$qaDirectory = Join-Path $repoDirectory ('artifacts/.qa/preview10-release-' + [Guid]::NewGuid().ToString('N'))
[IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $qaDirectory)
$dllText = [Text.Encoding]::Unicode.GetString([IO.File]::ReadAllBytes((Join-Path $qaDirectory 'TerminalSimulation.Wpf.dll')))
if (-not $dllText.Contains($ExpectedVersion)) { throw 'Embedded application version does not match manifest.' }
if ([Reflection.AssemblyName]::GetAssemblyName((Join-Path $qaDirectory 'System.Text.Json.dll')).Version.Major -lt 9) { throw 'System.Text.Json dependency mismatch.' }
foreach ($executable in @('TerminalSimulation.Wpf','update')) {
    $runtime = Get-Content -LiteralPath (Join-Path $qaDirectory "$executable.runtimeconfig.json") -Raw | ConvertFrom-Json
    if (@($runtime.runtimeOptions.includedFrameworks).Count -ne 2) { throw "$executable is not self-contained." }
}
foreach ($frontend in @('MapApp','MapAppOld')) {
    foreach ($file in @('index.html','app.css','app.js','vehicle.css','vehicle.js')) {
        $relative = Join-Path $frontend $file
        $actual = Join-Path $qaDirectory $relative
        $source = Join-Path $repoDirectory (Join-Path 'TerminalSimulation.Wpf' $relative)
        if (-not (Test-Path -LiteralPath $actual) -or (Get-FileHash -LiteralPath $actual).Hash -ne (Get-FileHash -LiteralPath $source).Hash) {
            throw "Missing or stale map resource: $relative"
        }
    }
}
# No automatic update requests from test instances; never alter user data.
[IO.File]::WriteAllText((Join-Path $qaDirectory 'update-sources.json'), '{"schemaVersion":3,"autoCheck":false,"channel":"preview","line":"main","sources":[]}', [Text.UTF8Encoding]::new($false))
foreach ($mode in @('new','old')) {
    if ($mode -eq 'old') {
        $config = Get-Content -LiteralPath (Join-Path $qaDirectory 'config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $config.MapProvider = 'old'
        [IO.File]::WriteAllText((Join-Path $qaDirectory 'config.json'), ($config | ConvertTo-Json -Depth 50), [Text.UTF8Encoding]::new($false))
    }
    $process = Start-Process -FilePath (Join-Path $qaDirectory 'TerminalSimulation.Wpf.exe') -WorkingDirectory $qaDirectory -WindowStyle Hidden -PassThru
    try {
        Start-Sleep -Seconds 10
        $process.Refresh()
        if ($process.HasExited) { throw "Application exited during $mode startup: $($process.ExitCode)" }
        if (-not $process.Responding -or $process.MainWindowHandle -eq [IntPtr]::Zero) { throw 'Application window did not initialize.' }
        if (Test-Path -LiteralPath (Join-Path $qaDirectory 'startup-crash.log')) { throw 'Application reported a startup crash.' }
        $config = Get-Content -LiteralPath (Join-Path $qaDirectory 'config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($config.MapProvider -ne $mode) { throw 'Map selection was not preserved.' }
        Write-Output "Startup passed: $mode; responding=$($process.Responding)"
    } finally {
        $process.Refresh()
        if (-not $process.HasExited) {
            [void]$process.CloseMainWindow()
            if (-not $process.WaitForExit(8000)) { Stop-Process -Id $process.Id -Force }
        }
    }
}
# Exercise the real bootstrap (without --worker). An intentionally invalid plan
# must reach managed validation in the relocated worker, never an install step.
$planPath = Join-Path $qaDirectory 'bootstrap-test-plan.json'
[IO.File]::WriteAllText($planPath, '{"SchemaVersion":0}', [Text.UTF8Encoding]::new($false))
$emptyDotnet = Join-Path $qaDirectory 'empty-dotnet'
[void](New-Item -ItemType Directory -Path $emptyDotnet)
$hostTrace = Join-Path $qaDirectory 'bootstrap-host-trace.log'
$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = Join-Path $qaDirectory 'update.exe'
$startInfo.Arguments = '--plan "' + $planPath + '"'
$startInfo.WorkingDirectory = $qaDirectory
$startInfo.UseShellExecute = $false
$startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$startInfo.EnvironmentVariables['DOTNET_ROOT'] = $emptyDotnet
$startInfo.EnvironmentVariables['DOTNET_ROOT_X64'] = $emptyDotnet
$startInfo.EnvironmentVariables['COREHOST_TRACE'] = '1'
$startInfo.EnvironmentVariables['COREHOST_TRACEFILE'] = $hostTrace
$bootstrap = [Diagnostics.Process]::Start($startInfo)
$workerProcess = $null
try {
    if (-not $bootstrap.WaitForExit(20000)) { throw 'Updater bootstrap did not exit.' }
    if ($bootstrap.ExitCode -ne 0) { throw "Updater bootstrap failed: $($bootstrap.ExitCode)" }
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $workerInfo = Get-CimInstance Win32_Process -Filter "Name='update.exe'" | Where-Object {
            $_.CommandLine -and $_.CommandLine.Contains($planPath) -and $_.CommandLine.Contains('--worker')
        } | Select-Object -First 1
        if ($workerInfo) { $workerProcess = [Diagnostics.Process]::GetProcessById($workerInfo.ProcessId) }
        $validationLog = Join-Path $qaDirectory 'update.log'
        if ($workerProcess -and (Test-Path -LiteralPath $validationLog)) { break }
        Start-Sleep -Milliseconds 300
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not $workerProcess -or -not (Test-Path -LiteralPath $validationLog)) { throw 'Temporary worker failed to reach managed plan validation.' }
    $workerProcess.Refresh()
    if ($workerProcess.HasExited -or -not $workerProcess.Responding) { throw 'Temporary worker is not responding.' }
    if (-not (Get-Content -LiteralPath $validationLog -Raw).Contains('InvalidDataException')) { throw 'Unexpected bootstrap validation outcome.' }
    $workerDirectory = Split-Path -Parent $workerInfo.ExecutablePath
    foreach ($file in @('hostfxr.dll','hostpolicy.dll','coreclr.dll','PresentationFramework.dll','System.Private.CoreLib.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $workerDirectory $file))) { throw "Relocated runtime missing: $file" }
    }
    $trace = Get-Content -LiteralPath $hostTrace -Raw
    if (-not $trace.Contains((Join-Path $workerDirectory 'coreclr.dll'))) { throw 'Worker did not load its app-local CoreCLR.' }
    foreach ($forbidden in @('TerminalSimulation.Wpf.dll','config.json','KEY.txt','MapApp','MapAppOld','Workspaces')) {
        if (Test-Path -LiteralPath (Join-Path $workerDirectory $forbidden)) { throw "Unrelated data copied into worker: $forbidden" }
    }
    Write-Output "Full updater bootstrap passed with app-local runtime: $workerDirectory"
} finally {
    if ($workerProcess) {
        $workerProcess.Refresh()
        if (-not $workerProcess.HasExited) {
            [void]$workerProcess.CloseMainWindow()
            if (-not $workerProcess.WaitForExit(5000)) { Stop-Process -Id $workerProcess.Id -Force }
        }
    }
    $bootstrap.Refresh()
    if (-not $bootstrap.HasExited) { Stop-Process -Id $bootstrap.Id -Force }
}
Write-Output "Verified release: $ExpectedVersion"
Write-Output "Package bytes: $($manifest.package.size)"
Write-Output "SHA-256: $($manifest.package.sha256)"
Write-Output "Isolated extraction: $qaDirectory"

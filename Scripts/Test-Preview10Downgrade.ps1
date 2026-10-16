param(
    [string]$SourcePackage = 'artifacts/publish/preview10-20261014.1/TerminalSimulation-win-x64-preview10-build.20261014.1.zip',
    [string]$TargetPackage = 'artifacts/publish/preview8-20260924.2/TerminalSimulation-win-x64-preview8-build.20260924.2.zip'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repo = Split-Path -Parent $PSScriptRoot
$qa = Join-Path $repo ('artifacts/.qa/downgrade-' + [Guid]::NewGuid().ToString('N'))
$installed = Join-Path $qa 'installed'
$payload = Join-Path $qa 'payload'
$backup = Join-Path $qa 'backup'
[IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $repo $SourcePackage), $installed)
[IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $repo $TargetPackage), $payload)
$workspace = Join-Path $installed 'Workspaces/Map/new'
[void](New-Item -ItemType Directory -Path $workspace -Force)
[IO.File]::WriteAllText((Join-Path $workspace 'sentinel.txt'), 'preserve-route-data')
[IO.File]::WriteAllText((Join-Path $installed 'update-sources.json'), '{"schemaVersion":3,"autoCheck":false,"channel":"preview","line":"main","sources":[]}', [Text.UTF8Encoding]::new($false))
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $repo 'TerminalSimulation.Updater/bin/Debug/net8.0-windows/update.dll'))
$plan = [Activator]::CreateInstance($assembly.GetType('TerminalSimulation.Updater.UpdatePlan'))
$plan.TargetDirectory = $installed
$plan.MainExecutablePath = Join-Path $installed 'TerminalSimulation.Wpf.exe'
$method = $assembly.GetType('TerminalSimulation.Updater.UpdateEngine').GetMethod('ApplyWithRollbackAsync', [Reflection.BindingFlags]'Static,NonPublic')
$invoke = [object[]]::new(5)
$invoke[0] = $plan.PSObject.BaseObject
$invoke[1] = $payload.PSObject.BaseObject
$invoke[2] = $backup.PSObject.BaseObject
$invoke[3] = $null
$invoke[4] = [Threading.CancellationToken]::None
$task = $method.Invoke($null, $invoke)
[void]$task.GetAwaiter().GetResult()
foreach ($name in @('hostfxr.dll','hostpolicy.dll','coreclr.dll')) {
    if (Test-Path -LiteralPath (Join-Path $installed $name)) { throw "Stale runtime remains: $name" }
    if (-not (Test-Path -LiteralPath (Join-Path $backup ('deleted/' + $name)))) { throw "Runtime backup missing: $name" }
}
if ((Get-Content -LiteralPath (Join-Path $workspace 'sentinel.txt') -Raw) -ne 'preserve-route-data') { throw 'Workspace was modified.' }
foreach ($name in @('TerminalSimulation.Wpf.exe','TerminalSimulation.Wpf.dll','TerminalSimulation.Wpf.runtimeconfig.json')) {
    if ((Get-FileHash -LiteralPath (Join-Path $installed $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $payload $name)).Hash) { throw "Payload mismatch: $name" }
}
$start = New-Object Diagnostics.ProcessStartInfo
$start.FileName = $plan.MainExecutablePath
$start.WorkingDirectory = $installed
$start.UseShellExecute = $false
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$start.EnvironmentVariables['COREHOST_TRACE'] = '1'
$tracePath = Join-Path $qa 'downgrade-host-trace.log'
$start.EnvironmentVariables['COREHOST_TRACEFILE'] = $tracePath
$process = [Diagnostics.Process]::Start($start)
try {
    Start-Sleep -Seconds 10
    $process.Refresh()
    if ($process.HasExited -or -not $process.Responding -or $process.MainWindowHandle -eq [IntPtr]::Zero) { throw 'Downgraded main app failed startup.' }
    if (Test-Path -LiteralPath (Join-Path $installed 'startup-crash.log')) { throw 'Downgraded main app reported a crash.' }
    $trace = Get-Content -LiteralPath $tracePath -Raw
    if ($trace -notmatch 'CoreCLR path = .*shared\\Microsoft.NETCore.App\\') { throw 'Downgraded main app did not load the system runtime.' }
    Write-Output "Preview10 -> Preview8 actual-package downgrade passed; title=$($process.MainWindowTitle)"
    Write-Output "User workspace preserved; obsolete runtime backed up. QA: $qa"
} finally {
    $process.Refresh()
    if (-not $process.HasExited) {
        [void]$process.CloseMainWindow()
        if (-not $process.WaitForExit(5000)) { Stop-Process -Id $process.Id -Force }
    }
}

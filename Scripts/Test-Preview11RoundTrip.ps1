param([string]$ElevenPayload = 'artifacts/.qa/preview11-payload', [string]$ElevenSetup = '', [switch]$PrivateRuntimeOnly, [switch]$FaultScenarios)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$source = [IO.Path]::GetFullPath((Join-Path $repo $ElevenPayload))
if (-not (Test-Path "$source/.Updater/bundle.json")) { throw 'Build the Preview11 QA payload first.' }
$qa = Join-Path $repo ('artifacts/.qa/preview11-roundtrip-' + [Guid]::NewGuid().ToString('N'))
$installed = Join-Path $qa 'installed'
[void](New-Item -ItemType Directory -Force -Path $installed)
Copy-Item -Path "$source/*" -Destination $installed -Recurse -Force
$sentinel = "$installed/Workspaces/Map/new/sentinel.txt"
[void](New-Item -ItemType Directory -Force -Path (Split-Path -Parent $sentinel))
[IO.File]::WriteAllText($sentinel, 'protected-user-route')
[IO.File]::WriteAllText("$installed/update-sources.json", '{"schemaVersion":3,"autoCheck":false,"channel":"preview","line":"main","sources":[]}', [Text.UTF8Encoding]::new($false))
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
public sealed class CtsPackageServer : IDisposable {
    readonly HttpListener listener = new HttpListener();
    readonly string root;
    public CtsPackageServer(string path, int port) {
        root = path; listener.Prefixes.Add("http://127.0.0.1:" + port + "/"); listener.Start();
        Task.Run(async () => {
            while (listener.IsListening) {
                HttpListenerContext c;
                try { c = await listener.GetContextAsync(); } catch { return; }
                try {
                    var name = Path.GetFileName(c.Request.Url.AbsolutePath);
                    using (var file = File.OpenRead(Path.Combine(root, name))) {
                        c.Response.ContentLength64 = file.Length;
                        await file.CopyToAsync(c.Response.OutputStream);
                    }
                } catch { try { c.Response.StatusCode = 500; } catch {} }
                finally { c.Response.Close(); }
            }
        });
    }
    public void Dispose() { listener.Close(); }
}
'@
$socket = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$socket.Start(); $port = ([Net.IPEndPoint]$socket.LocalEndpoint).Port; $socket.Stop()
$server = [CtsPackageServer]::new($qa, $port)
$nativeHash = (Get-FileHash "$installed/update.exe").Hash
$engineSlot = (Get-Content "$source/.Updater/current.txt" -Raw).Trim()
$engineHash = (Get-FileHash "$installed/.Updater/$engineSlot/update.dll").Hash
if ($ElevenSetup) { Copy-Item -LiteralPath ([IO.Path]::GetFullPath((Join-Path $repo $ElevenSetup))) -Destination "$qa/eleven.zip" }
else { [IO.Compression.ZipFile]::CreateFromDirectory($source, "$qa/eleven.zip", [IO.Compression.CompressionLevel]::Fastest, $false) }
Copy-Item -LiteralPath "$repo/artifacts/publish/preview9-20261008.1/TerminalSimulation-win-x64-preview9-build.20261008.1.zip" -Destination "$qa/nine.zip"
Copy-Item -LiteralPath "$repo/artifacts/publish/preview8-20260924.2/TerminalSimulation-win-x64-preview8-build.20260924.2.zip" -Destination "$qa/eight.zip"
$previousPrivate = $env:CTS_RUNTIME_PRIVATE_ONLY
$previousHidden = $env:CTS_QA_WINDOW_HIDDEN
$previousTrace = $env:COREHOST_TRACE
$previousTraceFile = $env:COREHOST_TRACEFILE
$previousPath = $env:Path
$env:Path = 'D:/App/mingw64/bin;' + $env:Path
$env:CTS_QA_WINDOW_HIDDEN = '1'
if ($PrivateRuntimeOnly) { $env:CTS_RUNTIME_PRIVATE_ONLY = '1' }
function Get-TestMain {
    Get-CimInstance Win32_Process -Filter "Name='TerminalSimulation.Wpf.exe'" | Where-Object { $_.ExecutablePath -eq "$installed\TerminalSimulation.Wpf.exe" }
}
function Stop-TestMain {
    foreach ($item in @(Get-TestMain)) {
        $process = Get-Process -Id $item.ProcessId -ErrorAction SilentlyContinue
        if ($process) {
            [void]$process.CloseMainWindow()
            if (-not $process.WaitForExit(5000)) { Stop-Process -Id $process.Id -Force }
        }
    }
}
function Invoke-Switch([string]$Package, [string]$Version) {
    Stop-TestMain
    $work = Join-Path $qa ([Guid]::NewGuid().ToString('N'))
    [void](New-Item -ItemType Directory -Path $work)
    $file = Get-Item "$qa/$Package"
    $plan = [ordered]@{
        SchemaVersion=2; Version=$Version; CurrentLine='main'; TargetLine='main';
        CurrentCompatibilityEpoch=1; TargetCompatibilityEpoch=1;
        PackageUrl="http://127.0.0.1:$port/$Package"; Sha256=(Get-FileHash $file.FullName).Hash;
        PackageSize=$file.Length; TargetDirectory=$installed; MainExecutablePath="$installed/TerminalSimulation.Wpf.exe";
        ParentProcessId=0; AllowInsecureHttp=$true; Delete=@()
    }
    $planPath = "$work/update-plan.json"
    [IO.File]::WriteAllText($planPath, ($plan | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    $launcher = Start-Process -FilePath "$installed/update.exe" -ArgumentList @('--plan', ('"' + $planPath + '"')) -WorkingDirectory $installed -WindowStyle Hidden -PassThru
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $completed = $false
    while ($timer.Elapsed.TotalSeconds -lt 180) {
        Start-Sleep -Milliseconds 500
        if (Test-Path "$work/update.log") { throw (Get-Content "$work/update.log" -Raw) }
        if (Test-Path "$work/update-bootstrap.log") { throw (Get-Content "$work/update-bootstrap.log" -Raw) }
        if (Test-Path "$installed/.Updater/state.json") {
            $state = Get-Content "$installed/.Updater/state.json" -Raw | ConvertFrom-Json
            if ($state.applicationVersion -eq $Version -and -not $state.pendingTransaction) { $completed = $true; break }
        }
    }
    if (-not $completed) { throw "Timed out switching to $Version. QA: $qa" }
    Start-Sleep -Seconds 2
    if ((Get-FileHash "$installed/update.exe").Hash -ne $nativeHash) { throw 'Legacy payload replaced the native updater bridge.' }
    if ((Get-FileHash "$installed/.Updater/$engineSlot/update.dll").Hash -ne $engineHash) { throw 'Legacy payload replaced the persistent kernel.' }
    if ((Get-Content $sentinel -Raw) -ne 'protected-user-route') { throw 'Workspace changed.' }
    $main = @(Get-TestMain)
    if ($main.Count -ne 1) { throw 'Target main process missing or duplicated.' }
    $process = Get-Process -Id $main[0].ProcessId
    if (-not $process.Responding -or -not $process.MainWindowTitle.Contains('车载定位终端模拟系统')) { throw 'Target main window is not healthy.' }
    Write-Output "PASS: switched to $Version; retained $engineSlot; main window responsive."
}
function Invoke-Failure([string]$Package, [string]$ExpectedMessage, [switch]$WrongHash) {
    Stop-TestMain
    $before = (Get-FileHash "$installed/TerminalSimulation.Wpf.exe").Hash
    $work = Join-Path $qa ([Guid]::NewGuid().ToString('N'))
    [void](New-Item -ItemType Directory -Path $work)
    $file = Get-Item "$qa/$Package"
    $hash = if ($WrongHash) { '0' * 64 } else { (Get-FileHash $file.FullName).Hash }
    $plan = @{
        SchemaVersion=2; Version='preview11-failure-test'; CurrentLine='main'; TargetLine='main';
        CurrentCompatibilityEpoch=1; TargetCompatibilityEpoch=1;
        PackageUrl="http://127.0.0.1:$port/$Package"; Sha256=$hash; PackageSize=$file.Length;
        TargetDirectory=$installed; MainExecutablePath="$installed/TerminalSimulation.Wpf.exe";
        ParentProcessId=0; AllowInsecureHttp=$true; Delete=@()
    }
    $path = "$work/update-plan.json"
    [IO.File]::WriteAllText($path, ($plan | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    [void](Start-Process -FilePath "$installed/update.exe" -ArgumentList @('--plan', ('"' + $path + '"')) -WorkingDirectory $installed -WindowStyle Hidden -PassThru)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path "$work/update.log") -and $timer.Elapsed.TotalSeconds -lt 180) { Start-Sleep -Milliseconds 500 }
    if (-not (Test-Path "$work/update.log")) { throw 'Failure scenario did not return diagnostics.' }
    Start-Sleep -Seconds 3
    $log = Get-Content "$work/update.log" -Raw
    if (-not $log.Contains($ExpectedMessage)) { throw "Unexpected failure: $log" }
    while ((Get-Content "$installed/.Updater/state.json" -Raw | ConvertFrom-Json).pendingTransaction -and $timer.Elapsed.TotalSeconds -lt 180) { Start-Sleep -Milliseconds 500 }
    if ((Get-FileHash "$installed/TerminalSimulation.Wpf.exe").Hash -ne $before) { throw 'Main EXE was not restored.' }
    $state = Get-Content "$installed/.Updater/state.json" -Raw | ConvertFrom-Json
    if ($state.pendingTransaction) { throw 'Rollback did not clear its pending transaction.' }
    if ((Get-Content $sentinel -Raw) -ne 'protected-user-route') { throw 'Failure modified user data.' }
    Get-CimInstance Win32_Process -Filter "Name='update.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($work) } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Write-Output "PASS: failure '$ExpectedMessage' did not damage the previous application."
}
try {
    Invoke-Switch 'nine.zip' 'preview9-build.20261008.1'
    Invoke-Switch 'eleven.zip' 'preview11-build.20261015.qa'
    if ($FaultScenarios) {
        Invoke-Failure 'nine.zip' 'SHA-256' -WrongHash
        $broken = "$qa/broken"
        [IO.Compression.ZipFile]::ExtractToDirectory("$qa/eleven.zip", $broken)
        $exitSource = "$qa/exit42.cpp"
        [IO.File]::WriteAllText($exitSource, 'int main(){return 42;}', [Text.Encoding]::ASCII)
        & 'D:/App/mingw64/bin/g++.exe' '-static' '-mwindows' $exitSource '-o' "$broken/TerminalSimulation.Wpf.exe"
        if ($LASTEXITCODE) { throw 'Startup failure fixture compilation failed.' }
        [IO.Compression.ZipFile]::CreateFromDirectory($broken, "$qa/broken.zip", [IO.Compression.CompressionLevel]::Fastest, $false)
        Invoke-Failure 'broken.zip' '42'
        $badKernel = "$qa/bad-kernel"
        [IO.Compression.ZipFile]::ExtractToDirectory("$qa/eleven.zip", $badKernel)
        Copy-Item -LiteralPath "$badKernel/.Updater/$engineSlot" -Destination "$badKernel/.Updater/engines/4.0.0" -Recurse
        Copy-Item -LiteralPath "$broken/TerminalSimulation.Wpf.exe" -Destination "$badKernel/.Updater/engines/4.0.0/update.exe" -Force
        $bundle = Get-Content "$badKernel/.Updater/bundle.json" -Raw | ConvertFrom-Json -AsHashtable
        $bundle.kernelVersion = '4.0.0'; $bundle.engine = 'engines/4.0.0'
        foreach ($key in @($bundle.files.Keys)) {
            if ($key.StartsWith($engineSlot + '/')) {
                $newKey = $key.Replace($engineSlot + '/', 'engines/4.0.0/')
                $bundle.files[$newKey] = (Get-FileHash "$badKernel/.Updater/$newKey").Hash.ToLowerInvariant()
            }
        }
        [IO.File]::WriteAllText("$badKernel/.Updater/bundle.json", ($bundle | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
        [IO.Compression.ZipFile]::CreateFromDirectory($badKernel, "$qa/bad-kernel.zip", [IO.Compression.CompressionLevel]::Fastest, $false)
        Invoke-Failure 'bad-kernel.zip' '新更新内核自检失败'
        if ((Get-Content "$installed/.Updater/current.txt" -Raw) -ne $engineSlot) { throw 'Failed new kernel was activated.' }
        # A missing current slot must fall back to the last-good kernel and heal
        # the pointer, without depending on any main application's runtime.
        [IO.File]::WriteAllText("$installed/.Updater/current.txt", 'engines/missing', [Text.Encoding]::ASCII)
        Invoke-Failure 'nine.zip' 'SHA-256' -WrongHash
        if ((Get-Content "$installed/.Updater/current.txt" -Raw) -ne $engineSlot) { throw 'Kernel fallback did not repair its pointer.' }
        Write-Output 'PASS: native launcher recovered the last-good kernel.'
    }
    Invoke-Switch 'eight.zip' 'preview8-build.20260924.2'
    # Test a later, independent double-click launch, not just the updater's child.
    Stop-TestMain
    $trace = "$qa/legacy-double-click.log"
    $env:COREHOST_TRACE = '1'; $env:COREHOST_TRACEFILE = $trace
    $main = Start-Process -FilePath "$installed/TerminalSimulation.Wpf.exe" -WorkingDirectory $installed -WindowStyle Hidden -PassThru
    Start-Sleep -Seconds 10
    $main.Refresh()
    if ($main.HasExited -or -not $main.Responding -or -not $main.MainWindowTitle.Contains('车载定位终端模拟系统')) { throw 'Legacy independent relaunch failed.' }
    $traceContent = Get-Content $trace -Raw
    if ($PrivateRuntimeOnly -and $traceContent -notmatch '\.Updater[\\/]runtimes[\\/]net8-x64') { throw 'Legacy relaunch did not use the private runtime.' }
    Write-Output 'PASS: independent legacy EXE relaunch uses the selected runtime.'
    $env:COREHOST_TRACE = $previousTrace; $env:COREHOST_TRACEFILE = $previousTraceFile
    Invoke-Switch 'nine.zip' 'preview9-build.20261008.1'
    Invoke-Switch 'eleven.zip' 'preview11-build.20261015.qa'
    Write-Output "Round-trip suite passed. Private runtime only: $PrivateRuntimeOnly; QA: $qa"
} finally {
    Stop-TestMain
    # Only processes with this test's plan paths may be terminated.
    Get-CimInstance Win32_Process -Filter "Name='update.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($qa) } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    $server.Dispose()
    $env:CTS_RUNTIME_PRIVATE_ONLY = $previousPrivate
    $env:CTS_QA_WINDOW_HIDDEN = $previousHidden
    $env:COREHOST_TRACE = $previousTrace
    $env:COREHOST_TRACEFILE = $previousTraceFile
    $env:Path = $previousPath
}

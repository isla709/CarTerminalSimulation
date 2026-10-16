param(
    [string]$Preview10Package = 'artifacts/publish/preview10-20261014.1/TerminalSimulation-win-x64-preview10-build.20261014.1.zip',
    [Parameter(Mandatory)][string]$Preview11Package
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repo = Split-Path -Parent $PSScriptRoot
$legacyPackage = [IO.Path]::GetFullPath((Join-Path $repo $Preview10Package))
$targetPackage = [IO.Path]::GetFullPath((Join-Path $repo $Preview11Package))
foreach ($path in @($legacyPackage, $targetPackage)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Release package is missing: $path" }
}
$qa = Join-Path $repo ('artifacts/.qa/preview10-to-preview11-' + [Guid]::NewGuid().ToString('N'))
$installed = Join-Path $qa 'installed'
$planDirectory = Join-Path $qa 'plan'
[void](New-Item -ItemType Directory -Force -Path $installed, $planDirectory)
[IO.Compression.ZipFile]::ExtractToDirectory($legacyPackage, $installed)
$sentinel = Join-Path $installed 'Workspaces/Map/new/sentinel.txt'
[void](New-Item -ItemType Directory -Force -Path (Split-Path -Parent $sentinel))
[IO.File]::WriteAllText($sentinel, 'preview10-user-data')

Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
public sealed class CtsPreview10PackageServer : IDisposable {
    readonly TcpListener listener;
    readonly string package;
    public CtsPreview10PackageServer(string file, int port) {
        package = file; listener = new TcpListener(IPAddress.Loopback, port); listener.Start();
        Task.Run(async () => {
            while (true) {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(); } catch { return; }
                _ = Task.Run(async () => {
                    using (client) {
                        try {
                            var network = client.GetStream();
                            var request = new byte[4096];
                            var received = 0;
                            while (received < request.Length) {
                                var count = await network.ReadAsync(request, received, request.Length - received);
                                if (count == 0) break;
                                received += count;
                                if (Encoding.ASCII.GetString(request, 0, received).Contains("\r\n\r\n")) break;
                            }
                            using (var fileStream = File.OpenRead(package)) {
                                var header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: " + fileStream.Length + "\r\nConnection: close\r\n\r\n");
                                await network.WriteAsync(header, 0, header.Length);
                                await fileStream.CopyToAsync(network);
                            }
                        } catch { }
                    }
                });
            }
        });
    }
    public void Dispose() { listener.Stop(); }
}
'@
$portProbe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$portProbe.Start(); $port = ([Net.IPEndPoint]$portProbe.LocalEndpoint).Port; $portProbe.Stop()
$server = [CtsPreview10PackageServer]::new($targetPackage, $port)
$version = [IO.Path]::GetFileNameWithoutExtension($targetPackage) -replace '^TerminalSimulation-win-x64-', ''
$packageInfo = Get-Item -LiteralPath $targetPackage
$plan = [ordered]@{
    SchemaVersion = 2; Version = $version; CurrentLine = 'main'; TargetLine = 'main'
    CurrentCompatibilityEpoch = 1; TargetCompatibilityEpoch = 1
    PackageUrl = "http://127.0.0.1:$port/preview11.zip"
    Sha256 = (Get-FileHash -LiteralPath $targetPackage -Algorithm SHA256).Hash
    PackageSize = $packageInfo.Length; TargetDirectory = $installed
    MainExecutablePath = Join-Path $installed 'TerminalSimulation.Wpf.exe'
    ParentProcessId = 0; AllowInsecureHttp = $true; Delete = @()
}
$planPath = Join-Path $planDirectory 'update-plan.json'
[IO.File]::WriteAllText($planPath, ($plan | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$trace = Join-Path $qa 'host-trace.log'
$previousTrace = $env:COREHOST_TRACE; $previousTraceFile = $env:COREHOST_TRACEFILE
$previousSources = $env:CTS_RUNTIME_PRIVATE_ONLY
$env:COREHOST_TRACE = '1'; $env:COREHOST_TRACEFILE = $trace; $env:CTS_RUNTIME_PRIVATE_ONLY = '1'
$updater = Start-Process -FilePath (Join-Path $installed 'update.exe') `
    -ArgumentList @('--plan', ('"' + $planPath + '"')) -WorkingDirectory $installed -WindowStyle Hidden -PassThru
$mainPath = Join-Path $installed 'TerminalSimulation.Wpf.exe'
try {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $ready = $false
    while ($timer.Elapsed.TotalSeconds -lt 180) {
        Start-Sleep -Milliseconds 500
        $updater.Refresh()
        if (Test-Path -LiteralPath (Join-Path $planDirectory 'update.log')) {
            throw (Get-Content -LiteralPath (Join-Path $planDirectory 'update.log') -Raw)
        }
        $main = Get-Process -Name 'TerminalSimulation.Wpf' -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -and $_.Path.Equals($mainPath, [StringComparison]::OrdinalIgnoreCase) } |
            Select-Object -First 1
        if ($main -and $main.Responding -and $main.MainWindowHandle -ne [IntPtr]::Zero -and
            $main.MainWindowTitle.Contains('车载定位终端模拟系统')) { $ready = $true; break }
        if ($updater.HasExited -and $updater.ExitCode -ne 0) { throw "Preview10 updater exited with $($updater.ExitCode)." }
    }
    if (-not $ready) { throw "Preview10 updater did not start a healthy Preview11 main window. QA: $qa" }
    $compatHostPath = Join-Path $installed '.Updater/compat-host.exe'
    $runtimeMarker = Get-Content -LiteralPath (Join-Path $installed '.Updater/app-runtime.txt') -Raw
    $hostMarker = Get-Content -LiteralPath (Join-Path $installed '.Updater/app-hostfxr.txt') -Raw
    if ((Get-FileHash -LiteralPath $mainPath).Hash -ne (Get-FileHash -LiteralPath $compatHostPath).Hash) { throw 'Legacy updater did not install the compatibility host as the main EXE.' }
    if ($runtimeMarker -match '^[A-Za-z]:|\.\.' -or $hostMarker -match '^[A-Za-z]:|\.\.') { throw 'Runtime bridge contains a non-portable path.' }
    if ((Get-Content -LiteralPath $sentinel -Raw) -ne 'preview10-user-data') { throw 'The legacy update modified user data.' }
    $hostTrace = if (Test-Path -LiteralPath $trace) { Get-Content -LiteralPath $trace -Raw } else { '' }
    if ($hostTrace -notmatch '\.Updater[\\/]runtimes[\\/]net8-x64[\\/]shared[\\/]Microsoft\.NETCore\.App') { throw 'Main application did not load the private runtime.' }
    Write-Output "PASS: Preview10 legacy updater upgraded to $version and started the app using its bundled .NET runtime. QA: $qa"
} finally {
    foreach ($process in @(Get-Process -Name 'TerminalSimulation.Wpf' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.Equals($mainPath, [StringComparison]::OrdinalIgnoreCase) })) {
        [void]$process.CloseMainWindow()
        if (-not $process.WaitForExit(5000)) { Stop-Process -Id $process.Id -Force }
    }
    $updater.Refresh()
    if (-not $updater.HasExited) { Stop-Process -Id $updater.Id -Force }
    $server.Dispose()
    $env:COREHOST_TRACE = $previousTrace; $env:COREHOST_TRACEFILE = $previousTraceFile
    $env:CTS_RUNTIME_PRIVATE_ONLY = $previousSources
}

param([Parameter(Mandatory)][string]$SetupPackage)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$setup = [IO.Path]::GetFullPath((Join-Path $repo $SetupPackage))
$qa = Join-Path $repo ('artifacts/.qa/legacy-setup-bridge-' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $qa)
Copy-Item -LiteralPath $setup -Destination "$qa/setup.exe"
Add-Type -TypeDefinition @'
using System; using System.IO; using System.Net; using System.Threading.Tasks;
public sealed class CtsSetupServer : IDisposable {
 readonly HttpListener h = new HttpListener(); readonly string path;
 public CtsSetupServer(string p,int port) {path=p;h.Prefixes.Add("http://127.0.0.1:"+port+"/");h.Start();Task.Run(async()=>{while(h.IsListening){HttpListenerContext c;try{c=await h.GetContextAsync();}catch{return;}try{using(var f=File.OpenRead(path)){c.Response.ContentLength64=f.Length;await f.CopyToAsync(c.Response.OutputStream);}}catch{}finally{c.Response.Close();}}});}
 public void Dispose(){h.Close();}
}
'@
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start();$port=([Net.IPEndPoint]$listener.LocalEndpoint).Port;$listener.Stop()
$server = [CtsSetupServer]::new("$qa/setup.exe",$port)
function Stop-TestProcesses([string]$Path) {
    Get-CimInstance Win32_Process | Where-Object { ($_.ExecutablePath -and $_.ExecutablePath.StartsWith($Path + '\', [StringComparison]::OrdinalIgnoreCase)) -or ($_.CommandLine -and $_.CommandLine.Contains($planRoot)) } | ForEach-Object {
        $p=Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue
        if($p){[void]$p.CloseMainWindow();if(-not $p.WaitForExit(3000)){Stop-Process -Id $p.Id -Force}}
    }
}
$failures=@()
try {
    foreach($legacy in @(@('preview8-20260924.2','preview8-build.20260924.2'),@('preview9-20261008.1','preview9-build.20261008.1'))) {
        $installed = Join-Path $qa $legacy[0]
        $planRoot = "$qa/plan-$($legacy[0])";[void](New-Item -ItemType Directory -Path $planRoot)
        [IO.Compression.ZipFile]::ExtractToDirectory("$repo/artifacts/publish/$($legacy[0])/TerminalSimulation-win-x64-$($legacy[1]).zip",$installed)
        [IO.File]::WriteAllText("$installed/update-sources.json",'{"schemaVersion":3,"autoCheck":false,"line":"main","channel":"preview","sources":[]}',[Text.UTF8Encoding]::new($false))
        [void](New-Item -ItemType Directory -Force -Path "$installed/Workspaces")
        [IO.File]::WriteAllText("$installed/Workspaces/sentinel.txt",'keep-user-data')
        $archive=[IO.Compression.ZipFile]::OpenRead($setup);try{$reader=[IO.StreamReader]::new($archive.GetEntry('setup-package.json').Open());try{$metadata=$reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}}finally{$archive.Dispose()}
        $plan=@{SchemaVersion=2;Version=$metadata.version;CurrentLine='main';TargetLine='main';CurrentCompatibilityEpoch=1;TargetCompatibilityEpoch=1;PackageUrl="http://127.0.0.1:$port/setup.exe";Sha256=(Get-FileHash $setup).Hash;PackageSize=(Get-Item $setup).Length;TargetDirectory=$installed;MainExecutablePath="$installed/TerminalSimulation.Wpf.exe";ParentProcessId=0;AllowInsecureHttp=$true;Delete=@()}
        $planPath="$planRoot/plan.json";[IO.File]::WriteAllText($planPath,($plan|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
        try {
            [void](Start-Process -FilePath "$installed/update.exe" -ArgumentList @('--plan',('"'+$planPath+'"')) -WorkingDirectory $installed -WindowStyle Hidden -PassThru)
            $timer=[Diagnostics.Stopwatch]::StartNew();$ok=$false
            while($timer.Elapsed.TotalSeconds -lt 100) {
                Start-Sleep -Milliseconds 500
                if(Test-Path "$planRoot/update.log"){throw(Get-Content "$planRoot/update.log" -Raw)}
                if(Test-Path "$installed/.Updater/bundle.json") {
                    $main=@(Get-CimInstance Win32_Process -Filter "Name='TerminalSimulation.Wpf.exe'" | Where-Object ExecutablePath -eq "$installed\TerminalSimulation.Wpf.exe")
                    if($main.Count -eq 1){$p=Get-Process -Id $main[0].ProcessId;if($p.Responding -and $p.MainWindowTitle.Contains('车载定位终端模拟系统')){$ok=$true;break}}
                }
            }
            if(-not $ok){throw 'Original updater could not complete (may require its original system runtime).'}
            if((Get-Content "$installed/Workspaces/sentinel.txt" -Raw) -ne 'keep-user-data'){throw 'User data changed.'}
            Write-Output "PASS: original $($legacy[1]) updater read the setup EXE as ZIP and launched Preview11."
        } catch {$failures+="$($legacy[1]): $_";Write-Warning $failures[-1]} finally {Stop-TestProcesses $installed}
    }
} finally {$server.Dispose()}
[IO.File]::WriteAllText("$qa/result.txt", $(if($failures.Count){$failures -join "`r`n"}else{'Both original legacy updater bridges passed.'}),[Text.UTF8Encoding]::new($false))
Write-Output "Legacy bridge evidence: $qa"
if($failures.Count){throw($failures -join "`n")}

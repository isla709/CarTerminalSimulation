param([string]$Configuration = 'Debug', [Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLowerInvariant() }
    finally { $stream.Dispose(); $sha.Dispose() }
}
$repo = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputPath)
if (-not $output.StartsWith($repo + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Updater output must be inside the workspace.' }
$native = Join-Path $repo "Updater.Native/bin/$Configuration"
$engine = Join-Path $repo "TerminalSimulation.Updater/bin/$Configuration/kernel"
$compiler = 'D:/App/mingw64/bin/g++.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Install MinGW x64 or configure the native compiler in Build-UpdaterBundle.ps1.' }
$mutex = [Threading.Mutex]::new($false, 'Local\CTS.UpdaterBundleBuild')
$locked = $false
try {
    $locked = $mutex.WaitOne([TimeSpan]::FromMinutes(5))
    if (-not $locked) { throw 'Updater bundle build timed out.' }
    [void](New-Item -ItemType Directory -Force -Path $native)
    $env:Path = 'D:/App/mingw64/bin;' + $env:Path
    Push-Location (Join-Path $repo 'Updater.Native')
    try {
        foreach ($name in @('update','app')) {
            & 'D:/App/mingw64/bin/windres.exe' "$name.rc" '-O' 'coff' '-o' "$native/$name.res"
            if ($LASTEXITCODE) { throw 'Native icon compilation failed.' }
            $flags = @('-std=c++17','-O2','-municode','-mwindows','-static','-static-libgcc','-static-libstdc++')
            if ($name -eq 'app') { $flags += '-DAPP_HOST' }
            & $compiler @flags 'launcher.cpp' "$native/$name.res" '-lshell32' '-lole32' '-o' "$native/$name.exe"
            if ($LASTEXITCODE) { throw 'Native compatibility host compilation failed.' }
        }
    } finally { Pop-Location }
    Push-Location (Join-Path $repo 'Updater.Native')
    try { & 'D:/App/mingw64/bin/windres.exe' 'setup.rc' '-O' 'coff' '-o' "$native/setup.res"; if ($LASTEXITCODE) { throw 'Setup icon compilation failed.' } }
    finally { Pop-Location }
    & $compiler '-std=c++17' '-O2' '-municode' '-mwindows' '-static' '-static-libgcc' '-static-libstdc++' "$repo/Updater.Native/setup.cpp" "$native/setup.res" '-lshell32' '-o' "$native/setup.exe"
    if ($LASTEXITCODE) { throw 'Native setup bootstrap compilation failed.' }
    $engineDirectory = [IO.Path]::GetFullPath($engine)
    if (-not $engineDirectory.StartsWith($repo + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Updater engine output escaped the workspace.' }
    if (Test-Path -LiteralPath $engineDirectory) { Remove-Item -LiteralPath $engineDirectory -Recurse -Force }
    [void](New-Item -ItemType Directory -Force -Path $engineDirectory)
    & dotnet publish "$repo/TerminalSimulation.Updater/TerminalSimulation.Updater.csproj" -c $Configuration -r win-x64 --self-contained false -o $engine -p:SatelliteResourceLanguages=en --nologo -v:q
    if ($LASTEXITCODE) { throw 'Isolated updater publish failed.' }
    # Only neutral resources are shipped; source app has no i18n adaptation.
    [xml]$project = Get-Content -LiteralPath "$repo/TerminalSimulation.Updater/TerminalSimulation.Updater.csproj" -Raw
    $kernelVersion = [string]$project.Project.PropertyGroup.Version
    $slot = "engines/$kernelVersion"
    $component = Join-Path $output '.Updater'
    foreach ($generated in @((Join-Path $component 'engines'), (Join-Path $component 'runtimes/net8-x64'))) {
        $generatedPath = [IO.Path]::GetFullPath($generated)
        if (-not $generatedPath.StartsWith($repo + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Updater bundle cleanup escaped the workspace.' }
        if (Test-Path -LiteralPath $generatedPath) { Remove-Item -LiteralPath $generatedPath -Recurse -Force }
    }
    [void](New-Item -ItemType Directory -Force -Path "$component/$slot")
    Copy-Item -Path "$engine/*" -Destination "$component/$slot" -Recurse -Force
    Copy-Item -LiteralPath "$native/update.exe" -Destination "$output/update.exe" -Force
    Copy-Item -LiteralPath "$native/app.exe" -Destination "$component/compat-host.exe" -Force
    foreach ($name in @('update.dll','update.deps.json','update.runtimeconfig.json')) { Copy-Item -LiteralPath "$engine/$name" -Destination "$output/$name" -Force }
    [IO.File]::WriteAllText("$component/current.txt", $slot, [Text.Encoding]::ASCII)
    [IO.File]::WriteAllText("$component/last-good.txt", $slot, [Text.Encoding]::ASCII)
    # Cache a clean official runtime in standard dotnet layout. Never construct it
    # from flattened application output: NuGet assemblies can shadow framework DLLs.
    $dotnetRoot = Split-Path -Parent (Get-Command dotnet.exe).Source
    $runtimeVersion = Get-ChildItem "$dotnetRoot/shared/Microsoft.WindowsDesktop.App" -Directory |
        Where-Object { $_.Name -match '^8\.0\.\d+$' -and (Test-Path "$dotnetRoot/shared/Microsoft.NETCore.App/$($_.Name)") -and (Test-Path "$dotnetRoot/host/fxr/$($_.Name)") } |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1 -ExpandProperty Name
    if (-not $runtimeVersion) { throw 'A clean matching .NET 8 x64 Desktop/Core runtime is required to build the offline compatibility cache.' }
    $runtime = "$component/runtimes/net8-x64"
    foreach ($part in @("shared/Microsoft.NETCore.App/$runtimeVersion", "shared/Microsoft.WindowsDesktop.App/$runtimeVersion", "host/fxr/$runtimeVersion")) {
        [void](New-Item -ItemType Directory -Force -Path "$runtime/$part")
        Get-ChildItem -LiteralPath "$dotnetRoot/$part" -File | Copy-Item -Destination "$runtime/$part" -Force
    }
    foreach ($notice in @('LICENSE.txt','ThirdPartyNotices.txt')) {
        if (Test-Path -LiteralPath "$dotnetRoot/$notice") { Copy-Item -LiteralPath "$dotnetRoot/$notice" -Destination "$runtime/$notice" -Force }
    }
    # Hash the complete protected bundle, including the private runtime cache.
    $files = [ordered]@{}
    Get-ChildItem -LiteralPath $component -File -Recurse | Where-Object { $_.Name -notin @('bundle.json','state.json','current.txt','last-good.txt','update.lock','app-runtime.txt','app-hostfxr.txt') -and $_.FullName -notlike '*\transactions\*' } | Sort-Object FullName | ForEach-Object {
        $relative = $_.FullName.Substring($component.Length + 1).Replace('\','/')
        $files[$relative] = Get-Sha256 $_.FullName
    }
    $manifest = [ordered]@{ schemaVersion = 1; kernelVersion = $kernelVersion; engine = $slot; runtimeVersion = $runtimeVersion; entrySha256 = (Get-Sha256 "$output/update.exe"); files = $files }
    [IO.File]::WriteAllText("$component/bundle.json", ($manifest | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
} finally {
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}

param([Parameter(Mandatory)][string]$ApplicationDirectory)

$ErrorActionPreference = 'Stop'
$application = [IO.Path]::GetFullPath($ApplicationDirectory)
$executable = Join-Path $application 'TerminalSimulation.Wpf.exe'
$runtimeConfigPath = Join-Path $application 'TerminalSimulation.Wpf.runtimeconfig.json'
$runtimeRootRelative = '.Updater/runtimes/net8-x64'
$runtimeRoot = Join-Path $application ($runtimeRootRelative.Replace('/', [IO.Path]::DirectorySeparatorChar))
$compatibilityHost = Join-Path $application '.Updater/compat-host.exe'

foreach ($path in @($executable, $runtimeConfigPath, $compatibilityHost)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Runtime bridge input is missing: $path" }
}

$options = (Get-Content -LiteralPath $runtimeConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json).runtimeOptions
$frameworks = @()
if ($options.framework) { $frameworks += $options.framework }
if ($options.frameworks) { $frameworks += @($options.frameworks) }
if ($options.includedFrameworks -or $frameworks.Count -eq 0) {
    throw 'The compatibility bridge is for framework-dependent .NET 8 releases only.'
}

$requested = @($frameworks | ForEach-Object {
    if ($_.name -notin @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')) {
        throw "Unsupported framework in release runtime config: $($_.name)"
    }
    [pscustomobject]@{ Name = [string]$_.name; Version = [version]$_.version }
})
foreach ($framework in $requested) {
    $shared = Join-Path $runtimeRoot ("shared/{0}" -f $framework.Name)
    $available = @(Get-ChildItem -LiteralPath $shared -Directory -ErrorAction SilentlyContinue | Where-Object {
        try { $version = [version]$_.Name; $version.Major -eq $framework.Version.Major -and $version.Minor -eq $framework.Version.Minor -and $version -ge $framework.Version }
        catch { $false }
    } | Sort-Object { [version]$_.Name } -Descending)
    if ($available.Count -eq 0) { throw "The private runtime cache does not satisfy $($framework.Name) $($framework.Version)." }
}

$fxrDirectory = Join-Path $runtimeRoot 'host/fxr'
$frameworkMajor = ($requested | ForEach-Object { $_.Version.Major } | Measure-Object -Maximum).Maximum
$hostfxr = @(Get-ChildItem -LiteralPath $fxrDirectory -Directory -ErrorAction SilentlyContinue | Where-Object {
    try { ([version]$_.Name).Major -eq $frameworkMajor }
    catch { $false }
} | Sort-Object { [version]$_.Name } -Descending | ForEach-Object { Join-Path $_.FullName 'hostfxr.dll' } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1)
if ($hostfxr.Count -eq 0) { throw 'The private runtime cache does not contain a compatible hostfxr.dll.' }

$runtimeMarker = Join-Path $application '.Updater/app-runtime.txt'
$hostMarker = Join-Path $application '.Updater/app-hostfxr.txt'
[IO.File]::WriteAllText($runtimeMarker, $runtimeRootRelative, [Text.UTF8Encoding]::new($false))
$hostRelative = [IO.Path]::GetRelativePath($application, $hostfxr[0]).Replace('\', '/')
[IO.File]::WriteAllText($hostMarker, $hostRelative, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath $compatibilityHost -Destination $executable -Force

if ((Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $compatibilityHost -Algorithm SHA256).Hash) {
    throw 'The legacy-compatible main executable was not staged correctly.'
}
Write-Output "Legacy runtime bridge ready: $runtimeRootRelative / $hostRelative"

param(
    [Parameter(Mandatory = $true)]
    [string]$InputDirectory,
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Channel = "preview",
    [string]$Line = "main",
    [ValidateRange(1, 2147483647)]
    [int]$CompatibilityEpoch = 1,
    [string]$ReleaseNotes = "",
    [string]$OutputDirectory = "artifacts/update",
    [string]$PackageBaseUrl = "",
    [string[]]$PreviousManifestPath = @(),
    [ValidateSet('auto','installer','zip','both')][string]$PackageFormat = 'auto',
    [string]$SetupStubPath = ''
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$source = [System.IO.Path]::GetFullPath($InputDirectory)
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not [System.IO.Directory]::Exists($source)) {
    throw "Input directory does not exist: $source"
}
if (-not [System.IO.File]::Exists([System.IO.Path]::Combine($source, "update.exe"))) {
    throw "The build output does not contain update.exe: $source"
}
$bundlePath = Join-Path $source '.Updater/bundle.json'
$bundle = $null
if (Test-Path -LiteralPath $bundlePath) {
    $bundle = Get-Content -LiteralPath $bundlePath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($relative in @('.Updater/current.txt', '.Updater/last-good.txt', '.Updater/compat-host.exe', ('.Updater/' + $bundle.engine + '/update.exe'))) {
        if (-not (Test-Path -LiteralPath (Join-Path $source $relative))) { throw "Persistent updater component missing: $relative" }
    }
    if (Test-Path -LiteralPath (Join-Path $source '.Updater/state.json')) { throw 'Do not package a used installation; publish to a clean output directory first.' }
}

[System.IO.Directory]::CreateDirectory($output) | Out-Null
$safeVersion = $Version -replace '[^A-Za-z0-9._-]', '_'
if ($PackageFormat -eq 'auto') {
    $previewMatch = [regex]::Match($Version, '^preview(\d+)(?:-|$)', 'IgnoreCase')
    $PackageFormat = if ($previewMatch.Success -and [int]$previewMatch.Groups[1].Value -le 10) { 'zip' } else { 'installer' }
}

function New-PackageAsset([string]$format) {
    $packageName = if ($format -eq 'installer') { "TerminalSimulation-Setup-win-x64-$safeVersion.exe" } else { "TerminalSimulation-win-x64-$safeVersion.zip" }
    $packagePath = [System.IO.Path]::Combine($output, $packageName)
    if ([System.IO.File]::Exists($packagePath)) { [System.IO.File]::Delete($packagePath) }
    if ($format -eq 'installer') {
        if ($null -eq $bundle) { throw 'Installer releases require the persistent updater bundle.' }
        if (-not $SetupStubPath) {
            $repo = Split-Path -Parent $PSScriptRoot
            $configuration = if ($source -match '[\\/]Debug[\\/]') { 'Debug' } else { 'Release' }
            $SetupStubPath = Join-Path $repo "Updater.Native/bin/$configuration/setup.exe"
        }
        if (-not (Test-Path -LiteralPath $SetupStubPath)) { throw "Setup bootstrap missing: $SetupStubPath" }
        $enginePath = Join-Path $source ('.Updater/' + $bundle.engine + '/update.exe')
        $arguments = @('--build-setup','--input',('"' + $source + '"'),'--stub',('"' + [IO.Path]::GetFullPath($SetupStubPath) + '"'),'--output',('"' + $packagePath + '"'),'--version',$Version,'--line',$Line,'--epoch',$CompatibilityEpoch)
        $process = Start-Process -FilePath $enginePath -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
        if ($process.ExitCode -ne 0) { throw "Setup package build failed: $($process.ExitCode). See $packagePath.error.txt" }
    } else {
        [System.IO.Compression.ZipFile]::CreateFromDirectory($source, $packagePath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    }

    $hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $packageInfo = [System.IO.FileInfo]::new($packagePath)
    $packageUrl = $null
    if (-not [string]::IsNullOrWhiteSpace($PackageBaseUrl)) {
        $packageUrl = $PackageBaseUrl.TrimEnd('/') + '/' + $packageName
    }
    return [ordered]@{
        format = $(if ($format -eq 'installer') { 'cts-setup-v1' } else { 'zip' })
        fileName = $packageName
        url = $packageUrl
        sha256 = $hash
        size = $packageInfo.Length
    }
}

$assetFormats = if ($PackageFormat -eq 'both') { @('zip', 'installer') } else { @($PackageFormat) }
$packageAssets = @{}
foreach ($format in $assetFormats) {
    $packageAssets[$format] = New-PackageAsset $format
}
# Keep the original v2 field as a regular ZIP. Preview-era updaters ignore the
# optional `packages` field and therefore continue to consume a portable archive.
$legacyPackageFormat = if ($PackageFormat -eq 'both') { 'zip' } else { $PackageFormat }
$legacyPackage = $packageAssets[$legacyPackageFormat]
$alternatePackages = @($assetFormats | Where-Object { $_ -ne $legacyPackageFormat } | ForEach-Object { $packageAssets[$_] })

$manifest = [ordered]@{
    schemaVersion = 2
    product = "TerminalSimulation"
    version = $Version
    channel = $Channel
    line = $Line
    compatibilityEpoch = $CompatibilityEpoch
    publishedAt = [DateTimeOffset]::Now.ToString("o")
    releaseNotes = $ReleaseNotes
    minimumUpdaterVersion = "2.0.0"
    package = $legacyPackage
    delete = @()
}
if ($alternatePackages.Count -gt 0) { $manifest.packages = $alternatePackages }
if ($null -ne $bundle) {
    # Optional metadata: legacy v2 clients ignore unknown properties and can still
    # install this payload. Do NOT raise minimumUpdaterVersion to the engine v3.
    $manifest.updater = [ordered]@{ protocolVersion = 2; kernelVersion = $bundle.kernelVersion; persistent = $true; offlineRuntime = $bundle.runtimeVersion }
}

$manifestDocument = $manifest
if ($PreviousManifestPath.Count -gt 0) {
    $versions = @()
    foreach ($previousPathValue in $PreviousManifestPath) {
        $previousPath = [System.IO.Path]::GetFullPath($previousPathValue)
        if (-not [System.IO.File]::Exists($previousPath)) {
            throw "Previous manifest does not exist: $previousPath"
        }

        $previous = Get-Content -LiteralPath $previousPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($null -ne $previous.product -and $previous.product -ne "TerminalSimulation") {
            throw "Previous manifest product does not match TerminalSimulation: $previousPath"
        }
        if ($null -ne $previous.line -and -not [string]::IsNullOrWhiteSpace([string]$previous.line) -and $previous.line -ne $Line) {
            throw "Previous manifest line '$($previous.line)' does not match '$Line': $previousPath"
        }

        if ($null -ne $previous.versions) {
            $versions += @($previous.versions)
        }
        else {
            $versions += $previous
        }
    }

    if (@($versions | Where-Object { $_.version -eq $Version }).Count -gt 0) {
        throw "The catalog already contains version $Version."
    }
    $versions += $manifest
    $manifestDocument = [ordered]@{
        schemaVersion = 2
        product = "TerminalSimulation"
        line = $Line
        versions = $versions
    }
}

$manifestPath = [System.IO.Path]::Combine($output, "update-manifest.json")
[System.IO.File]::WriteAllText(
    $manifestPath,
    ($manifestDocument | ConvertTo-Json -Depth 12),
    [System.Text.UTF8Encoding]::new($false))

foreach ($format in $assetFormats) { Write-Output "Package:  $($packageAssets[$format].fileName)" }
Write-Output "Manifest: $manifestPath"
foreach ($format in $assetFormats) { Write-Output "SHA-256:  $($packageAssets[$format].fileName) $($packageAssets[$format].sha256)" }
if ($PreviousManifestPath.Count -gt 0) {
    Write-Output "Catalog:   $($versions.Count) versions"
}

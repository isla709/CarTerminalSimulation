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
    [string[]]$PreviousManifestPath = @()
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

[System.IO.Directory]::CreateDirectory($output) | Out-Null
$safeVersion = $Version -replace '[^A-Za-z0-9._-]', '_'
$packageName = "TerminalSimulation-win-x64-$safeVersion.zip"
$packagePath = [System.IO.Path]::Combine($output, $packageName)
if ([System.IO.File]::Exists($packagePath)) {
    [System.IO.File]::Delete($packagePath)
}
[System.IO.Compression.ZipFile]::CreateFromDirectory($source, $packagePath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
$packageInfo = [System.IO.FileInfo]::new($packagePath)
$packageUrl = $null
if (-not [string]::IsNullOrWhiteSpace($PackageBaseUrl)) {
    $packageUrl = $PackageBaseUrl.TrimEnd('/') + '/' + $packageName
}

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
    package = [ordered]@{
        fileName = $packageName
        url = $packageUrl
        sha256 = $hash
        size = $packageInfo.Length
    }
    delete = @()
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

Write-Output "Package:  $packagePath"
Write-Output "Manifest: $manifestPath"
Write-Output "SHA-256:  $hash"
if ($PreviousManifestPath.Count -gt 0) {
    Write-Output "Catalog:   $($versions.Count) versions"
}

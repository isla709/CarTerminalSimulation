param(
    [Parameter(Mandatory = $true)]
    [string]$InputDirectory,
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Channel = "preview",
    [string]$ReleaseNotes = "",
    [string]$OutputDirectory = "artifacts/update",
    [string]$PackageBaseUrl = ""
)

$ErrorActionPreference = "Stop"
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
    schemaVersion = 1
    product = "TerminalSimulation"
    version = $Version
    channel = $Channel
    publishedAt = [DateTimeOffset]::Now.ToString("o")
    releaseNotes = $ReleaseNotes
    minimumUpdaterVersion = "1.0.0"
    package = [ordered]@{
        fileName = $packageName
        url = $packageUrl
        sha256 = $hash
        size = $packageInfo.Length
    }
    delete = @()
}

$manifestPath = [System.IO.Path]::Combine($output, "update-manifest.json")
[System.IO.File]::WriteAllText(
    $manifestPath,
    ($manifest | ConvertTo-Json -Depth 8),
    [System.Text.UTF8Encoding]::new($false))

Write-Output "Package:  $packagePath"
Write-Output "Manifest: $manifestPath"
Write-Output "SHA-256:  $hash"

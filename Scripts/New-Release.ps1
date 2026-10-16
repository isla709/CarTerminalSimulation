param(
    [string]$AppVersion = '',
    [Parameter(Mandatory)][string]$BuildVersion,
    [string]$OutputDirectory = '',
    [string]$ReleaseNotesPath = '',
    [string]$PackageBaseUrl = '',
    [string]$Line = '',
    [int]$CompatibilityEpoch = 0,
    [switch]$NoRestore
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
[xml]$project = Get-Content -LiteralPath "$repo/TerminalSimulation.Wpf/TerminalSimulation.Wpf.csproj" -Raw
if (-not $AppVersion) { $AppVersion = [string]($project.Project.PropertyGroup.AppVersion | Where-Object { $_ } | Select-Object -First 1) }
if (-not $Line) { $Line = [string]($project.Project.PropertyGroup.UpdateLine | Where-Object { $_ } | Select-Object -First 1) }
if (-not $CompatibilityEpoch) { $CompatibilityEpoch = [int]($project.Project.PropertyGroup.CompatibilityEpoch | Where-Object { $_ } | Select-Object -First 1) }
if (-not $ReleaseNotesPath) { $ReleaseNotesPath = "docs/$AppVersion-release-notes.md" }
if (-not $Line -or $CompatibilityEpoch -lt 1) { throw 'Project version line or compatibility epoch is missing.' }
if ($BuildVersion -notmatch '^build\.\d{8}\.\d+$' -or $AppVersion -notmatch '^[a-zA-Z0-9._-]+$') { throw 'Use a safe version prefix and deterministic numeric build revision.' }
$version = "$AppVersion-$BuildVersion"
$channel = if ($AppVersion -match '^preview') { 'preview' } elseif ($AppVersion -match '^beta') { 'beta' } elseif ($AppVersion -match '^test') { 'test' } else { 'stable' }
if (-not $OutputDirectory) { $OutputDirectory = "artifacts/publish/$version" }
$output = [IO.Path]::GetFullPath((Join-Path $repo $OutputDirectory))
$root = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts/publish'))
if (-not $output.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Release output must be below artifacts/publish.' }
if (Test-Path -LiteralPath $output) { throw "Do not overwrite an existing release: $output" }
$notes = Get-Content -LiteralPath (Join-Path $repo $ReleaseNotesPath) -Raw -Encoding UTF8
$application = Join-Path $output 'application'
Push-Location $repo
try {
    $publishArguments = @('publish', 'TerminalSimulation.Wpf/TerminalSimulation.Wpf.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'false', '-m:1', '-p:IncludeXunjieCloudPlugin=false', "-p:AppVersion=$AppVersion", "-p:BuildVersionOverride=$BuildVersion", "-p:UpdateChannel=$channel", "-p:UpdateLine=$Line", "-p:CompatibilityEpoch=$CompatibilityEpoch", '-o', $application, '--nologo', '-v:minimal')
    if ($NoRestore) { $publishArguments += '--no-restore' }
    & dotnet @publishArguments
    if ($LASTEXITCODE) { throw 'Application publish failed.' }
    # Refresh and validate the isolated engine and official runtime closure.
    & (Join-Path $PSScriptRoot 'Build-UpdaterBundle.ps1') -Configuration Release -OutputPath $application
    # Make the release payload directly launchable by pre-Preview11 updaters and
    # by a fresh offline install, neither of which can inject a compatibility host.
    & (Join-Path $PSScriptRoot 'Prepare-LegacyRuntimeBridge.ps1') -ApplicationDirectory $application
    & (Join-Path $PSScriptRoot 'New-UpdatePackage.ps1') -InputDirectory $application -Version $version -Channel $channel -Line $Line -CompatibilityEpoch $CompatibilityEpoch -ReleaseNotes $notes -OutputDirectory $output -PackageBaseUrl $PackageBaseUrl -PackageFormat both
    $manifest = Get-Content -LiteralPath "$output/update-manifest.json" -Raw -Encoding UTF8 | ConvertFrom-Json
    [IO.File]::WriteAllText("$output/RELEASE-NOTES.md", $notes, [Text.UTF8Encoding]::new($false))
    $packages = @($manifest.package) + @($manifest.packages)
    $checksums = ($packages | ForEach-Object { "$($_.sha256)  $($_.fileName)" }) -join "`r`n"
    [IO.File]::WriteAllText("$output/SHA256SUMS.txt", "$checksums`r`n", [Text.UTF8Encoding]::new($false))
    $assetNames = ($packages | ForEach-Object { $_.fileName }) -join '、'
    [IO.File]::WriteAllText("$output/UPLOAD-INSTRUCTIONS.md", "版本：$version`r`n请将以下文件以及 update-manifest.json、SHA256SUMS.txt 上传到同一个 GitHub / GitCode Release：$assetNames。两站必须发布完全相同的文件。EXE 用于安装或修复；ZIP 解压后即可便携运行，无需安装。不要上传 application 目录。`r`n", [Text.UTF8Encoding]::new($false))
    Write-Output "Dual-format release ready: $output"
} finally { Pop-Location }

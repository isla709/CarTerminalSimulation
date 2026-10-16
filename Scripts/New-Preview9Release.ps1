param(
    [string]$BuildVersion = 'build.20261008.1',
    [string]$OutputDirectory = 'artifacts/publish/preview9-20261008.1',
    [switch]$ResumePublishedFiles
)

$ErrorActionPreference = 'Stop'
$repoDirectory = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath((Join-Path $repoDirectory $OutputDirectory))
$publishRoot = [IO.Path]::GetFullPath((Join-Path $repoDirectory 'artifacts/publish'))
if (-not $output.StartsWith($publishRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release output must be a child of artifacts/publish.'
}
if ((Test-Path -LiteralPath $output) -and -not $ResumePublishedFiles) {
    throw "Release output already exists; choose a new directory rather than overwrite: $output"
}
$version = "preview9-$BuildVersion"
if ($ResumePublishedFiles -and (Test-Path -LiteralPath (Join-Path $output "TerminalSimulation-win-x64-$version.zip"))) {
    throw 'A completed release already exists; do not overwrite its assets.'
}
$applicationDirectory = Join-Path $output 'application'
$notesPath = Join-Path $repoDirectory 'docs/preview9-release-notes.md'
$notes = Get-Content -LiteralPath $notesPath -Raw -Encoding UTF8
if (-not $notes.Contains($version)) { throw 'Release notes do not match the requested version.' }

New-Item -ItemType Directory -Path $applicationDirectory -Force | Out-Null
Push-Location $repoDirectory
try {
    if (-not $ResumePublishedFiles) {
    & dotnet publish TerminalSimulation.Wpf/TerminalSimulation.Wpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:AppVersion=preview9 "-p:BuildVersionOverride=$BuildVersion" -o $applicationDirectory
    if ($LASTEXITCODE -ne 0) { throw "Main application publish failed: $LASTEXITCODE" }
    # Publish separately: the updater's .NET 8 framework DLLs must not overwrite
    # newer NuGet DLLs (such as System.Text.Json 9) required by the main app.
    $updaterStage = Join-Path $repoDirectory ('artifacts/.qa/updater-publish-' + [Guid]::NewGuid().ToString('N'))
    & dotnet publish TerminalSimulation.Updater/TerminalSimulation.Updater.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $updaterStage
    if ($LASTEXITCODE -ne 0) { throw "Updater publish failed: $LASTEXITCODE" }
    foreach ($updaterFile in @('update.exe','update.dll','update.deps.json','update.runtimeconfig.json')) {
        Copy-Item -LiteralPath (Join-Path $updaterStage $updaterFile) -Destination (Join-Path $applicationDirectory $updaterFile) -Force
    }
    }

    # Incremental publish may keep a newer timestamp from an earlier erroneous
    # merge. Restore managed dependencies from the verified main build whenever
    # its required assembly version is higher than the published copy.
    $mainBuildDirectory = Join-Path $repoDirectory 'TerminalSimulation.Wpf/bin/Release/net8.0-windows/win-x64'
    foreach ($buildDll in Get-ChildItem -LiteralPath $mainBuildDirectory -File -Filter '*.dll') {
        $publishedDll = Join-Path $applicationDirectory $buildDll.Name
        if (-not (Test-Path -LiteralPath $publishedDll)) { continue }
        try {
            $builtVersion = [Reflection.AssemblyName]::GetAssemblyName($buildDll.FullName).Version
            $publishedVersion = [Reflection.AssemblyName]::GetAssemblyName($publishedDll).Version
            if ($builtVersion -gt $publishedVersion) {
                Copy-Item -LiteralPath $buildDll.FullName -Destination $publishedDll -Force
                Write-Output "Restored main dependency: $($buildDll.Name) $builtVersion"
            }
        }
        catch [BadImageFormatException] {
            # Native DLLs have no managed assembly metadata.
        }
    }

    foreach ($required in @(
        'TerminalSimulation.Wpf.exe', 'TerminalSimulation.Wpf.dll',
        'update.exe', 'update.dll', 'update.runtimeconfig.json',
        'hostfxr.dll', 'coreclr.dll', 'PresentationFramework.dll',
        'MapApp/index.html', 'MapApp/app.js', 'MapApp/app.css', 'Regions.json',
        'ffmpeg-native/TerminalFfmpeg.Native.dll', 'ffmpeg-native/avcodec-63.dll',
        'ffmpeg-native/avformat-63.dll', 'ffmpeg-native/avutil-61.dll',
        'ffmpeg-native/swscale-10.dll', 'ffmpeg-native/swresample-7.dll',
        'ffmpeg-native/libwinpthread-1.dll', 'ffmpeg-native/FFmpeg-GPL-LICENSE.txt'
    )) {
        if (-not (Test-Path -LiteralPath (Join-Path $applicationDirectory $required) -PathType Leaf)) {
            throw "Release dependency missing: $required"
        }
    }
    foreach ($privatePath in @('config.json', 'update-sources.json', 'utility_settings.json', 'KEY.txt', 'Workspaces', '.claude')) {
        if (Test-Path -LiteralPath (Join-Path $applicationDirectory $privatePath)) {
            throw "User data must not be included in the release: $privatePath"
        }
    }
    # Self-contained WindowsDesktop runtime publishing adds culture resources
    # even though this application uses only neutral UI. Remove only verified
    # culture directories containing exclusively generated satellite DLLs.
    foreach ($culture in @('cs','de','en','es','fr','it','ja','ko','pl','pt-BR','ru','tr','zh-Hans','zh-Hant')) {
        $cultureDirectory = [IO.Path]::GetFullPath((Join-Path $applicationDirectory $culture))
        if (-not $cultureDirectory.StartsWith($applicationDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Culture resource directory is outside the release application directory.'
        }
        if (Test-Path -LiteralPath $cultureDirectory) {
            $cultureFiles = @(Get-ChildItem -LiteralPath $cultureDirectory -Force)
            if (@($cultureFiles | Where-Object { $_.PSIsContainer -or $_.Name -notlike '*.resources.dll' }).Count -gt 0) {
                throw "Unexpected content in generated culture directory: $cultureDirectory"
            }
            foreach ($cultureFile in $cultureFiles) { Remove-Item -LiteralPath $cultureFile.FullName }
            Remove-Item -LiteralPath $cultureDirectory
        }
    }
    $satellites = @(Get-ChildItem -LiteralPath $applicationDirectory -Recurse -Filter '*.resources.dll')
    if ($satellites.Count -gt 0) { throw 'Unexpected localization resources in release.' }
    $jsonAssembly = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $applicationDirectory 'System.Text.Json.dll'))
    if ($jsonAssembly.Version.Major -lt 9) { throw 'System.Text.Json 9 required by the main application was overwritten.' }

    Copy-Item -LiteralPath $notesPath -Destination (Join-Path $output 'RELEASE-NOTES.md')
    & (Join-Path $PSScriptRoot 'New-UpdatePackage.ps1') -InputDirectory $applicationDirectory -Version $version -Channel preview -Line main -CompatibilityEpoch 1 -ReleaseNotes $notes -OutputDirectory $output

    $manifestPath = Join-Path $output 'update-manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $packagePath = Join-Path $output $manifest.package.fileName
    if ((Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.package.sha256) {
        throw 'Generated ZIP hash does not match manifest.'
    }
    if ((Get-Item -LiteralPath $packagePath).Length -ne $manifest.package.size) {
        throw 'Generated ZIP size does not match manifest.'
    }
    $checksum = "$($manifest.package.sha256)  $($manifest.package.fileName)"
    [IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), $checksum + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    Write-Output "Release ready: $output"
}
finally {
    Pop-Location
}

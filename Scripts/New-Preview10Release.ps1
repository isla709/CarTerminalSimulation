param(
    [string]$BuildVersion = 'build.20261014.1',
    [string]$OutputDirectory = 'artifacts/publish/preview10-20261014.1'
)

$ErrorActionPreference = 'Stop'
$repoDirectory = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath((Join-Path $repoDirectory $OutputDirectory))
$publishRoot = [IO.Path]::GetFullPath((Join-Path $repoDirectory 'artifacts/publish'))
if (-not $output.StartsWith($publishRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release output must be a child of artifacts/publish.'
}
if (Test-Path -LiteralPath $output) { throw "Do not overwrite an existing release: $output" }
if ($BuildVersion -notmatch '^build\.\d{8}\.\d+$') { throw 'Use a deterministic numeric release revision.' }
$version = "preview10-$BuildVersion"
$applicationDirectory = Join-Path $output 'application'
$notes = Get-Content -LiteralPath (Join-Path $repoDirectory 'docs/preview10-release-notes.md') -Raw -Encoding UTF8
$notes = "Release version: ``$version``" + [Environment]::NewLine + [Environment]::NewLine + $notes
$notes = $notes -replace '(?m)^- [^\r\n]*Preview10[^\r\n]*artifacts/publish/[^\r\n]*\r?\n', ''

[void](New-Item -ItemType Directory -Path $applicationDirectory)
Push-Location $repoDirectory
try {
    & dotnet publish TerminalSimulation.Wpf/TerminalSimulation.Wpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:IncludeXunjieCloudPlugin=false -p:AppVersion=preview10 "-p:BuildVersionOverride=$BuildVersion" -o $applicationDirectory
    if ($LASTEXITCODE -ne 0) { throw "Main publish failed: $LASTEXITCODE" }
    # Merge only updater-specific files. Its framework DLLs must not overwrite
    # main-app NuGet dependencies such as System.Text.Json 9.
    $updaterStage = Join-Path $repoDirectory ('artifacts/.qa/updater-preview10-' + [Guid]::NewGuid().ToString('N'))
    & dotnet publish TerminalSimulation.Updater/TerminalSimulation.Updater.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:IncludeXunjieCloudPlugin=false -o $updaterStage
    if ($LASTEXITCODE -ne 0) { throw "Updater publish failed: $LASTEXITCODE" }
    foreach ($file in @('update.exe','update.dll','update.deps.json','update.runtimeconfig.json')) {
        Copy-Item -LiteralPath (Join-Path $updaterStage $file) -Destination (Join-Path $applicationDirectory $file) -Force
    }
    foreach ($required in @(
        'TerminalSimulation.Wpf.exe','TerminalSimulation.Wpf.dll','update.exe','update.dll',
        'hostfxr.dll','coreclr.dll','PresentationFramework.dll','Regions.json',
        'MapApp/index.html','MapApp/app.js','MapApp/app.css','MapApp/vehicle.js','MapApp/vehicle.css',
        'MapAppOld/index.html','MapAppOld/app.js','MapAppOld/app.css','MapAppOld/vehicle.js','MapAppOld/vehicle.css',
        'MapAppOld/vendor/leaflet.js','MapAppOld/vendor/leaflet.css','MapAppOld/vendor/leaflet.polylineDecorator.js',
        'MapAppOld/vendor/leaflet.LICENSE','MapAppOld/vendor/leaflet-polylinedecorator.LICENSE',
        'ffmpeg-native/TerminalFfmpeg.Native.dll','ffmpeg-native/avcodec-63.dll',
        'ffmpeg-native/avformat-63.dll','ffmpeg-native/avutil-61.dll',
        'ffmpeg-native/swscale-10.dll','ffmpeg-native/swresample-7.dll',
        'ffmpeg-native/libwinpthread-1.dll','ffmpeg-native/FFmpeg-GPL-LICENSE.txt'
    )) {
        if (-not (Test-Path -LiteralPath (Join-Path $applicationDirectory $required) -PathType Leaf)) {
            throw "Missing release dependency: $required"
        }
    }
    foreach ($privatePath in @('config.json','utility_settings.json','update-sources.json','KEY.txt','Workspaces','.claude')) {
        if (Test-Path -LiteralPath (Join-Path $applicationDirectory $privatePath)) { throw "Unexpected user data: $privatePath" }
    }
    if (@(Get-ChildItem -LiteralPath $applicationDirectory -Recurse -Force | Where-Object { $_.Name -like '*XunjieCloud*' }).Count -gt 0) {
        throw 'Internal XunjieCloud plugin must not be published.'
    }
    # Remove only validated generated satellite directories in this fresh output.
    foreach ($culture in @('cs','de','en','es','fr','it','ja','ko','pl','pt-BR','ru','tr','zh-Hans','zh-Hant')) {
        $cultureDirectory = [IO.Path]::GetFullPath((Join-Path $applicationDirectory $culture))
        if (-not $cultureDirectory.StartsWith($applicationDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Culture path is outside the release application directory.'
        }
        if (Test-Path -LiteralPath $cultureDirectory) {
            $cultureFiles = @(Get-ChildItem -LiteralPath $cultureDirectory -Force)
            if (@($cultureFiles | Where-Object { $_.PSIsContainer -or $_.Name -notlike '*.resources.dll' }).Count -gt 0) {
                throw "Unexpected files in generated culture directory: $cultureDirectory"
            }
            foreach ($file in $cultureFiles) { Remove-Item -LiteralPath $file.FullName }
            Remove-Item -LiteralPath $cultureDirectory
        }
    }
    if (@(Get-ChildItem -LiteralPath $applicationDirectory -Recurse -Filter '*.resources.dll').Count -gt 0) { throw 'Satellite resources remain.' }
    if ([Reflection.AssemblyName]::GetAssemblyName((Join-Path $applicationDirectory 'System.Text.Json.dll')).Version.Major -lt 9) {
        throw 'System.Text.Json 9 was overwritten.'
    }
    [IO.File]::WriteAllText((Join-Path $output 'RELEASE-NOTES.md'), $notes, [Text.UTF8Encoding]::new($false))
    & (Join-Path $PSScriptRoot 'New-UpdatePackage.ps1') -InputDirectory $applicationDirectory -Version $version -Channel preview -Line main -CompatibilityEpoch 1 -ReleaseNotes $notes -OutputDirectory $output
    $manifest = Get-Content -LiteralPath (Join-Path $output 'update-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $packagePath = Join-Path $output $manifest.package.fileName
    if ((Get-Item -LiteralPath $packagePath).Length -ne $manifest.package.size -or (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.package.sha256) {
        throw 'Package integrity mismatch.'
    }
    [IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), "$($manifest.package.sha256)  $($manifest.package.fileName)" + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    $upload = Get-Content -LiteralPath (Join-Path $repoDirectory 'docs/preview10-upload-instructions.md') -Raw -Encoding UTF8
    $upload = $upload.Replace('__VERSION__', $version).Replace('__PACKAGE__', $manifest.package.fileName)
    [IO.File]::WriteAllText((Join-Path $output 'UPLOAD-INSTRUCTIONS.md'), $upload, [Text.UTF8Encoding]::new($false))
    Write-Output "Release ready: $output"
}
finally { Pop-Location }

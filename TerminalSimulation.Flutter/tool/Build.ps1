#Requires -Version 7.0
param(
  [ValidateSet('windows','android','ohos')][string]$Platform='windows',
  [ValidateSet('Debug','Release')][string]$Configuration='Debug',
  [string]$FlutterSdk,
  [string]$DevecoHome='D:\App\DevEco Studio',
  [string]$AndroidSdk,
  [string]$JavaHome,
  [ValidateSet('ohos-arm64','ohos-arm','ohos-x64')][string]$OhosTargetPlatform='ohos-arm64',
  [switch]$Codesign,
  [switch]$DebugMapWebView,
  [switch]$DebugFrameTimings,
  [switch]$OfflineDependencies,
  [switch]$VerboseBuild
)
$ErrorActionPreference='Stop'
if($DebugFrameTimings -and $Configuration -ne 'Debug'){throw 'Frame diagnostics are available only in Debug builds.'}
if($DebugMapWebView -and $Configuration -ne 'Debug'){throw 'Map WebView debugging is available only in Debug builds.'}
$projectRoot=Split-Path $PSScriptRoot -Parent
$repositoryRoot=Split-Path $projectRoot -Parent
if(-not $FlutterSdk){
  if($Platform -eq 'ohos'){$FlutterSdk=Join-Path $repositoryRoot 'bin/Debug/toolchains/flutter-oh'}
  else{
  $flutterCommand=Get-Command flutter -ErrorAction Stop
  $FlutterSdk=Split-Path (Split-Path $flutterCommand.Source -Parent) -Parent
  }
}
$dart=Join-Path $FlutterSdk 'bin/cache/dart-sdk/bin/dart.exe'
$snapshot=Join-Path $FlutterSdk 'bin/cache/flutter_tools.snapshot'
$packages=Join-Path $FlutterSdk 'packages/flutter_tools/.dart_tool/package_config.json'
# Flutter's Windows shader toolchain cannot reliably round-trip Chinese paths.
# Use an ASCII build copy, preserving the user-facing project directory name.
$outputPlatform=if($Platform -eq 'ohos' -and $OhosTargetPlatform -ne 'ohos-arm64'){$OhosTargetPlatform}else{$Platform}
$buildRoot=Join-Path $repositoryRoot "bin/$Configuration/Flutter/$outputPlatform"
$sourceRoot=Join-Path $buildRoot 'source'
New-Item -ItemType Directory -Force -Path $sourceRoot | Out-Null
$credentialFile=Join-Path $repositoryRoot 'docs/KEY.txt'
$embeddedCredential=Join-Path $sourceRoot 'assets/credentials/map-credential.bin'
# Remove an earlier builder's generated credential before any operation can fail.
if(Test-Path -LiteralPath $embeddedCredential){Remove-Item -LiteralPath $embeddedCredential -Force}
if(-not(Test-Path -LiteralPath $credentialFile -PathType Leaf)){throw 'Map credential missing: provide your local untracked docs/KEY.txt.'}
& git -C $repositoryRoot check-ignore --quiet -- docs/KEY.txt
if($LASTEXITCODE -ne 0){throw 'docs/KEY.txt must remain untracked and ignored by Git.'}
# DevEco stores builder-owned signing configuration in the ignored build copy.
# Keep its signing material and bundle identity when refreshing public sources.
$localOhosIdentity=$null
if($Platform -eq 'ohos' -and (Test-Path -LiteralPath (Join-Path $sourceRoot 'ohos/build-profile.json5'))){
  try {
    $existingProfile=Get-Content -LiteralPath (Join-Path $sourceRoot 'ohos/build-profile.json5') -Raw | ConvertFrom-Json -AsHashtable
    if($existingProfile.app.signingConfigs.Count -gt 0){
      $existingApp=Get-Content -LiteralPath (Join-Path $sourceRoot 'ohos/AppScope/app.json5') -Raw | ConvertFrom-Json -AsHashtable
      $productSignings=@{}
      foreach($product in $existingProfile.app.products){if($product.signingConfig){$productSignings[$product.name]=$product.signingConfig}}
      $localOhosIdentity=@{signingConfigs=$existingProfile.app.signingConfigs;products=$productSignings;bundleName=$existingApp.app.bundleName}
    }
  } catch {throw 'Cannot read local OHOS signing configuration; the existing build copy was not replaced.'}
}
# Synchronize the map asset set, including removal of retired HTML controls.
# All targets are resolved build-cache directories below this platform's build root.
$resolvedBuildRoot=(Resolve-Path -LiteralPath $buildRoot).Path
$oldMapDirectories=@(Get-ChildItem -LiteralPath $sourceRoot -Directory -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Name -in @('map','maps') -and $_.Parent.Name -eq 'assets' })
foreach($directory in $oldMapDirectories){
  $resolvedTarget=(Resolve-Path -LiteralPath $directory.FullName).Path
  if(-not $resolvedTarget.StartsWith($resolvedBuildRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid map build-cache target.'}
  if($directory.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Unexpected map build-cache reparse point.'}
  Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
}
foreach($sourceName in @('lib','assets',$Platform)){
  $source=Join-Path $projectRoot $sourceName
  if(-not(Test-Path -LiteralPath $source)){continue}
  foreach($file in Get-ChildItem -LiteralPath $source -Recurse -File){
    $relative=[IO.Path]::GetRelativePath($projectRoot,$file.FullName)
    if($relative.Replace('\','/') -eq 'assets/credentials/map-credential.bin'){continue}
    if($relative -match '(^|[\\/])(ephemeral|build|\.gradle|\.cxx|\.plugin_symlinks|oh_modules|\.hvigor)([\\/]|$)' -or $file.Name -eq 'local.properties'){continue}
    $target=Join-Path $sourceRoot $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
  }
}
foreach($name in @('pubspec.yaml','pubspec.lock')){Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $sourceRoot -Force}
if($Platform -eq 'ohos'){
  # The OHOS SDK exposes PlatformView types absent from upstream Flutter.
  # Select the native-composition adapter only in this SDK's build copy.
  Copy-Item -LiteralPath (Join-Path $projectRoot 'ohos/flutter/map_webview.dart.template') -Destination (Join-Path $sourceRoot 'lib/ui/map_webview.dart') -Force
  Copy-Item -LiteralPath (Join-Path $projectRoot 'ohos/flutter/harmony_chrome.dart.template') -Destination (Join-Path $sourceRoot 'lib/ui/harmony_chrome.dart') -Force
  foreach($retired in @('lib/ui/harmony_navigation.dart','ohos/entry/src/main/ets/platform/CtsNavigationView.ets')){
    $retiredPath=Join-Path $sourceRoot $retired
    if(Test-Path -LiteralPath $retiredPath -PathType Leaf){Remove-Item -LiteralPath $retiredPath -Force}
  }
  if($localOhosIdentity){
    $profilePath=Join-Path $sourceRoot 'ohos/build-profile.json5'
    $profile=Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json -AsHashtable
    $profile.app.signingConfigs=$localOhosIdentity.signingConfigs
    foreach($product in $profile.app.products){
      if($localOhosIdentity.products.ContainsKey($product.name)){$product.signingConfig=$localOhosIdentity.products[$product.name]}
    }
    $profile | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $profilePath -Encoding utf8
    $appPath=Join-Path $sourceRoot 'ohos/AppScope/app.json5'
    $app=Get-Content -LiteralPath $appPath -Raw | ConvertFrom-Json -AsHashtable
    $app.app.bundleName=$localOhosIdentity.bundleName
    $app | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $appPath -Encoding utf8
    Write-Output 'Local OHOS signing configuration and bundle identity retained in the build copy.'
  }
}
$stagedToolDirectory=Join-Path $sourceRoot 'tool'
New-Item -ItemType Directory -Force -Path $stagedToolDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'embed_map_credentials.dart') -Destination $stagedToolDirectory -Force
$configDirectory=Join-Path $buildRoot 'tool-config'
New-Item -ItemType Directory -Force -Path $configDirectory | Out-Null
$toolSettings=@{'build-dir'='build';'enable-windows-desktop'=$true;'enable-linux-desktop'=$true}
if(-not $AndroidSdk -and (Test-Path -LiteralPath 'D:\App\AndroidSDK')){$AndroidSdk='D:\App\AndroidSDK'}
if($AndroidSdk){$toolSettings['android-sdk']=$AndroidSdk}
if(-not $JavaHome -and $Platform -eq 'android' -and (Test-Path -LiteralPath 'C:\Program Files\Java\jdk-21.0.2')){$JavaHome='C:\Program Files\Java\jdk-21.0.2'}
if(-not $JavaHome -and (Test-Path -LiteralPath (Join-Path $DevecoHome 'jbr'))){$JavaHome=Join-Path $DevecoHome 'jbr'}
if($JavaHome){$toolSettings['jdk-dir']=$JavaHome}
if($Platform -eq 'ohos'){$toolSettings['ohos-sdk']=Join-Path $DevecoHome 'sdk';$toolSettings['enable-ohos']=$true}
$toolSettings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $configDirectory '.flutter_settings') -Encoding utf8
$oldAppData=$env:APPDATA
$oldPath=$env:PATH
$oldJavaHome=$env:JAVA_HOME
$oldDevecoSdk=$env:DEVECO_SDK_HOME
$oldNodeHome=$env:NODE_HOME
$oldGradleOpts=$env:GRADLE_OPTS
$oldPubCache=$env:PUB_CACHE
$oldGitCount=$env:GIT_CONFIG_COUNT
$gitConfigIndex=if($oldGitCount){[int]$oldGitCount}else{0}
$gitConfigKey="GIT_CONFIG_KEY_$gitConfigIndex"
$gitConfigValue="GIT_CONFIG_VALUE_$gitConfigIndex"
$oldGitKey=[Environment]::GetEnvironmentVariable($gitConfigKey)
$oldGitValue=[Environment]::GetEnvironmentVariable($gitConfigValue)
Push-Location $sourceRoot
try {
  $env:APPDATA=$configDirectory
  $env:GIT_CONFIG_COUNT=[string]($gitConfigIndex+1)
  [Environment]::SetEnvironmentVariable($gitConfigKey,'core.longpaths','Process')
  [Environment]::SetEnvironmentVariable($gitConfigValue,'true','Process')
  if($Platform -in @('android','ohos')){
    # Hvigor module paths and Kotlin incremental caches both require a shared drive.
    $env:PUB_CACHE=Join-Path $repositoryRoot 'bin/Debug/toolchains/pub-cache-ohos'
  }
  if($JavaHome){$env:JAVA_HOME=$JavaHome}
  if($Platform -eq 'android' -and $IsWindows){
    # Java does not automatically inherit the Windows HTTPS proxy used by other tools.
    $proxySettings=Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' -Name ProxyEnable,ProxyServer -ErrorAction SilentlyContinue
    if($proxySettings.ProxyEnable -eq 1){
      $proxyText=($proxySettings.ProxyServer -split ';' | Select-Object -First 1) -replace '^\w+=',''
      if($proxyText -notmatch '://'){$proxyText='http://'+$proxyText}
      $proxyUri=[Uri]$proxyText
      if($proxyUri.Host -and -not $proxyUri.UserInfo){
        $env:GRADLE_OPTS="$oldGradleOpts -Dhttps.proxyHost=$($proxyUri.Host) -Dhttps.proxyPort=$($proxyUri.Port) -Dhttp.proxyHost=$($proxyUri.Host) -Dhttp.proxyPort=$($proxyUri.Port) -Djdk.tls.client.protocols=TLSv1.2 -Dhttps.protocols=TLSv1.2"
        $gradleProperties=Join-Path $sourceRoot 'android/gradle.properties'
        $properties=Get-Content -LiteralPath $gradleProperties -Raw
        $properties=$properties -replace '(?m)^org.gradle.jvmargs=([^\r\n]+)', '$0 -Djdk.tls.client.protocols=TLSv1.2 -Dhttps.protocols=TLSv1.2'
        Set-Content -LiteralPath $gradleProperties -Value $properties -Encoding utf8
        @("systemProp.https.proxyHost=$($proxyUri.Host)","systemProp.https.proxyPort=$($proxyUri.Port)","systemProp.http.proxyHost=$($proxyUri.Host)","systemProp.http.proxyPort=$($proxyUri.Port)",'systemProp.https.protocols=TLSv1.2','systemProp.jdk.tls.client.protocols=TLSv1.2') | Add-Content -LiteralPath $gradleProperties -Encoding utf8
      }
    }
  }
  if($Platform -eq 'ohos'){
    $env:DEVECO_SDK_HOME=Join-Path $DevecoHome 'sdk'
    $env:NODE_HOME=Join-Path $DevecoHome 'tools/node'
    $toolPaths=@((Join-Path $DevecoHome 'tools/ohpm/bin'),(Join-Path $DevecoHome 'tools/hvigor/bin'),$env:NODE_HOME,(Join-Path $DevecoHome 'sdk/default/openharmony/toolchains'))
    $env:PATH=($toolPaths -join [IO.Path]::PathSeparator)+[IO.Path]::PathSeparator+$env:PATH
  }
  $pubArguments=@('pub','get')
  if($OfflineDependencies){$pubArguments+='--offline'}
  & $dart "--packages=$packages" $snapshot @pubArguments
  if($LASTEXITCODE -ne 0){throw 'Dependency resolution failed'}
  # Only paths cross the process boundary. Plaintext is never passed as a define,
  # environment override, source literal or command-line argument.
  & $dart "--packages=$sourceRoot/.dart_tool/package_config.json" (Join-Path $stagedToolDirectory 'embed_map_credentials.dart') $credentialFile $embeddedCredential
  if($LASTEXITCODE -ne 0){throw 'Map credential embedding failed; build stopped.'}
  $target=switch($Platform){'android'{'apk'}'ohos'{'hap'}default{$Platform}}
  $flutterArguments=@()
  if($VerboseBuild){$flutterArguments+='--verbose'}
  $flutterArguments+=@('build',$target,"--$($Configuration.ToLowerInvariant())")
  $flutterArguments+='--no-pub'
  if($Platform -eq 'ohos'){$flutterArguments+="--target-platform=$OhosTargetPlatform"}
  if($DebugMapWebView){$flutterArguments+='--dart-define=CTS_MAP_WEB_DEBUG=true'}
  if($DebugFrameTimings){$flutterArguments+='--dart-define=CTS_FRAME_TIMINGS=true'}
  if($Platform -eq 'ohos' -and -not $Codesign){$flutterArguments+='--no-codesign'}
  & $dart "--packages=$packages" $snapshot @flutterArguments
  if($LASTEXITCODE -ne 0){throw "Flutter build failed ($LASTEXITCODE)"}
  Write-Output "Build outputs: $sourceRoot/build"
} finally {
  $env:APPDATA=$oldAppData
  $env:PATH=$oldPath
  $env:JAVA_HOME=$oldJavaHome
  $env:DEVECO_SDK_HOME=$oldDevecoSdk
  $env:NODE_HOME=$oldNodeHome
  $env:GRADLE_OPTS=$oldGradleOpts
  $env:PUB_CACHE=$oldPubCache
  $env:GIT_CONFIG_COUNT=$oldGitCount
  [Environment]::SetEnvironmentVariable($gitConfigKey,$oldGitKey,'Process')
  [Environment]::SetEnvironmentVariable($gitConfigValue,$oldGitValue,'Process')
  Pop-Location
  # The finished bundle contains the encrypted asset; do not retain generated input.
  if(Test-Path -LiteralPath $embeddedCredential){Remove-Item -LiteralPath $embeddedCredential -Force}
}

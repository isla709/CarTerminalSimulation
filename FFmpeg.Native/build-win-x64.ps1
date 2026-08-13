param([string] $FfmpegDevelopmentPackage = (Join-Path $PSScriptRoot '..\ThirdParty\ffmpeg'))

$ErrorActionPreference = 'Stop'
$ffmpeg = (Resolve-Path -LiteralPath $FfmpegDevelopmentPackage).Path
$gcc = 'D:\App\mingw64\bin\gcc.exe'
$mingwBin = Split-Path -Parent $gcc
$env:Path = "$mingwBin;$env:Path"
$artifacts = Join-Path $PSScriptRoot 'artifacts\win-x64'
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$command = '"{0}" -std=c17 -O2 -shared -I "{1}" -I "{2}" "{3}" -L "{4}" -lavformat -lavcodec -lavutil -lswscale -lswresample -static-libgcc -o "{5}"' -f `
  $gcc, (Join-Path $ffmpeg 'include'), (Join-Path $PSScriptRoot 'native'),
  (Join-Path $PSScriptRoot 'native\terminal_ffmpeg.c'), (Join-Path $ffmpeg 'lib'),
  (Join-Path $artifacts 'TerminalFfmpeg.Native.dll')
& $env:ComSpec /d /s /c $command
$nativeExitCode = $LASTEXITCODE
if ($nativeExitCode -ne 0) { throw "Native bridge compilation failed: $nativeExitCode" }

'avformat-63.dll','avcodec-63.dll','avutil-61.dll','swscale-10.dll','swresample-7.dll' | ForEach-Object {
  Copy-Item -LiteralPath (Join-Path $ffmpeg "bin\$_") -Destination $artifacts -Force
}
Copy-Item -LiteralPath (Join-Path $ffmpeg 'LICENSE.txt') -Destination (Join-Path $artifacts 'FFmpeg-GPL-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $mingwBin 'libwinpthread-1.dll') -Destination $artifacts -Force

$smokeCommand = '"{0}" -municode -O2 "{1}" -o "{2}"' -f $gcc,
  (Join-Path $PSScriptRoot 'native\smoke_test.c'), (Join-Path $artifacts 'TerminalFfmpeg.SmokeTest.exe')
& $env:ComSpec /d /s /c $smokeCommand
if ($LASTEXITCODE -ne 0) { throw "Smoke test compilation failed: $LASTEXITCODE" }

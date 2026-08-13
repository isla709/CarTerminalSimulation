param(
    [Parameter(Mandatory = $true)] [string] $FfmpegSource,
    [Parameter(Mandatory = $true)] [string] $X264Prefix
)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $FfmpegSource).Path
$x264 = (Resolve-Path -LiteralPath $X264Prefix).Path
$prefix = Join-Path $PSScriptRoot 'build\win-x64'

$configure = @(
    '--target-os=mingw32', '--arch=x86_64', '--enable-shared', '--disable-static',
    '--disable-programs', '--disable-doc', '--disable-debug', '--disable-autodetect',
    '--disable-everything', '--enable-gpl', '--enable-version3', '--enable-libx264',
    '--enable-protocol=file',
    '--enable-demuxer=mov,matroska,avi,h264,aac',
    '--enable-muxer=h264,adts,alaw,image2',
    '--enable-decoder=h264,hevc,mpeg4,mpeg2video,aac,mp3,pcm_s16le,pcm_s24le,pcm_f32le',
    '--enable-encoder=libx264,aac,pcm_alaw,mjpeg',
    '--enable-parser=h264,hevc,aac,mpegaudio,mpeg4video',
    '--enable-bsf=h264_mp4toannexb',
    "--prefix=$($prefix -replace '\\','/')",
    "--extra-cflags=-I$($x264 -replace '\\','/')/include",
    "--extra-ldflags=-L$($x264 -replace '\\','/')/lib"
)

Write-Host 'Run the following inside an MSYS2 MinGW64 shell:'
Write-Host "cd '$($source -replace '\\','/')'"
Write-Host "./configure $($configure -join ' ')"
Write-Host 'make -j && make install'
Write-Host 'Then build native/terminal_ffmpeg.c as TerminalFfmpeg.Native.dll against that prefix.'

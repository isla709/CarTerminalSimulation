# Terminal FFmpeg Native Runtime

This directory builds the in-process media runtime used by the WPF application. It does not build or ship `ffmpeg.exe` or `ffprobe.exe`.

## License

The selected build enables `libx264` and is therefore distributed under GPL-compatible terms. Release packages must include FFmpeg and x264 license texts, corresponding-source instructions, the exact build script/configuration, and notices for local modifications.

## Required artifacts

Run `build-win-x64.ps1` in a reproducible MSYS2/MinGW environment. Copy the resulting files to `artifacts/win-x64/`:

- `TerminalFfmpeg.Native.dll`
- `avcodec-*.dll`, `avformat-*.dll`, `avutil-*.dll`
- `swscale-*.dll`, `swresample-*.dll`
- `libx264-*.dll` and their required runtime DLLs
- `COPYING.GPLv2`, `COPYING.GPLv3`, FFmpeg source/build offer and x264 license

The normal WPF Debug and Release build copies this directory to the output `ffmpeg-native/` folder. This repository does not use single-file publishing, and the native directory must remain beside the application.

`TerminalFfmpeg.Native.dll` must implement `tf_probe`, `tf_transcode`, `tf_thumbnail`, and `tf_last_error` declared in `native/terminal_ffmpeg.h`. The native implementation should be built from FFmpeg's `transcode.c`/`encode_video.c` examples and must use the send/receive API.

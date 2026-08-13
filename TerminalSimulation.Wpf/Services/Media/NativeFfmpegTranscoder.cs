using System.Runtime.InteropServices;
using System.Text;
using System.IO;

namespace TerminalSimulation.Wpf.Services.Media;

internal sealed class NativeFfmpegTranscoder : IInProcessMediaTranscoder
{
    private const string LibraryName = "TerminalFfmpeg.Native";
    private static readonly string NativeDirectory = Path.Combine(Helpers.PathHelper.AppDir, "ffmpeg-native");
    private static readonly NativeProgressCallback ProgressCallback = ReportProgress;
    private static readonly object ProgressLock = new();
    private static IProgress<double>? _activeProgress;

    static NativeFfmpegTranscoder()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeFfmpegTranscoder).Assembly, ResolveLibrary);
    }

    public MediaProbeResult Probe(string inputPath)
    {
        EnsureAvailable();
        ThrowIfFailed(tf_probe(inputPath, out var fps, out var hasAudio), "读取媒体信息");
        return new MediaProbeResult(fps > 0 ? fps : 25, hasAudio != 0);
    }

    public async Task TranscodeAsync(MediaTranscodeRequest request, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        EnsureAvailable();
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (ProgressLock)
            {
                _activeProgress = progress;
                try
                {
                    ThrowIfFailed(tf_transcode(
                        request.InputPath, request.H264OutputPath, request.G711AOutputPath, request.AacOutputPath,
                        request.FrameRate, request.GopSize, request.MaxBitrate, request.BufferSize,
                        ProgressCallback, IntPtr.Zero), "转码");
                }
                finally { _activeProgress = null; }
            }
            cancellationToken.ThrowIfCancellationRequested();
        }, cancellationToken);
    }

    public Task WriteThumbnailAsync(string inputPath, string outputPath, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAvailable();
            ThrowIfFailed(tf_thumbnail(inputPath, outputPath), "生成缩略图");
        }, cancellationToken);

    private static void EnsureAvailable()
    {
        var bridge = Path.Combine(NativeDirectory, "TerminalFfmpeg.Native.dll");
        if (!File.Exists(bridge))
        {
            throw new FileNotFoundException("发布包缺少进程内 FFmpeg 模块，请构建 FFmpeg.Native 后重新发布。", bridge);
        }
    }

    private static IntPtr ResolveLibrary(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal)) return IntPtr.Zero;
        return NativeLibrary.Load(Path.Combine(NativeDirectory, "TerminalFfmpeg.Native.dll"));
    }

    private static void ThrowIfFailed(int result, string operation)
    {
        if (result >= 0) return;
        var buffer = new StringBuilder(1024);
        tf_last_error(buffer, buffer.Capacity);
        throw new InvalidOperationException($"FFmpeg {operation}失败: {buffer}");
    }

    private static void ReportProgress(double value, IntPtr userData) => _activeProgress?.Report(Math.Clamp(value, 0, 1));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NativeProgressCallback(double progress, IntPtr userData);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int tf_probe(string inputPath, out double frameRate, out int hasAudio);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int tf_transcode(string inputPath, string h264OutputPath, string? g711aOutputPath,
        string? aacOutputPath, double frameRate, int gopSize, int maxBitrate, int bufferSize,
        NativeProgressCallback callback, IntPtr userData);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int tf_thumbnail(string inputPath, string outputPath);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern void tf_last_error(StringBuilder buffer, int capacity);
}

namespace TerminalSimulation.Wpf.Services.Media;

internal sealed record MediaProbeResult(double FrameRate, bool HasAudio);

internal sealed record MediaTranscodeRequest(
    string InputPath,
    string H264OutputPath,
    string? G711AOutputPath,
    string? AacOutputPath,
    double FrameRate,
    int GopSize,
    int MaxBitrate,
    int BufferSize);

internal interface IInProcessMediaTranscoder
{
    MediaProbeResult Probe(string inputPath);
    Task TranscodeAsync(MediaTranscodeRequest request, IProgress<double>? progress, CancellationToken cancellationToken);
    Task WriteThumbnailAsync(string inputPath, string outputPath, CancellationToken cancellationToken);
}

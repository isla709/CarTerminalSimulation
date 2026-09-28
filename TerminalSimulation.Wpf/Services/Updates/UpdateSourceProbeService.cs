using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;

namespace TerminalSimulation.Wpf.Services.Updates;

internal sealed record UpdateSourceProbeResult(
    UpdateDownloadSource Source,
    bool IsAvailable,
    long LatencyMilliseconds,
    long BytesRead,
    double? BytesPerSecond,
    string Detail);

internal sealed class UpdateSourceProbeService
{
    private const int ProbeBytes = 256 * 1024;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(8);
    private static readonly HttpClient SharedHttpClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient _httpClient;

    public UpdateSourceProbeService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
    }

    public async Task<UpdateSourceProbeResult> ProbeAsync(
        UpdateDownloadSource source,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source.PackageUri);
            request.Headers.Range = new RangeHeaderValue(0, ProbeBytes - 1);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            var latency = stopwatch.ElapsedMilliseconds;
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateSourceProbeResult(
                    source, false, latency, 0, null,
                    $"HTTP {(int)response.StatusCode}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[32 * 1024];
            var totalRead = 0;
            while (totalRead < ProbeBytes)
            {
                var read = await stream.ReadAsync(
                    buffer.AsMemory(0, Math.Min(buffer.Length, ProbeBytes - totalRead)),
                    timeout.Token);
                if (read == 0) break;
                totalRead += read;
            }

            stopwatch.Stop();
            var transferSeconds = Math.Max(0.001, stopwatch.Elapsed.TotalSeconds - latency / 1000d);
            double? speed = totalRead > 0 ? totalRead / transferSeconds : null;
            return new UpdateSourceProbeResult(
                source,
                true,
                latency,
                totalRead,
                speed,
                totalRead > 0 ? "连接正常" : "已连接");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new UpdateSourceProbeResult(
                source, false, stopwatch.ElapsedMilliseconds, 0, null, "连接超时");
        }
        catch (Exception ex)
        {
            return new UpdateSourceProbeResult(
                source, false, stopwatch.ElapsedMilliseconds, 0, null,
                FriendlyError(ex));
        }
    }

    internal static IReadOnlyList<UpdateSourceProbeResult> Rank(
        IEnumerable<UpdateSourceProbeResult> results) => results
        .OrderByDescending(result => result.IsAvailable)
        .ThenByDescending(result => result.BytesPerSecond ?? 0)
        .ThenBy(result => result.LatencyMilliseconds)
        .ThenByDescending(result => result.Source.Priority)
        .ToList();

    private static string FriendlyError(Exception exception) => exception switch
    {
        HttpRequestException { InnerException: not null } request => request.InnerException!.Message,
        HttpRequestException request => request.Message,
        _ => exception.Message
    };
}

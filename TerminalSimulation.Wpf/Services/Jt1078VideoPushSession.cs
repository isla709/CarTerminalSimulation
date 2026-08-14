using TerminalSimulation.Protocol;

namespace TerminalSimulation.Wpf.Services;

internal sealed class Jt1078VideoPushSession : IVideoPushSession
{
    private readonly JT1078Pusher _pusher;
    private readonly string _host;
    private readonly int _port;

    public Jt1078VideoPushSession(string host, int port, string simCard, byte channel, string videoFile, string audioFile,
        int dataType, int audioCodec, double targetFps, bool constantFps)
    {
        _host = host;
        _port = port;
        _pusher = new JT1078Pusher(simCard, channel, videoFile, audioFile, dataType, audioCodec, targetFps, constantFps);
        _pusher.OnLog += message => Log?.Invoke(message);
        _pusher.OnStatusUpdate += message => StatusUpdated?.Invoke(message);
        _pusher.OnDisconnected += () => { IsRunning = false; Disconnected?.Invoke(); };
        _pusher.OnError += exception => Error?.Invoke(exception);
    }

    public bool IsRunning { get; private set; }
    public long TotalPushedBytes => _pusher.TotalPushedBytes;
    public event Action<string>? Log;
    public event Action<string>? StatusUpdated;
    public event Action? Disconnected;
    public event Action<Exception>? Error;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _pusher.StartAsync(_host, _port, cancellationToken).ConfigureAwait(false);
        IsRunning = true;
    }

    public async Task StopAsync()
    {
        await _pusher.StopAsync().ConfigureAwait(false);
        IsRunning = false;
    }

    public async ValueTask DisposeAsync()
    {
        await _pusher.DisposeAsync().ConfigureAwait(false);
        IsRunning = false;
    }
}

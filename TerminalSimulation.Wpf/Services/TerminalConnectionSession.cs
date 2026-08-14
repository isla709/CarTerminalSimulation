using TerminalSimulation.Network;

namespace TerminalSimulation.Wpf.Services;

internal sealed class TerminalConnectionSession : IConnectionSession
{
    private readonly TerminalNetworkClient _client = new();
    private long _connectionId;

    public TerminalConnectionSession()
    {
        _client.OnDataReceived += data => DataReceived?.Invoke(data);
        _client.OnDisconnected += () => Disconnected?.Invoke();
        _client.OnError += exception => Error?.Invoke(exception);
    }

    public bool IsConnected => _client.IsConnected;
    public long ConnectionId => Volatile.Read(ref _connectionId);
    public event Action<byte[]>? DataReceived;
    public event Action? Disconnected;
    public event Action<Exception>? Error;

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        await _client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _connectionId);
    }

    public Task SendAsync(byte[] data, CancellationToken cancellationToken = default) =>
        _client.SendAsync(data, cancellationToken);

    public Task DisconnectAsync() => _client.DisconnectAsync();
    public ValueTask DisposeAsync() => _client.DisposeAsync();
}

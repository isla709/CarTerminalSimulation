using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace TerminalSimulation.Network;

public sealed class TerminalNetworkClient : IDisposable, IAsyncDisposable
{
    private const int MaxFrameLength = 64 * 1024;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private TcpClient? _tcpClient;
    private NetworkStream? _networkStream;
    private CancellationTokenSource? _sessionCts;
    private Channel<WriteRequest>? _sendQueue;
    private Task? _receiveTask;
    private Task? _sendTask;
    private long _sessionId;
    private bool _disposed;

    public event Action<byte[]>? OnDataReceived;
    public event Action? OnDisconnected;
    public event Action<Exception>? OnError;

    public bool IsConnected => _tcpClient?.Connected == true && _sessionCts?.IsCancellationRequested == false;

    public async Task ConnectAsync(string ip, int port, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await DisconnectAsync().ConfigureAwait(false);

        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var client = new TcpClient();
            await client.ConnectAsync(ip, port, cancellationToken).ConfigureAwait(false);
            var stream = client.GetStream();
            var cts = new CancellationTokenSource();
            var queue = Channel.CreateUnbounded<WriteRequest>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
            var sessionId = Interlocked.Increment(ref _sessionId);

            _tcpClient = client;
            _networkStream = stream;
            _sessionCts = cts;
            _sendQueue = queue;
            _sendTask = SendLoopAsync(sessionId, stream, queue.Reader, cts.Token);
            _receiveTask = ReceiveLoopAsync(sessionId, stream, cts.Token);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task SendAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0) return;

        var queue = _sendQueue;
        var sessionId = Volatile.Read(ref _sessionId);
        if (!IsConnected || queue == null)
        {
            throw new InvalidOperationException("Not connected to server.");
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new WriteRequest(sessionId, data.ToArray(), completion);
        await queue.Writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SendLoopAsync(long sessionId, NetworkStream stream, ChannelReader<WriteRequest> reader, CancellationToken token)
    {
        Exception? failure = null;
        try
        {
            await foreach (var request in reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                if (request.SessionId != sessionId)
                {
                    request.Completion.TrySetException(new IOException("The connection session has changed."));
                    continue;
                }

                try
                {
                    await stream.WriteAsync(request.Data, token).ConfigureAwait(false);
                    request.Completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    request.Completion.TrySetException(ex);
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
        {
            failure = ex;
            OnError?.Invoke(ex);
        }
        finally
        {
            while (reader.TryRead(out var pending))
            {
                pending.Completion.TrySetException(failure ?? new IOException("The connection was closed."));
            }
            RequestSessionStop(sessionId);
        }
    }

    private async Task ReceiveLoopAsync(long sessionId, NetworkStream stream, CancellationToken token)
    {
        var buffer = new byte[4096];
        var currentPacket = new List<byte>(4096);
        var isReadingPacket = false;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var bytesRead = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                if (bytesRead == 0) break;

                for (var i = 0; i < bytesRead; i++)
                {
                    var value = buffer[i];
                    if (value == 0x7E)
                    {
                        if (!isReadingPacket)
                        {
                            isReadingPacket = true;
                            currentPacket.Clear();
                            currentPacket.Add(value);
                        }
                        else if (currentPacket.Count > 1)
                        {
                            currentPacket.Add(value);
                            OnDataReceived?.Invoke(currentPacket.ToArray());
                            currentPacket.Clear();
                            currentPacket.Add(value);
                        }
                    }
                    else if (isReadingPacket)
                    {
                        currentPacket.Add(value);
                        if (currentPacket.Count > MaxFrameLength)
                        {
                            throw new InvalidDataException($"JT808 frame exceeded {MaxFrameLength} bytes.");
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or InvalidDataException)
        {
            OnError?.Invoke(ex);
        }
        finally
        {
            if (RequestSessionStop(sessionId)) OnDisconnected?.Invoke();
        }
    }

    private bool RequestSessionStop(long sessionId)
    {
        if (Volatile.Read(ref _sessionId) != sessionId) return false;
        try { _sessionCts?.Cancel(); } catch (ObjectDisposedException) { }
        try { _sendQueue?.Writer.TryComplete(); } catch (ObjectDisposedException) { }
        try { _networkStream?.Close(); } catch (IOException) { }
        try { _tcpClient?.Close(); } catch (SocketException) { }
        return true;
    }

    public async Task DisconnectAsync()
    {
        Task? receiveTask;
        Task? sendTask;
        CancellationTokenSource? cts;

        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            Interlocked.Increment(ref _sessionId);
            cts = _sessionCts;
            receiveTask = _receiveTask;
            sendTask = _sendTask;
            _sessionCts = null;
            _receiveTask = null;
            _sendTask = null;
            _sendQueue?.Writer.TryComplete();
            _sendQueue = null;
            try { cts?.Cancel(); } catch (ObjectDisposedException) { }
            _networkStream?.Dispose();
            _networkStream = null;
            _tcpClient?.Dispose();
            _tcpClient = null;
        }
        finally
        {
            _lifecycleLock.Release();
        }

        var tasks = new[] { receiveTask, sendTask }.Where(t => t != null).Cast<Task>().ToArray();
        if (tasks.Length > 0)
        {
            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        cts?.Dispose();
    }

    public void Disconnect() => DisconnectAsync().GetAwaiter().GetResult();

    public void Dispose()
    {
        if (_disposed) return;
        Disconnect();
        _disposed = true;
        _lifecycleLock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await DisconnectAsync().ConfigureAwait(false);
        _disposed = true;
        _lifecycleLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record WriteRequest(long SessionId, byte[] Data, TaskCompletionSource Completion);
}

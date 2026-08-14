namespace TerminalSimulation.Wpf.Services;

internal interface IConnectionSession : IAsyncDisposable
{
    bool IsConnected { get; }
    long ConnectionId { get; }
    event Action<byte[]>? DataReceived;
    event Action? Disconnected;
    event Action<Exception>? Error;
    Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);
    Task SendAsync(byte[] data, CancellationToken cancellationToken = default);
    Task DisconnectAsync();
}

internal interface IAppLogger
{
    void Info(string category, string message, long? connectionId = null, byte? channel = null, ushort? messageId = null);
    void Error(string category, string message, Exception exception, long? connectionId = null, byte? channel = null, ushort? messageId = null);
}

internal interface IConfigStore<T>
{
    T Load();
    Task SaveAsync(T value, CancellationToken cancellationToken = default);
}

internal interface ICredentialStore
{
    string Protect(string value);
    bool TryUnprotect(string protectedValue, out string value);
}

internal interface ISerialPortService : IDisposable
{
    bool IsOpen { get; }
    event Action<byte[]>? DataReceived;
    IReadOnlyList<string> GetPortNames();
    void Open(string portName, int baudRate);
    void Write(byte[] data);
    void Close();
}

internal interface ITtsService
{
    IReadOnlyList<string> GetInstalledVoices();
    Task SpeakAsync(string text, string? voiceName = null, CancellationToken cancellationToken = default);
}

internal interface ILocationSimulationService
{
    Task RunAsync(IReadOnlyList<ViewModels.GeoPoint> path, Func<double> speedKph,
        Action<ViewModels.GeoPoint, int> positionChanged, CancellationToken cancellationToken);
}

internal interface IVideoPushSession : IAsyncDisposable
{
    bool IsRunning { get; }
    long TotalPushedBytes { get; }
    event Action<string>? Log;
    event Action<string>? StatusUpdated;
    event Action? Disconnected;
    event Action<Exception>? Error;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}

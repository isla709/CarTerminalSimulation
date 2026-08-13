namespace TerminalSimulation.Wpf.Services;

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

internal interface IVideoPushSession : IAsyncDisposable
{
    bool IsRunning { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}

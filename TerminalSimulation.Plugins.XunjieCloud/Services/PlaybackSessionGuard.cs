namespace TerminalSimulation.Plugins.XunjieCloud.Services;

internal sealed class PlaybackSessionGuard : IDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _cts;
    private int _generation;

    public (int Generation, CancellationToken Token) Begin()
    {
        lock (_sync)
        {
            CancelCore();
            _cts = new CancellationTokenSource();
            return (_generation, _cts.Token);
        }
    }

    public int CurrentGeneration { get { lock (_sync) return _generation; } }
    public CancellationToken CurrentToken { get { lock (_sync) return _cts?.Token ?? new CancellationToken(true); } }

    public bool IsCurrent(int generation, CancellationToken token)
    {
        lock (_sync)
            return _cts != null && generation == _generation && token == _cts.Token && !token.IsCancellationRequested;
    }

    public void Cancel() { lock (_sync) CancelCore(); }

    private void CancelCore()
    {
        _generation++;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Cancel();
}

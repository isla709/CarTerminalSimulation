using System.Text.Json;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TerminalSimulation.Wpf.Services;

internal sealed class AtomicJsonConfigStore<T> : IConfigStore<T> where T : new()
{
    private readonly string _path;
    private readonly Action<string>? _warning;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public AtomicJsonConfigStore(string path, Action<string>? warning = null)
    {
        _path = Path.GetFullPath(path);
        _warning = warning;
    }

    public T Load()
    {
        RecoverTemporaryFile();
        if (!File.Exists(_path)) return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(_path), _options) ?? new T();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            var corruptPath = $"{_path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            try { File.Copy(_path, corruptPath, overwrite: false); }
            catch (Exception copyException) { _warning?.Invoke($"配置损坏且备份失败: {copyException.Message}"); }
            _warning?.Invoke($"配置加载失败，已回退默认值并保留损坏副本: {ex.Message}");
            return new T();
        }
    }

    public async Task SaveAsync(T value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporaryPath = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = JsonSerializer.Serialize(value, _options);
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            if (File.Exists(_path))
            {
                File.Replace(temporaryPath, _path, _path + ".bak", ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, _path);
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private void RecoverTemporaryFile()
    {
        var temporaryPath = _path + ".tmp";
        if (!File.Exists(_path) && File.Exists(temporaryPath))
        {
            try { File.Move(temporaryPath, _path); }
            catch (IOException ex) { _warning?.Invoke($"恢复临时配置失败: {ex.Message}"); }
        }
    }
}

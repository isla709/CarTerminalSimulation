using TerminalSimulation.Wpf.Services;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class AtomicJsonConfigStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"terminal-config-{Guid.NewGuid():N}");
    private string ConfigPath => Path.Combine(_directory, "config.json");

    [Fact]
    public async Task SaveAsync_ConcurrentRequestsAlwaysLeaveValidJson()
    {
        var store = new AtomicJsonConfigStore<TestConfig>(ConfigPath);
        await Task.WhenAll(Enumerable.Range(0, 30).Select(index => store.SaveAsync(new TestConfig { Value = index })));
        var loaded = store.Load();
        Assert.InRange(loaded.Value, 0, 29);
        Assert.False(File.Exists(ConfigPath + ".tmp"));
    }

    [Fact]
    public void Load_RecoversTemporaryFileWhenPrimaryIsMissing()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(ConfigPath + ".tmp", "{\"Value\":42}");
        var store = new AtomicJsonConfigStore<TestConfig>(ConfigPath);
        Assert.Equal(42, store.Load().Value);
        Assert.True(File.Exists(ConfigPath));
    }

    [Fact]
    public void Load_CorruptJsonCreatesBackupAndReturnsDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(ConfigPath, "{broken");
        var warnings = new List<string>();
        var store = new AtomicJsonConfigStore<TestConfig>(ConfigPath, warnings.Add);
        Assert.Equal(0, store.Load().Value);
        Assert.Single(Directory.GetFiles(_directory, "config.json.corrupt-*"));
        Assert.NotEmpty(warnings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    public sealed class TestConfig
    {
        public int Value { get; set; }
    }
}

namespace TerminalSimulation.Wpf.Services;

public enum ApplicationLogCategory { Communication, System, Plugin }

internal sealed record ApplicationLogEntry(long Sequence, DateTimeOffset Timestamp,
    ApplicationLogCategory Category, string Source, string Message, string RawData);

/// <summary>Bounded process log history, independent of console visibility and UI filters.</summary>
internal sealed class ApplicationLog
{
    internal const int Capacity = 2000;
    internal static ApplicationLog Shared { get; } = new();
    private readonly object _gate = new();
    private readonly Queue<ApplicationLogEntry> _entries = new();
    private long _sequence;
    internal event Action? Changed;

    internal static ApplicationLogCategory Classify(string source) => source switch
    {
        "插件" => ApplicationLogCategory.Plugin,
        "发送" or "发送解析" or "接收" or "接收解析" or "网络" or "协议" or "解析异常" or
            "心跳" or "自动汇报" or "音视频" or "音视频控制" => ApplicationLogCategory.Communication,
        _ => ApplicationLogCategory.System
    };

    internal void Write(string source, string message, ApplicationLogCategory? category = null, string? rawData = null)
    {
        lock (_gate)
        {
            _entries.Enqueue(new ApplicationLogEntry(++_sequence, DateTimeOffset.Now,
                category ?? Classify(source), source, message, rawData ?? ExtractRaw(message)));
            while (_entries.Count > Capacity) _entries.Dequeue();
        }
        NotifyChanged();
    }

    internal ApplicationLogEntry[] Snapshot() { lock (_gate) return _entries.ToArray(); }
    internal void Clear() { lock (_gate) _entries.Clear(); NotifyChanged(); }

    private void NotifyChanged()
    {
        if (Changed is not { } handlers) return;
        foreach (Action handler in handlers.GetInvocationList())
            try { handler(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }

    private static string ExtractRaw(string message)
    {
        var start = message.IndexOf("RAW: ", StringComparison.Ordinal);
        if (start < 0) return "";
        var value = message[(start + 5)..].Trim();
        return value.Length > 0 && value.All(c => Uri.IsHexDigit(c) || char.IsWhiteSpace(c)) ? value : "";
    }
}

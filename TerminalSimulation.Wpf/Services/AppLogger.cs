namespace TerminalSimulation.Wpf.Services;

internal sealed class AppLogger : IAppLogger
{
    public void Info(string category, string message, long? connectionId = null, byte? channel = null, ushort? messageId = null)
    {
        var context = FormatContext(connectionId, channel, messageId);
        if (category is "发送" or "发送解析" or "接收" or "接收解析")
            ConsoleLogger.LogNetwork(category, Array.Empty<byte>(), context + message);
        else
            ConsoleLogger.LogDebug(category, context + message);
    }

    public void Error(string category, string message, Exception exception, long? connectionId = null, byte? channel = null, ushort? messageId = null) =>
        ConsoleLogger.LogError(category, FormatContext(connectionId, channel, messageId) + message, exception);

    private static string FormatContext(long? connectionId, byte? channel, ushort? messageId)
    {
        var parts = new List<string>(3);
        if (connectionId.HasValue) parts.Add($"connection={connectionId}");
        if (channel.HasValue) parts.Add($"channel={channel}");
        if (messageId.HasValue) parts.Add($"msg=0x{messageId:X4}");
        return parts.Count == 0 ? string.Empty : $"[{string.Join(" ", parts)}] ";
    }
}

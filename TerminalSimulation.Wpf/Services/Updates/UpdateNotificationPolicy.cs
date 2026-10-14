namespace TerminalSimulation.Wpf.Services.Updates;

// The update catalog has already applied line/channel/compatibility rules.
// Do not announce switches, downgrades or the same release repeatedly.
internal sealed class UpdateNotificationPolicy
{
    private readonly HashSet<string> _announced = new(StringComparer.OrdinalIgnoreCase);

    public bool TryAnnounce(string currentVersion, string? availableVersion, bool enabled, bool interactive)
    {
        if (string.IsNullOrWhiteSpace(availableVersion) || UpdateVersionComparer.Compare(availableVersion, currentVersion) <= 0)
            return false;
        var version = availableVersion.Trim();
        if (interactive) { _announced.Add(version); return false; }
        return enabled && _announced.Add(version);
    }
}

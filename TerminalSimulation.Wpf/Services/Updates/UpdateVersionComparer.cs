using System.Text.RegularExpressions;

namespace TerminalSimulation.Wpf.Services.Updates;

internal static partial class UpdateVersionComparer
{
    public static int Compare(string left, string right)
    {
        var leftPreview = ParsePreview(left);
        var rightPreview = ParsePreview(right);
        if (leftPreview is not null && rightPreview is not null)
        {
            var comparison = leftPreview.Value.Preview.CompareTo(rightPreview.Value.Preview);
            if (comparison != 0) return comparison;
            comparison = leftPreview.Value.BuildDate.CompareTo(rightPreview.Value.BuildDate);
            if (comparison != 0) return comparison;

            // Generated local builds use a random suffix. Only numeric release revisions are ordered
            // within the same day, otherwise they are treated as the same build to avoid update loops.
            if (leftPreview.Value.NumericRevision is { } leftRevision &&
                rightPreview.Value.NumericRevision is { } rightRevision)
                return leftRevision.CompareTo(rightRevision);
            return 0;
        }

        if (Version.TryParse(left.TrimStart('v', 'V'), out var leftVersion) &&
            Version.TryParse(right.TrimStart('v', 'V'), out var rightVersion))
            return leftVersion.CompareTo(rightVersion);

        return StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }

    private static PreviewVersion? ParsePreview(string value)
    {
        var match = PreviewVersionRegex().Match(value.Trim());
        if (!match.Success || !int.TryParse(match.Groups["preview"].Value, out var preview)) return null;
        var date = match.Groups["date"].Success && int.TryParse(match.Groups["date"].Value, out var parsedDate)
            ? parsedDate
            : 0;
        int? revision = match.Groups["revision"].Success && int.TryParse(match.Groups["revision"].Value, out var parsedRevision)
            ? parsedRevision
            : null;
        return new PreviewVersion(preview, date, revision);
    }

    [GeneratedRegex("^preview(?<preview>\\d+)(?:-build\\.(?<date>\\d{8})(?:\\.(?<revision>[A-Za-z0-9]+))?)?$", RegexOptions.IgnoreCase)]
    private static partial Regex PreviewVersionRegex();

    private readonly record struct PreviewVersion(int Preview, int BuildDate, int? NumericRevision);
}

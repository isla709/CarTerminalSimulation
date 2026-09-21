using System.Text.Json.Serialization;

namespace TerminalSimulation.Wpf.Services.Updates;

internal sealed class UpdateSourceConfiguration
{
    public int SchemaVersion { get; set; } = 1;
    public bool AutoCheck { get; set; } = true;
    public double CheckIntervalHours { get; set; } = 12;
    public string Channel { get; set; } = "preview";
    public string Line { get; set; } = "main";
    public List<UpdateSourceDefinition> Sources { get; set; } = [];
}

internal sealed class UpdateSourceDefinition
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; }
    public string? Owner { get; set; }
    public string? Repository { get; set; }
    public string? ApiBaseUrl { get; set; }
    public string? ManifestAssetName { get; set; }
    public string? ManifestUrl { get; set; }
    public bool AllowInsecureHttp { get; set; }
}

internal sealed class UpdateManifest
{
    public int SchemaVersion { get; set; } = 2;
    public string Product { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Channel { get; set; } = "preview";
    public string Line { get; set; } = string.Empty;
    public int CompatibilityEpoch { get; set; } = 1;
    public DateTimeOffset? PublishedAt { get; set; }
    public string? ReleaseNotes { get; set; }
    public string? MinimumUpdaterVersion { get; set; }
    public UpdatePackage Package { get; set; } = new();
    public List<string> Delete { get; set; } = [];
}

internal sealed class UpdateCatalog
{
    public int SchemaVersion { get; set; } = 2;
    public string Product { get; set; } = string.Empty;
    public string Line { get; set; } = string.Empty;
    public List<UpdateManifest> Versions { get; set; } = [];
}

internal sealed class UpdatePackage
{
    public string FileName { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public long? Size { get; set; }
}

internal sealed record UpdateCandidate(
    string Version,
    string Channel,
    string Line,
    int CompatibilityEpoch,
    DateTimeOffset? PublishedAt,
    string ReleaseNotes,
    string SourceName,
    Uri PackageUri,
    string Sha256,
    long? PackageSize,
    bool AllowInsecureHttp,
    IReadOnlyList<string> Delete);

internal sealed record UpdateCheckResult(
    UpdateCandidate? Candidate,
    IReadOnlyList<UpdateCandidate> Candidates,
    bool HasEnabledSources,
    IReadOnlyList<string> Diagnostics);

internal sealed class GitHubRelease
{
    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAsset> Assets { get; set; } = [];
}

internal sealed class GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("digest")]
    public string? Digest { get; set; }
}

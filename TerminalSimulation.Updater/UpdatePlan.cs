using System.Text.Json.Serialization;

namespace TerminalSimulation.Updater;

public sealed class UpdatePlan
{
    public int SchemaVersion { get; set; } = 2;
    public string Version { get; set; } = string.Empty;
    public string CurrentLine { get; set; } = string.Empty;
    public string TargetLine { get; set; } = string.Empty;
    public int CurrentCompatibilityEpoch { get; set; }
    public int TargetCompatibilityEpoch { get; set; }
    public string PackageUrl { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long? PackageSize { get; set; }
    public string PackageFormat { get; set; } = "auto";
    public string TargetDirectory { get; set; } = string.Empty;
    public string MainExecutablePath { get; set; } = string.Empty;
    public int ParentProcessId { get; set; }
    public bool AllowInsecureHttp { get; set; }
    public List<string> Delete { get; set; } = [];
}

public enum UpdateStage { Preparing, Downloading, Verifying, Adapting, Installing, Starting, Completed }
public sealed record UpdateProgress(string Message, double? Percent = null, string? Version = null,
    UpdateStage Stage = UpdateStage.Preparing);
public sealed record UpdateResult(bool Success, string Message);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
[JsonSerializable(typeof(UpdatePlan))]
[JsonSerializable(typeof(KernelState))]
[JsonSerializable(typeof(BundleManifest))]
[JsonSerializable(typeof(List<TransactionFile>))]
internal partial class UpdaterJsonContext : JsonSerializerContext;

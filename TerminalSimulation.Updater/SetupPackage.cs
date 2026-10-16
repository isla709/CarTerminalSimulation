using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace TerminalSimulation.Updater;

internal sealed class SetupManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string Product { get; set; } = "TerminalSimulation";
    public string Version { get; set; } = "";
    public string Line { get; set; } = "main";
    public int CompatibilityEpoch { get; set; } = 1;
    public string MainExecutable { get; set; } = "TerminalSimulation.Wpf.exe";
    public string Engine { get; set; } = "";
    public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal static class SetupPackage
{
    internal const string ManifestName = "setup-package.json";
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    internal static bool IsExecutable(string path)
    {
        using var file = File.OpenRead(path);
        return file.ReadByte() == 'M' && file.ReadByte() == 'Z';
    }

    internal static SetupManifest Read(string path)
    {
        if (!IsExecutable(path)) throw new InvalidDataException("不是 CTS 安装程序。");
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry(ManifestName) ?? throw new InvalidDataException("安装包协议清单缺失。");
        if (entry.Length > 4 * 1024 * 1024) throw new InvalidDataException("安装包清单过大。");
        using var stream = entry.Open();
        var manifest = JsonSerializer.Deserialize<SetupManifest>(stream, Json) ?? throw new InvalidDataException("安装包清单损坏。");
        if (manifest.SchemaVersion != 1 || manifest.Product != "TerminalSimulation" || string.IsNullOrWhiteSpace(manifest.Version) ||
            string.IsNullOrWhiteSpace(manifest.Line) || manifest.CompatibilityEpoch < 1 || manifest.MainExecutable != "TerminalSimulation.Wpf.exe" ||
            !manifest.Engine.StartsWith("engines/", StringComparison.Ordinal) || manifest.Files.Count < 5)
            throw new InvalidDataException("安装包产品或协议不受支持。");
        foreach (var (relative, hash) in manifest.Files)
        {
            _ = UpdateKernel.SafePath(Path.GetTempPath(), relative);
            if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("安装包文件摘要无效。");
        }
        return manifest;
    }

    internal static void ValidateDirectory(string directory, SetupManifest manifest)
    {
        var actual = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(directory, p).Replace('\\', '/')).Where(p => p != ManifestName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!actual.SetEquals(manifest.Files.Keys)) throw new InvalidDataException("安装载荷文件集合与清单不一致。");
        foreach (var (relative, hash) in manifest.Files)
        {
            using var file = File.OpenRead(UpdateKernel.SafePath(directory, relative));
            if (!Convert.ToHexString(SHA256.HashData(file)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"安装载荷校验失败：{relative}");
        }
        ValidateRuntimeBridge(directory);
        if (UpdateKernel.ValidateBundle(directory) is null) throw new InvalidDataException("安装包缺少独立更新内核。");
    }

    private static void ValidateRuntimeBridge(string directory)
    {
        var runtimeMarker = Path.Combine(directory, ".Updater", "app-runtime.txt");
        var hostMarker = Path.Combine(directory, ".Updater", "app-hostfxr.txt");
        if (!File.Exists(runtimeMarker) && !File.Exists(hostMarker)) return;
        if (!File.Exists(runtimeMarker) || !File.Exists(hostMarker))
            throw new InvalidDataException("安装包的旧版运行时兼容标记不完整。");

        var runtime = File.ReadAllText(runtimeMarker).Trim();
        var hostfxr = File.ReadAllText(hostMarker).Trim();
        if (string.IsNullOrWhiteSpace(runtime) || string.IsNullOrWhiteSpace(hostfxr))
            throw new InvalidDataException("安装包的旧版运行时兼容标记为空。");
        var runtimePath = UpdateKernel.SafePath(directory, runtime.Replace('/', Path.DirectorySeparatorChar));
        var hostfxrPath = UpdateKernel.SafePath(directory, hostfxr.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(runtimePath) || !File.Exists(hostfxrPath) ||
            !Path.GetFullPath(hostfxrPath).StartsWith(Path.GetFullPath(runtimePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("安装包的旧版运行时兼容标记没有指向载荷内的私有运行时。");
    }

    internal static void Extract(string package, string directory, string? expectedVersion = null)
    {
        var manifest = Read(package);
        if (expectedVersion is not null && manifest.Version != expectedVersion) throw new InvalidDataException("安装包版本与更新计划不一致。");
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("安装载荷暂存目录必须为空。");
        Directory.CreateDirectory(directory);
        UpdateEngine.ExtractZipSafely(package, directory);
        ValidateDirectory(directory, manifest);
    }

    internal static void Create(string input, string stub, string output, string version, string line, int epoch)
    {
        input = Path.GetFullPath(input); output = Path.GetFullPath(output);
        if (output.StartsWith(input.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("安装包输出不能在载荷目录内。");
        if (!IsExecutable(stub)) throw new InvalidDataException("安装引导程序不是有效 EXE。");
        var bundle = UpdateKernel.ValidateBundle(input) ?? throw new InvalidDataException("载荷缺少更新内核。");
        var manifest = new SetupManifest { Version = version, Line = line, CompatibilityEpoch = epoch, Engine = bundle.Engine };
        var files = Directory.GetFiles(input, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(input, file).Replace('\\', '/');
            if (relative == ManifestName || relative.StartsWith(".Installation/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith(".Updater/transactions/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("Workspaces/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("Plugins/", StringComparison.OrdinalIgnoreCase) ||
                new[] { "config.json", "utility_settings.json", "update-sources.json", "KEY.txt", ".Updater/state.json", ".Updater/update.lock" }.Contains(relative, StringComparer.OrdinalIgnoreCase) ||
                relative.Contains("XunjieCloud", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"不得打包用户数据或内部组件：{relative}");
            using var stream = File.OpenRead(file);
            manifest.Files.Add(relative, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
        }
        ValidateRuntimeBridge(input);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using (var destination = new FileStream(output, FileMode.CreateNew, FileAccess.ReadWrite))
        {
            using (var bootstrap = File.OpenRead(stub)) bootstrap.CopyTo(destination);
            // Absolute offsets include the PE prefix. This is a real ZIP as well as a real EXE.
            using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(input, file).Replace('\\', '/');
                var bootstrapInflater = relative.StartsWith(".Updater/runtimes/net8-x64/shared/Microsoft.NETCore.App/", StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(relative).Equals("System.IO.Compression.Native.dll", StringComparison.OrdinalIgnoreCase);
                // Keep the private runtime's native inflater available before the managed
                // engine starts; all other files use ordinary ZIP Deflate entries.
                // All other entries remain standard ZIP and keep the existing update format.
                var level = bootstrapInflater ? CompressionLevel.NoCompression : CompressionLevel.SmallestSize;
                var entry = archive.CreateEntry(relative, level);
                using var target = entry.Open(); using var source = File.OpenRead(file); source.CopyTo(target);
            }
            using var metadata = archive.CreateEntry(ManifestName, CompressionLevel.NoCompression).Open();
            JsonSerializer.Serialize(metadata, manifest, Json);
        }
        _ = Read(output);
    }
}

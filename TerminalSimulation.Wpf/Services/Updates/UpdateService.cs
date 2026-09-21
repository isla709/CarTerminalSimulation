using System.Net.Http.Headers;
using System.Text.Json;
using System.IO;
using System.Net.Http;

namespace TerminalSimulation.Wpf.Services.Updates;

internal sealed class UpdateService
{
    private const int MaximumManifestBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly HttpClient _httpClient;
    private readonly IAppLogger _logger;
    private readonly string _configurationPath;

    public UpdateService(IAppLogger logger, HttpClient? httpClient = null, string? configurationPath = null)
    {
        _logger = logger;
        _configurationPath = configurationPath ?? Path.Combine(AppContext.BaseDirectory, "update-sources.json");
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CarTerminalSimulation", "1.0"));
    }

    public async Task<UpdateSourceConfiguration> LoadConfigurationAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_configurationPath))
        {
            var defaults = CreateDefaultConfiguration();
            Directory.CreateDirectory(Path.GetDirectoryName(_configurationPath)!);
            await File.WriteAllTextAsync(_configurationPath, JsonSerializer.Serialize(defaults, JsonOptions), cancellationToken);
            return defaults;
        }

        await using var stream = File.OpenRead(_configurationPath);
        var configuration = await JsonSerializer.DeserializeAsync<UpdateSourceConfiguration>(stream, JsonOptions, cancellationToken)
                            ?? throw new InvalidDataException("更新源配置为空。");
        if (configuration.SchemaVersion != 1)
            throw new InvalidDataException($"不支持的更新源配置版本：{configuration.SchemaVersion}。");
        configuration.CheckIntervalHours = Math.Clamp(configuration.CheckIntervalHours, 1, 168);
        return configuration;
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(string currentVersion, CancellationToken cancellationToken)
    {
        var configuration = await LoadConfigurationAsync(cancellationToken);
        var sources = configuration.Sources
            .Where(source => source.Enabled)
            .OrderByDescending(source => source.Priority)
            .ToList();
        var diagnostics = new List<string>();
        var candidates = new List<UpdateCandidate>();

        foreach (var source in sources)
        {
            try
            {
                var candidate = source.Type.ToLowerInvariant() switch
                {
                    "github" => await CheckGitHubAsync(source, configuration.Channel, cancellationToken),
                    "manifest" => await CheckManifestAsync(source, configuration.Channel, cancellationToken),
                    _ => throw new InvalidDataException($"未知更新源类型：{source.Type}")
                };
                if (candidate is not null) candidates.Add(candidate);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var sourceName = DisplayName(source);
                diagnostics.Add($"{sourceName}: {ex.Message}");
                _logger.Error("自动更新", $"检查更新源失败：{sourceName}", ex);
            }
        }

        var newest = candidates
            .Where(candidate => UpdateVersionComparer.Compare(candidate.Version, currentVersion) > 0)
            .OrderByDescending(candidate => candidate.Version, Comparer<string>.Create(UpdateVersionComparer.Compare))
            .ThenByDescending(candidate => candidate.PublishedAt)
            .FirstOrDefault();
        return new UpdateCheckResult(newest, sources.Count > 0, diagnostics);
    }

    private async Task<UpdateCandidate?> CheckGitHubAsync(
        UpdateSourceDefinition source,
        string channel,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source.Owner) || string.IsNullOrWhiteSpace(source.Repository))
            throw new InvalidDataException("GitHub 更新源缺少 owner 或 repository。");

        var apiBase = string.IsNullOrWhiteSpace(source.ApiBaseUrl) ? "https://api.github.com" : source.ApiBaseUrl.TrimEnd('/');
        var releasesUri = new Uri($"{apiBase}/repos/{Uri.EscapeDataString(source.Owner)}/{Uri.EscapeDataString(source.Repository)}/releases?per_page=20");
        using var request = new HttpRequestMessage(HttpMethod.Get, releasesUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var releases = await ReadJsonAsync<List<GitHubRelease>>(response, cancellationToken) ?? [];
        var manifestName = string.IsNullOrWhiteSpace(source.ManifestAssetName)
            ? "update-manifest.json"
            : source.ManifestAssetName;

        foreach (var release in releases.Where(item => !item.Draft))
        {
            if (string.Equals(channel, "stable", StringComparison.OrdinalIgnoreCase) && release.Prerelease) continue;
            var manifestAsset = release.Assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, manifestName, StringComparison.OrdinalIgnoreCase));
            if (manifestAsset is null) continue;

            var manifestUri = RequireRemoteUri(manifestAsset.BrowserDownloadUrl, allowInsecureHttp: false);
            var manifest = await DownloadManifestAsync(manifestUri, cancellationToken);
            if (!ChannelMatches(channel, manifest.Channel)) continue;
            var packageAsset = release.Assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, manifest.Package.FileName, StringComparison.OrdinalIgnoreCase));
            if (packageAsset is null)
                throw new InvalidDataException($"Release 缺少清单指定的资产 {manifest.Package.FileName}。");

            var sha256 = manifest.Package.Sha256;
            if (string.IsNullOrWhiteSpace(sha256) && packageAsset.Digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true)
                sha256 = packageAsset.Digest[7..];
            manifest.Package.Sha256 = sha256;
            manifest.Package.Url = packageAsset.BrowserDownloadUrl;
            manifest.Package.Size ??= packageAsset.Size;
            manifest.PublishedAt ??= release.PublishedAt;
            manifest.ReleaseNotes ??= release.Body;
            return CreateCandidate(manifest, DisplayName(source), allowInsecureHttp: false, manifestUri);
        }

        return null;
    }

    private async Task<UpdateCandidate?> CheckManifestAsync(
        UpdateSourceDefinition source,
        string channel,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source.ManifestUrl))
            throw new InvalidDataException("托管平台更新源缺少 manifestUrl。");
        var manifestUri = RequireRemoteUri(source.ManifestUrl, source.AllowInsecureHttp);
        var manifest = await DownloadManifestAsync(manifestUri, cancellationToken);
        if (!ChannelMatches(channel, manifest.Channel)) return null;
        return CreateCandidate(manifest, DisplayName(source), source.AllowInsecureHttp, manifestUri);
    }

    private async Task<UpdateManifest> DownloadManifestAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync<UpdateManifest>(response, cancellationToken)
               ?? throw new InvalidDataException("更新清单为空。");
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > MaximumManifestBytes)
            throw new InvalidDataException("更新服务返回的 JSON 内容过大。");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var limited = new LimitedReadStream(source, MaximumManifestBytes);
        return await JsonSerializer.DeserializeAsync<T>(limited, JsonOptions, cancellationToken);
    }

    private static UpdateCandidate CreateCandidate(
        UpdateManifest manifest,
        string sourceName,
        bool allowInsecureHttp,
        Uri manifestUri)
    {
        if (manifest.SchemaVersion != 1) throw new InvalidDataException($"不支持的更新清单版本：{manifest.SchemaVersion}。");
        if (!string.Equals(manifest.Product, "TerminalSimulation", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"更新产品标识不匹配：{manifest.Product}。");
        if (string.IsNullOrWhiteSpace(manifest.Version)) throw new InvalidDataException("更新清单缺少版本号。");
        if (!string.IsNullOrWhiteSpace(manifest.MinimumUpdaterVersion) &&
            Version.TryParse(manifest.MinimumUpdaterVersion, out var minimumUpdater) &&
            minimumUpdater > new Version(1, 0, 0))
            throw new InvalidDataException($"此更新需要 update.exe {minimumUpdater} 或更高版本，请先安装完整升级包。");
        if (string.IsNullOrWhiteSpace(manifest.Package.FileName)) throw new InvalidDataException("更新清单缺少包文件名。");
        if (manifest.Package.Sha256.Length != 64 || !manifest.Package.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("更新清单缺少有效的 SHA-256 摘要。");

        Uri packageUri;
        if (Uri.TryCreate(manifest.Package.Url, UriKind.Absolute, out var absolute)) packageUri = absolute;
        else if (!string.IsNullOrWhiteSpace(manifest.Package.Url)) packageUri = new Uri(manifestUri, manifest.Package.Url);
        else throw new InvalidDataException("更新清单缺少下载地址。");
        RequireRemoteUri(packageUri.AbsoluteUri, allowInsecureHttp);

        return new UpdateCandidate(
            manifest.Version,
            manifest.Channel,
            manifest.PublishedAt,
            manifest.ReleaseNotes ?? "此版本未提供更新说明。",
            sourceName,
            packageUri,
            manifest.Package.Sha256,
            manifest.Package.Size,
            allowInsecureHttp,
            manifest.Delete);
    }

    private static Uri RequireRemoteUri(string value, bool allowInsecureHttp)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) throw new InvalidDataException($"地址无效：{value}");
        if (uri.Scheme != Uri.UriSchemeHttps && !(allowInsecureHttp && uri.Scheme == Uri.UriSchemeHttp))
            throw new InvalidDataException("更新源必须使用 HTTPS；受信任内网 HTTP 源需显式设置 allowInsecureHttp。");
        return uri;
    }

    private static bool ChannelMatches(string configured, string manifest) =>
        string.IsNullOrWhiteSpace(configured) ||
        string.Equals(configured, manifest, StringComparison.OrdinalIgnoreCase);

    private static string DisplayName(UpdateSourceDefinition source) =>
        string.IsNullOrWhiteSpace(source.Name) ? source.Type : source.Name;

    private static UpdateSourceConfiguration CreateDefaultConfiguration() => new()
    {
        AutoCheck = true,
        CheckIntervalHours = 12,
        Channel = "preview",
        Sources =
        [
            new UpdateSourceDefinition
            {
                Type = "github",
                Name = "GitHub Releases",
                Owner = "isla709",
                Repository = "CarTerminalSimulation",
                ManifestAssetName = "update-manifest.json",
                Priority = 100,
                Enabled = true
            }
        ]
    };

    private sealed class LimitedReadStream(Stream inner, long maximumBytes) : Stream
    {
        private long _totalRead;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _totalRead; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => Track(inner.Read(buffer, offset, count));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Track(await inner.ReadAsync(buffer, cancellationToken));
        private int Track(int read)
        {
            _totalRead += read;
            if (_totalRead > maximumBytes) throw new InvalidDataException("更新服务返回的 JSON 内容过大。");
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}

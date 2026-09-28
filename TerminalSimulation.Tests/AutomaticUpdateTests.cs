using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using TerminalSimulation.Updater;
using TerminalSimulation.Wpf.Services;
using TerminalSimulation.Wpf.Services.Updates;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class AutomaticUpdateTests
{
    [Theory]
    [InlineData("preview", "preview", false, true)]
    [InlineData("preview", "stable", false, false)]
    [InlineData("preview", "beta", false, false)]
    [InlineData("stable", "stable", false, true)]
    [InlineData("stable", "preview", false, false)]
    [InlineData("stable", "preview", true, true)]
    [InlineData("stable", "beta", true, true)]
    [InlineData("stable", "test", true, true)]
    public void UpdateChannels_RespectStablePrereleaseOptIn(
        string currentChannel,
        string targetChannel,
        bool includePrerelease,
        bool expected)
    {
        Assert.Equal(expected,
            UpdateChannelPolicy.IsAllowed(currentChannel, targetChannel, includePrerelease));
    }

    [Theory]
    [InlineData("{\"Version\":\"preview7-build.20260921.1\",\"SchemaVersion\":2}")]
    [InlineData("{\"version\":\"preview7-build.20260921.1\",\"schemaVersion\":2}")]
    public void UpdaterPlan_ReadsLegacyAndWebJsonPropertyCasing(string json)
    {
        var plan = JsonSerializer.Deserialize(json, UpdaterJsonContext.Default.UpdatePlan);

        Assert.NotNull(plan);
        Assert.Equal(2, plan!.SchemaVersion);
        Assert.Equal("preview7-build.20260921.1", plan.Version);
    }

    [Theory]
    [InlineData("preview6", "preview5-build.20260921.abcd", 1)]
    [InlineData("preview5-build.20260922.1", "preview5-build.20260921.abcd", 1)]
    [InlineData("preview5-build.20260921.2", "preview5-build.20260921.1", 1)]
    [InlineData("preview5-build.20260921.abcd", "preview5-build.20260921.ef01", 0)]
    public void PreviewVersions_AreComparedWithoutRandomBuildLoops(string left, string right, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(UpdateVersionComparer.Compare(left, right)));
    }

    [Fact]
    public async Task HostedManifestSource_ReturnsValidatedCandidate()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var configPath = Path.Combine(root, "update-sources.json");
            await File.WriteAllTextAsync(configPath, """
                {
                  "schemaVersion": 1,
                  "autoCheck": true,
                  "channel": "preview",
                  "sources": [
                    {
                      "type": "manifest",
                      "name": "test mirror",
                      "manifestUrl": "https://updates.example/update-manifest.json",
                      "enabled": true
                    }
                  ]
                }
                """);
            using var client = new HttpClient(new StubHandler(request =>
            {
                Assert.Equal("https://updates.example/update-manifest.json", request.RequestUri!.AbsoluteUri);
                return JsonResponse("""
                    {
                      "schemaVersion": 2,
                      "product": "TerminalSimulation",
                      "version": "preview6-build.20260921.1",
                      "channel": "preview",
                      "line": "main",
                      "compatibilityEpoch": 1,
                      "releaseNotes": "test",
                      "package": {
                        "fileName": "app.zip",
                        "url": "app.zip",
                        "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        "size": 42
                      }
                    }
                    """);
            }));
            var service = new UpdateService(new TestLogger(), client, configPath);

            var result = await service.CheckForUpdatesAsync(
                "preview5-build.20260920.1", "main", 1, CancellationToken.None);

            Assert.NotNull(result.Candidate);
            var source = Assert.Single(result.Candidate!.Sources);
            Assert.Equal("https://updates.example/app.zip", source.PackageUri.AbsoluteUri);
            Assert.Equal("test mirror", source.Name);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReleasedBuild_IsListedWhenCurrentLocalBuildHasEquivalentRandomSuffix()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var configPath = Path.Combine(root, "update-sources.json");
            await File.WriteAllTextAsync(configPath, """
                {
                  "schemaVersion": 1,
                  "channel": "preview",
                  "line": "main",
                  "sources": [{
                    "type": "manifest",
                    "name": "test mirror",
                    "manifestUrl": "https://updates.example/update-manifest.json"
                  }]
                }
                """);
            using var client = new HttpClient(new StubHandler(_ => JsonResponse("""
                {
                  "schemaVersion": 2,
                  "product": "TerminalSimulation",
                  "version": "preview6-build.20260921.1",
                  "channel": "preview",
                  "line": "main",
                  "compatibilityEpoch": 1,
                  "package": {
                    "fileName": "app.zip",
                    "url": "app.zip",
                    "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    "size": 42
                  }
                }
                """)));
            var service = new UpdateService(new TestLogger(), client, configPath);

            var result = await service.CheckForUpdatesAsync(
                "preview6-build.20260921.220d", "main", 1, CancellationToken.None);

            Assert.Null(result.Candidate);
            var candidate = Assert.Single(result.Candidates);
            Assert.Equal("preview6-build.20260921.1", candidate.Version);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExactInstalledRelease_IsNotListedAgain()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var configPath = Path.Combine(root, "update-sources.json");
            await File.WriteAllTextAsync(configPath, """
                {
                  "schemaVersion": 1,
                  "channel": "preview",
                  "line": "main",
                  "sources": [{
                    "type": "manifest",
                    "manifestUrl": "https://updates.example/update-manifest.json"
                  }]
                }
                """);
            using var client = new HttpClient(new StubHandler(_ => JsonResponse("""
                {
                  "schemaVersion": 2,
                  "product": "TerminalSimulation",
                  "version": "preview6-build.20260921.1",
                  "channel": "preview",
                  "line": "main",
                  "compatibilityEpoch": 1,
                  "package": {
                    "fileName": "app.zip",
                    "url": "app.zip",
                    "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                  }
                }
                """)));
            var service = new UpdateService(new TestLogger(), client, configPath);

            var result = await service.CheckForUpdatesAsync(
                "PREVIEW6-BUILD.20260921.1", "main", 1, CancellationToken.None);

            Assert.Empty(result.Candidates);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GitHubSource_ResolvesPackageFromSameReleaseAsset()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var configPath = Path.Combine(root, "update-sources.json");
            await File.WriteAllTextAsync(configPath, """
                {
                  "schemaVersion": 1,
                  "channel": "preview",
                  "sources": [
                    {
                      "type": "github",
                      "name": "GitHub",
                      "owner": "owner",
                      "repository": "repo",
                      "apiBaseUrl": "https://api.example",
                      "manifestAssetName": "update-manifest.json"
                    }
                  ]
                }
                """);
            var manifestAttempts = 0;
            using var client = new HttpClient(new StubHandler(request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/releases"))
                {
                    Assert.True(request.Headers.Contains("X-GitHub-Api-Version"));
                    return JsonResponse("""
                        [{
                          "draft": false,
                          "prerelease": true,
                          "body": "release notes",
                          "published_at": "2026-09-21T00:00:00Z",
                          "assets": [
                            { "name": "update-manifest.json", "browser_download_url": "https://download.example/manifest", "size": 500 },
                            { "name": "app.zip", "browser_download_url": "https://download.example/app.zip", "size": 1000, "digest": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" }
                          ]
                        }]
                        """);
                }

                if (Interlocked.Increment(ref manifestAttempts) == 1)
                    throw new HttpRequestException("manifest cold connection failed");
                return JsonResponse("""
                    {
                      "schemaVersion": 2,
                      "product": "TerminalSimulation",
                      "version": "preview6-build.20260921.1",
                      "channel": "preview",
                      "line": "main",
                      "compatibilityEpoch": 1,
                      "package": { "fileName": "app.zip", "sha256": "", "size": 1000 }
                    }
                    """);
            }));
            var service = new UpdateService(new TestLogger(), client, configPath);

            var result = await service.CheckForUpdatesAsync("preview5", "main", 1, CancellationToken.None);

            var source = Assert.Single(result.Candidate!.Sources);
            Assert.Equal("https://download.example/app.zip", source.PackageUri.AbsoluteUri);
            Assert.Equal(new string('b', 64), source.Sha256);
            Assert.Equal(2, manifestAttempts);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GitHubReleaseCatalog_ListsTwoBuildsFromOneRelease()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var configPath = Path.Combine(root, "update-sources.json");
            await File.WriteAllTextAsync(configPath, """
                {
                  "schemaVersion": 3,
                  "channel": "preview",
                  "sources": [{
                    "type": "github",
                    "name": "GitHub",
                    "owner": "owner",
                    "repository": "repo",
                    "apiBaseUrl": "https://api.example"
                  }]
                }
                """);
            using var client = new HttpClient(new StubHandler(request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/releases"))
                {
                    return JsonResponse("""
                        [{
                          "draft": false,
                          "prerelease": true,
                          "assets": [
                            { "name": "update-manifest.json", "browser_download_url": "https://download.example/catalog.json", "size": 1000 },
                            { "name": "preview8.1.zip", "browser_download_url": "https://download.example/preview8.1.zip", "size": 100 },
                            { "name": "preview8.2.zip", "browser_download_url": "https://download.example/preview8.2.zip", "size": 200 }
                          ]
                        }]
                        """);
                }

                return JsonResponse("""
                    {
                      "schemaVersion": 2,
                      "product": "TerminalSimulation",
                      "line": "main",
                      "versions": [
                        {
                          "schemaVersion": 2,
                          "version": "preview8-build.20260924.1",
                          "channel": "preview",
                          "compatibilityEpoch": 1,
                          "package": {
                            "fileName": "preview8.1.zip",
                            "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                            "size": 100
                          }
                        },
                        {
                          "schemaVersion": 2,
                          "version": "preview8-build.20260924.2",
                          "channel": "preview",
                          "compatibilityEpoch": 1,
                          "package": {
                            "fileName": "preview8.2.zip",
                            "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                            "size": 200
                          }
                        }
                      ]
                    }
                    """);
            }));
            var service = new UpdateService(new TestLogger(), client, configPath);

            var result = await service.CheckForUpdatesAsync("preview7", "main", 1, CancellationToken.None);

            Assert.Equal(2, result.Candidates.Count);
            Assert.Contains(result.Candidates, candidate => candidate.Version.EndsWith(".1", StringComparison.Ordinal));
            Assert.Contains(result.Candidates, candidate => candidate.Version.EndsWith(".2", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task GitCodeSource_ResolvesReleaseAttachments()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var configPath = Path.Combine(root, "update-sources.json");
            await File.WriteAllTextAsync(configPath, """
                {
                  "schemaVersion": 1,
                  "channel": "preview",
                  "sources": [{
                    "type": "gitcode",
                    "name": "GitCode",
                    "owner": "owner",
                    "repository": "repo",
                    "apiBaseUrl": "https://api.gitcode.example/api/v5",
                    "manifestAssetName": "update-manifest.json"
                  }]
                }
                """);
            using var client = new HttpClient(new StubHandler(request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/releases"))
                {
                    return JsonResponse("""
                        [{
                          "tag_name": "Preview8",
                          "prerelease": true,
                          "body": "GitCode notes",
                          "created_at": "2026-09-24T10:00:00+08:00",
                          "release_status": "pre",
                          "assets": [
                            { "name": "update-manifest.json", "browser_download_url": "https://gitcode.example/manifest" },
                            { "name": "app.zip", "browser_download_url": "https://gitcode.example/app.zip" }
                          ]
                        }]
                        """);
                }

                return JsonResponse("""
                    {
                      "schemaVersion": 2,
                      "product": "TerminalSimulation",
                      "version": "preview8-build.20260924.1",
                      "channel": "preview",
                      "line": "main",
                      "compatibilityEpoch": 1,
                      "package": {
                        "fileName": "app.zip",
                        "sha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                        "size": 2048
                      }
                    }
                    """);
            }));
            var service = new UpdateService(new TestLogger(), client, configPath);

            var result = await service.CheckForUpdatesAsync("preview7", "main", 1, CancellationToken.None);

            var source = Assert.Single(result.Candidate!.Sources);
            Assert.Equal("GitCode", source.Name);
            Assert.Equal("https://gitcode.example/app.zip", source.PackageUri.AbsoluteUri);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SameVersionAcrossSources_IsAggregatedIntoOneCandidate()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var configPath = Path.Combine(root, "update-sources.json");
            await File.WriteAllTextAsync(configPath, """
                {
                  "schemaVersion": 1,
                  "channel": "preview",
                  "sources": [
                    { "type": "manifest", "name": "GitCode", "priority": 200, "manifestUrl": "https://gitcode.example/update.json" },
                    { "type": "manifest", "name": "GitHub", "priority": 100, "manifestUrl": "https://github.example/update.json" }
                  ]
                }
                """);
            using var client = new HttpClient(new StubHandler(request =>
            {
                var host = request.RequestUri!.Host;
                return JsonResponse($$"""
                    {
                      "schemaVersion": 2,
                      "product": "TerminalSimulation",
                      "version": "preview8-build.20260924.1",
                      "channel": "preview",
                      "line": "main",
                      "compatibilityEpoch": 1,
                      "package": {
                        "fileName": "app.zip",
                        "url": "https://{{host}}/app.zip",
                        "sha256": "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
                        "size": 4096
                      }
                    }
                    """);
            }));
            var service = new UpdateService(new TestLogger(), client, configPath);

            var result = await service.CheckForUpdatesAsync("preview7", "main", 1, CancellationToken.None);

            var candidate = Assert.Single(result.Candidates);
            Assert.Equal(2, candidate.Sources.Count);
            Assert.Equal(["GitCode", "GitHub"], candidate.Sources.Select(source => source.Name).ToArray());
            Assert.Equal("GitCode、GitHub", candidate.SourceSummary);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TransientFirstConnectionFailure_IsRetriedBeforeReturningVersions()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var configPath = Path.Combine(root, "update-sources.json");
            await File.WriteAllTextAsync(configPath, """
                {
                  "schemaVersion": 3,
                  "channel": "preview",
                  "sources": [
                    { "type": "manifest", "name": "GitCode", "priority": 200, "manifestUrl": "https://gitcode.example/update.json" },
                    { "type": "manifest", "name": "GitHub", "priority": 100, "manifestUrl": "https://github.example/update.json" }
                  ]
                }
                """);
            var githubAttempts = 0;
            using var client = new HttpClient(new StubHandler(request =>
            {
                if (request.RequestUri!.Host == "github.example" && Interlocked.Increment(ref githubAttempts) == 1)
                    throw new HttpRequestException("cold connection failed");
                var host = request.RequestUri!.Host;
                return JsonResponse($$"""
                    {
                      "schemaVersion": 2,
                      "product": "TerminalSimulation",
                      "version": "preview8-build.20260924.1",
                      "channel": "preview",
                      "line": "main",
                      "compatibilityEpoch": 1,
                      "package": {
                        "fileName": "app.zip",
                        "url": "https://{{host}}/app.zip",
                        "sha256": "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
                        "size": 4096
                      }
                    }
                    """);
            }));
            var service = new UpdateService(new TestLogger(), client, configPath);

            var result = await service.CheckForUpdatesAsync("preview7", "main", 1, CancellationToken.None);

            Assert.Equal(2, Assert.Single(result.Candidates).Sources.Count);
            Assert.Equal(2, githubAttempts);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SourceProbeRanking_PrefersAvailableAndFasterSource()
    {
        var slow = new UpdateDownloadSource("slow", 100, new Uri("https://slow.example/app.zip"), new string('a', 64), 1, false);
        var fast = new UpdateDownloadSource("fast", 50, new Uri("https://fast.example/app.zip"), new string('a', 64), 1, false);
        var offline = new UpdateDownloadSource("offline", 999, new Uri("https://offline.example/app.zip"), new string('a', 64), 1, false);

        var ranked = UpdateSourceProbeService.Rank([
            new UpdateSourceProbeResult(slow, true, 30, 1024, 1000, "ok"),
            new UpdateSourceProbeResult(offline, false, 1, 0, null, "failed"),
            new UpdateSourceProbeResult(fast, true, 80, 1024, 5000, "ok")
        ]);

        Assert.Equal(["fast", "slow", "offline"], ranked.Select(result => result.Source.Name).ToArray());
    }

    [Fact]
    public async Task LegacyOfficialSourceConfiguration_IsMigratedWithGitCodeSource()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var configPath = Path.Combine(root, "update-sources.json");
            await File.WriteAllTextAsync(configPath, """
                {
                  "schemaVersion": 1,
                  "sources": [{
                    "type": "github",
                    "name": "GitHub Releases",
                    "owner": "isla709",
                    "repository": "CarTerminalSimulation"
                  }]
                }
                """);
            var service = new UpdateService(new TestLogger(), new HttpClient(new StubHandler(_ =>
                throw new InvalidOperationException("not used"))), configPath);

            var configuration = await service.LoadConfigurationAsync(CancellationToken.None);

            Assert.Equal(3, configuration.SchemaVersion);
            var gitCode = Assert.Single(configuration.Sources, source =>
                string.Equals(source.Type, "gitcode", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("Neruya", gitCode.Owner);
            var persisted = JsonSerializer.Deserialize<UpdateSourceConfigurationForTest>(
                await File.ReadAllTextAsync(configPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.Equal(3, persisted!.SchemaVersion);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PreviousDefaultGitCodeOwner_IsMigratedToPublishedRepository()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var configPath = Path.Combine(root, "update-sources.json");
            await File.WriteAllTextAsync(configPath, """
                {
                  "schemaVersion": 2,
                  "sources": [{
                    "type": "gitcode",
                    "name": "GitCode Releases",
                    "owner": "isla709",
                    "repository": "CarTerminalSimulation",
                    "apiBaseUrl": "https://api.gitcode.com/api/v5"
                  }]
                }
                """);
            var service = new UpdateService(new TestLogger(), new HttpClient(new StubHandler(_ =>
                throw new InvalidOperationException("not used"))), configPath);

            var configuration = await service.LoadConfigurationAsync(CancellationToken.None);

            Assert.Equal(3, configuration.SchemaVersion);
            var gitCode = Assert.Single(configuration.Sources);
            Assert.Equal("Neruya", gitCode.Owner);
            Assert.Contains("\"owner\": \"Neruya\"", await File.ReadAllTextAsync(configPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Updater_RejectsZipPathTraversal()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var zipPath = Path.Combine(root, "bad.zip");
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../outside.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("bad");
            }

            Assert.Throws<InvalidDataException>(() =>
                UpdateEngine.ExtractZipSafely(zipPath, Path.Combine(root, "stage")));
            Assert.False(File.Exists(Path.Combine(root, "outside.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HostedCatalog_ListsCompatibleVersionsAndFiltersOtherLines()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var configPath = Path.Combine(root, "update-sources.json");
            await File.WriteAllTextAsync(configPath, """
                {
                  "schemaVersion": 1,
                  "channel": "preview",
                  "line": "main",
                  "sources": [{
                    "type": "manifest",
                    "name": "catalog",
                    "manifestUrl": "https://updates.example/catalog.json"
                  }]
                }
                """);
            using var client = new HttpClient(new StubHandler(_ => JsonResponse("""
                {
                  "schemaVersion": 2,
                  "product": "TerminalSimulation",
                  "line": "main",
                  "versions": [
                    {
                      "schemaVersion": 2,
                      "version": "V0.1",
                      "channel": "preview",
                      "compatibilityEpoch": 1,
                      "package": {
                        "fileName": "v01.zip",
                        "url": "v01.zip",
                        "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                        "size": 10
                      }
                    },
                    {
                      "schemaVersion": 2,
                      "version": "V2.0",
                      "channel": "preview",
                      "compatibilityEpoch": 2,
                      "package": {
                        "fileName": "v2.zip",
                        "url": "v2.zip",
                        "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                        "size": 20
                      }
                    },
                    {
                      "schemaVersion": 2,
                      "version": "other-1",
                      "channel": "preview",
                      "line": "experimental",
                      "compatibilityEpoch": 1,
                      "package": {
                        "fileName": "other.zip",
                        "url": "other.zip",
                        "sha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                        "size": 30
                      }
                    }
                  ]
                }
                """)));
            var service = new UpdateService(new TestLogger(), client, configPath);

            var result = await service.CheckForUpdatesAsync("V1.1", "main", 1, CancellationToken.None);

            Assert.Equal(2, result.Candidates.Count);
            Assert.Contains(result.Candidates, candidate => candidate.Version == "V0.1");
            Assert.Contains(result.Candidates, candidate => candidate.Version == "V2.0");
            Assert.DoesNotContain(result.Candidates, candidate => candidate.Line == "experimental");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HostedCatalog_HidesVersionsBelowCurrentCompatibilityEpoch()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var configPath = Path.Combine(root, "update-sources.json");
            await File.WriteAllTextAsync(configPath, """
                {
                  "schemaVersion": 1,
                  "channel": "preview",
                  "line": "main",
                  "sources": [{
                    "type": "manifest",
                    "manifestUrl": "https://updates.example/version.json"
                  }]
                }
                """);
            using var client = new HttpClient(new StubHandler(_ => JsonResponse("""
                {
                  "schemaVersion": 2,
                  "product": "TerminalSimulation",
                  "version": "V1.0",
                  "channel": "preview",
                  "line": "main",
                  "compatibilityEpoch": 1,
                  "package": {
                    "fileName": "v1.zip",
                    "url": "v1.zip",
                    "sha256": "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd",
                    "size": 10
                  }
                }
                """)));
            var service = new UpdateService(new TestLogger(), client, configPath);

            var result = await service.CheckForUpdatesAsync("V2.0", "main", 2, CancellationToken.None);

            Assert.Empty(result.Candidates);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("main", "preview", 1, 1)]
    [InlineData("main", "main", 2, 1)]
    public void Updater_RejectsCrossLineOrIncompatibleRollback(
        string currentLine,
        string targetLine,
        int currentEpoch,
        int targetEpoch)
    {
        var plan = new UpdatePlan
        {
            CurrentLine = currentLine,
            TargetLine = targetLine,
            CurrentCompatibilityEpoch = currentEpoch,
            TargetCompatibilityEpoch = targetEpoch
        };

        Assert.Throws<InvalidDataException>(() => UpdateEngine.ValidateCompatibility(plan));
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "TerminalSimulation.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    private sealed class TestLogger : IAppLogger
    {
        public void Info(string category, string message, long? connectionId = null, byte? channel = null, ushort? messageId = null) { }
        public void Error(string category, string message, Exception exception, long? connectionId = null, byte? channel = null, ushort? messageId = null) { }
    }

    private sealed class UpdateSourceConfigurationForTest
    {
        public int SchemaVersion { get; set; }
    }
}

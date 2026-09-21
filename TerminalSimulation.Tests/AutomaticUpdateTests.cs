using System.IO.Compression;
using System.Net;
using System.Text;
using TerminalSimulation.Updater;
using TerminalSimulation.Wpf.Services;
using TerminalSimulation.Wpf.Services.Updates;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class AutomaticUpdateTests
{
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
                      "schemaVersion": 1,
                      "product": "TerminalSimulation",
                      "version": "preview6-build.20260921.1",
                      "channel": "preview",
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

            var result = await service.CheckForUpdatesAsync("preview5-build.20260920.1", CancellationToken.None);

            Assert.NotNull(result.Candidate);
            Assert.Equal("https://updates.example/app.zip", result.Candidate!.PackageUri.AbsoluteUri);
            Assert.Equal("test mirror", result.Candidate.SourceName);
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

                return JsonResponse("""
                    {
                      "schemaVersion": 1,
                      "product": "TerminalSimulation",
                      "version": "preview6-build.20260921.1",
                      "channel": "preview",
                      "package": { "fileName": "app.zip", "sha256": "", "size": 1000 }
                    }
                    """);
            }));
            var service = new UpdateService(new TestLogger(), client, configPath);

            var result = await service.CheckForUpdatesAsync("preview5", CancellationToken.None);

            Assert.Equal("https://download.example/app.zip", result.Candidate!.PackageUri.AbsoluteUri);
            Assert.Equal(new string('b', 64), result.Candidate.Sha256);
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
}

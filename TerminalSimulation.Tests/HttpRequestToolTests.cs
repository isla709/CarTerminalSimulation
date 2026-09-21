using TerminalSimulation.Wpf.ViewModels.Utilities;
using System.Net.Http.Headers;
using System.Text;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class HttpRequestToolTests
{
    [Fact]
    public async Task SendRequestCommand_ReceivesStatusHeadersAndJsonBody()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
            {
            }

            var payload = "{\"ok\":true}";
            var response = System.Text.Encoding.UTF8.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\nX-Test: yes\r\nContent-Length: {System.Text.Encoding.UTF8.GetByteCount(payload)}\r\nConnection: close\r\n\r\n{payload}");
            await stream.WriteAsync(response);
        });

        using var viewModel = new HttpRequestToolViewModel();
        viewModel.SelectedRequest!.Url = $"http://127.0.0.1:{port}/health";

        await viewModel.SendRequestCommand.ExecuteAsync(null);
        await serverTask;

        Assert.Equal("HTTP 200 OK", viewModel.ResponseStatus);
        Assert.Contains("X-Test: yes", viewModel.ResponseHeaders);
        Assert.Contains("\"ok\": true", viewModel.ResponseBody);
    }

    [Fact]
    public void BuildRequestUri_PreservesExistingQueryAndEncodesEnabledParameters()
    {
        var parameters = new[]
        {
            new HttpNameValueItem { Name = "search text", Value = "中文 value" },
            new HttpNameValueItem { Name = "ignored", Value = "x", IsEnabled = false }
        };

        var result = HttpRequestToolViewModel.BuildRequestUri(
            "https://example.com/api?existing=1#result",
            parameters);

        Assert.Equal("https", result.Scheme);
        Assert.Contains("existing=1", result.Query);
        Assert.Contains("search%20text=", result.Query);
        Assert.DoesNotContain("ignored", result.Query);
        Assert.Equal("#result", result.Fragment);
    }

    [Fact]
    public void RequestDocument_RoundTripsAllEditableFields()
    {
        var source = new HttpRequestItem
        {
            Name = "创建订单",
            Method = "POST",
            Url = "https://example.com/orders",
            Body = "{\"count\":2}",
            ContentType = "application/json"
        };
        source.QueryParameters.Add(new HttpNameValueItem { Name = "dryRun", Value = "true" });
        source.Headers.Add(new HttpNameValueItem { Name = "X-Trace", Value = "abc" });

        var restored = HttpRequestItem.FromDocument(source.ToDocument());

        Assert.Equal(source.Name, restored.Name);
        Assert.Equal(source.Method, restored.Method);
        Assert.Equal(source.Url, restored.Url);
        Assert.Equal(source.Body, restored.Body);
        Assert.Equal(source.ContentType, restored.ContentType);
        Assert.Single(restored.QueryParameters);
        Assert.Single(restored.Headers);
        Assert.Equal("X-Trace", restored.Headers[0].Name);
    }

    [Fact]
    public async Task SaveWorkspaceCommand_WritesFixedFileAndNextInstanceLoadsIt()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), $"http-workspace-{Guid.NewGuid():N}");
        var savePath = Path.Combine(testDirectory, "Workspaces", "Http", ".Save", "workspace.http-workspace.json");

        try
        {
            using (var source = new HttpRequestToolViewModel(savePath))
            {
                source.WorkspaceName = "自动恢复工作区";
                source.SelectedRequest!.Name = "持久请求";
                source.SelectedRequest.Method = "POST";
                source.SelectedRequest.Url = "https://example.com/persisted";
                source.SelectedRequest.Body = "{\"saved\":true}";

                await source.SaveWorkspaceCommand.ExecuteAsync(null);

                Assert.True(File.Exists(savePath));
                Assert.False(source.IsDirty);
                Assert.Equal(Path.GetFullPath(savePath), source.WorkspacePath);
            }

            using var restored = new HttpRequestToolViewModel(savePath);
            Assert.Equal("自动恢复工作区", restored.WorkspaceName);
            Assert.False(restored.IsDirty);
            Assert.Equal(Path.GetFullPath(savePath), restored.WorkspacePath);
            Assert.Equal("持久请求", restored.SelectedRequest!.Name);
            Assert.Equal("POST", restored.SelectedRequest.Method);
            Assert.Equal("https://example.com/persisted", restored.SelectedRequest.Url);
            Assert.Equal("{\"saved\":true}", restored.SelectedRequest.Body);
            Assert.Contains("已自动加载", restored.StatusMessage);
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("application/json", "{\"name\":\"测试\",\"items\":[1,2]}", "\n", "\"name\": \"测试\"")]
    [InlineData("application/xml", "<root><item id=\"1\">值</item></root>", "\n", "  <item")]
    public void StructuredBodyFormatter_FormatsJsonAndXml(
        string contentType,
        string source,
        string expectedLineBreak,
        string expectedFragment)
    {
        var success = StructuredBodyFormatter.TryTransform(
            source,
            contentType,
            indented: true,
            out var result,
            out var error);

        Assert.True(success, error);
        Assert.Contains(expectedLineBreak, result);
        Assert.Contains(expectedFragment, result);
    }

    [Fact]
    public void StructuredBodyFormatter_ReportsJsonLocationAndCanMinify()
    {
        Assert.False(StructuredBodyFormatter.TryValidate("{\"a\":}", "application/json", out var error));
        Assert.Contains("JSON 第", error);

        Assert.True(StructuredBodyFormatter.TryTransform(
            "{\n  \"a\": true\n}",
            "application/json",
            indented: false,
            out var compact,
            out error), error);
        Assert.Equal("{\"a\":true}", compact);
    }

    [Fact]
    public void FormatResponseBody_InfersJsonFormatsItAndDecodesUnicode()
    {
        var payload = Encoding.UTF8.GetBytes(
            "{\"code\":200,\"remark\":\"\\u5E73\\u53F0-\\u6587\\u672C\\u4FE1\\u606F\"}");

        var result = HttpRequestToolViewModel.FormatResponseBody(
            payload,
            new MediaTypeHeaderValue("text/plain") { CharSet = "utf-8" },
            truncated: false,
            out var effectiveContentType);

        Assert.Equal("application/json", effectiveContentType);
        Assert.Contains(Environment.NewLine, result);
        Assert.Contains("平台-文本信息", result);
        Assert.Contains("\"code\": 200", result);
    }
}

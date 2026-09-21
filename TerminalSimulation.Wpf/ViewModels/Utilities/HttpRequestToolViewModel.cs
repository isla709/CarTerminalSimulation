using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TerminalSimulation.Wpf.Helpers;
using TerminalSimulation.Wpf.Services;

namespace TerminalSimulation.Wpf.ViewModels.Utilities;

public partial class HttpRequestToolViewModel : ObservableObject, IDisposable
{
    private const string PersistentWorkspaceDirectoryName = ".Save";
    private const string PersistentWorkspaceFileName = "workspace.http-workspace.json";
    private const int MaximumWorkspaceBytes = 10 * 1024 * 1024;
    private const int MaximumResponseBytes = 10 * 1024 * 1024;
    private readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };
    private static readonly JsonSerializerOptions WorkspaceJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private CancellationTokenSource? _requestCancellation;
    private readonly string _persistentWorkspacePath;
    private bool _suppressDirty;
    private bool _disposed;

    public HttpRequestToolViewModel() : this(persistentWorkspacePath: null)
    {
    }

    internal HttpRequestToolViewModel(string? persistentWorkspacePath)
    {
        _persistentWorkspacePath = string.IsNullOrWhiteSpace(persistentWorkspacePath)
            ? GetDefaultPersistentWorkspacePath()
            : Path.GetFullPath(persistentWorkspacePath);

        if (!TryLoadPersistentWorkspace())
        {
            AddRequestInternal("新请求", markDirty: false);
        }
        IsDirty = false;
    }

    public IReadOnlyList<string> AvailableMethods { get; } =
        ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    public IReadOnlyList<string> AvailableContentTypes { get; } =
        ["application/json", "text/plain", "application/xml", "application/x-www-form-urlencoded"];

    public ObservableCollection<HttpRequestItem> Requests { get; } = new();

    [ObservableProperty] private HttpRequestItem? _selectedRequest;
    [ObservableProperty] private HttpNameValueItem? _selectedQueryParameter;
    [ObservableProperty] private HttpNameValueItem? _selectedHeader;
    [ObservableProperty] private string _workspaceName = "HTTP 工作空间";
    [ObservableProperty] private string? _workspacePath;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isSending;
    [ObservableProperty] private int _timeoutSeconds = 30;
    [ObservableProperty] private string _responseStatus = "尚未发送请求";
    [ObservableProperty] private string _responseSummary = string.Empty;
    [ObservableProperty] private string _responseContentType = "text/plain";
    [ObservableProperty] private string _responseHeaders = string.Empty;
    [ObservableProperty] private string _responseBody = string.Empty;
    [ObservableProperty] private string _statusMessage = "就绪";

    public string WorkspaceDisplayName => IsDirty ? $"{WorkspaceName}  • 未保存" : WorkspaceName;

    partial void OnWorkspaceNameChanged(string value) => MarkDirty();

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(WorkspaceDisplayName));

    partial void OnSelectedRequestChanged(HttpRequestItem? value)
    {
        SendRequestCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSendingChanged(bool value)
    {
        SendRequestCommand.NotifyCanExecuteChanged();
        CancelRequestCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void NewRequest()
    {
        AddRequestInternal($"请求 {Requests.Count + 1}", markDirty: true);
    }

    [RelayCommand]
    private void DuplicateRequest()
    {
        if (SelectedRequest is null)
        {
            return;
        }

        var duplicate = HttpRequestItem.FromDocument(SelectedRequest.ToDocument());
        duplicate.Id = Guid.NewGuid();
        duplicate.Name = $"{SelectedRequest.Name} - 副本";
        AttachRequest(duplicate);
        Requests.Add(duplicate);
        SelectedRequest = duplicate;
        MarkDirty();
    }

    [RelayCommand]
    private void DeleteRequest()
    {
        if (SelectedRequest is null)
        {
            return;
        }

        var index = Requests.IndexOf(SelectedRequest);
        var removed = SelectedRequest;
        DetachRequest(removed);
        Requests.Remove(removed);
        SelectedRequest = Requests.Count == 0 ? null : Requests[Math.Clamp(index, 0, Requests.Count - 1)];
        MarkDirty();
    }

    [RelayCommand]
    private void AddQueryParameter()
    {
        if (SelectedRequest is null)
        {
            return;
        }

        SelectedRequest.QueryParameters.Add(new HttpNameValueItem());
        SelectedQueryParameter = SelectedRequest.QueryParameters[^1];
    }

    [RelayCommand]
    private void RemoveQueryParameter()
    {
        if (SelectedRequest is null || SelectedQueryParameter is null)
        {
            return;
        }

        SelectedRequest.QueryParameters.Remove(SelectedQueryParameter);
        SelectedQueryParameter = null;
    }

    [RelayCommand]
    private void AddHeader()
    {
        if (SelectedRequest is null)
        {
            return;
        }

        SelectedRequest.Headers.Add(new HttpNameValueItem());
        SelectedHeader = SelectedRequest.Headers[^1];
    }

    [RelayCommand]
    private void RemoveHeader()
    {
        if (SelectedRequest is null || SelectedHeader is null)
        {
            return;
        }

        SelectedRequest.Headers.Remove(SelectedHeader);
        SelectedHeader = null;
    }

    private bool CanSendRequest() => !IsSending && SelectedRequest is not null;

    [RelayCommand(CanExecute = nameof(CanSendRequest))]
    private async Task SendRequestAsync()
    {
        if (SelectedRequest is null)
        {
            return;
        }

        var source = SelectedRequest;
        Uri requestUri;
        try
        {
            requestUri = BuildRequestUri(source.Url, source.QueryParameters);
        }
        catch (Exception ex) when (ex is UriFormatException or InvalidOperationException)
        {
            ResponseStatus = "请求地址无效";
            ResponseSummary = ex.Message;
            StatusMessage = "请检查 URL";
            return;
        }

        var method = string.IsNullOrWhiteSpace(source.Method) ? "GET" : source.Method.Trim().ToUpperInvariant();
        var headers = source.Headers
            .Where(item => item.IsEnabled && !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => (Name: item.Name.Trim(), item.Value))
            .ToArray();
        var body = source.Body;
        var contentType = source.ContentType;

        _requestCancellation?.Dispose();
        _requestCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 1, 300)));
        var cancellationToken = _requestCancellation.Token;
        IsSending = true;
        ResponseStatus = "正在发送…";
        ResponseSummary = requestUri.ToString();
        ResponseContentType = "text/plain";
        ResponseHeaders = string.Empty;
        ResponseBody = string.Empty;
        StatusMessage = "请求进行中";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), requestUri);
            if (ShouldCreateContent(method, body))
            {
                request.Content = CreateContent(body, contentType);
            }

            foreach (var header in headers)
            {
                if (request.Headers.TryAddWithoutValidation(header.Name, header.Value))
                {
                    continue;
                }

                request.Content ??= new ByteArrayContent([]);
                request.Content.Headers.TryAddWithoutValidation(header.Name, header.Value);
            }

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var (payload, truncated) = await ReadLimitedAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                MaximumResponseBytes,
                cancellationToken);
            stopwatch.Stop();

            ResponseStatus = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            ResponseSummary = $"{stopwatch.Elapsed.TotalMilliseconds:F0} ms  •  {FormatBytes(payload.Length)}" +
                              (truncated ? "  •  已截断" : string.Empty);
            ResponseHeaders = FormatResponseHeaders(response);
            ResponseBody = FormatResponseBody(
                payload,
                response.Content.Headers.ContentType,
                truncated,
                out var effectiveContentType);
            ResponseContentType = effectiveContentType;
            StatusMessage = response.IsSuccessStatusCode ? "请求成功" : "请求已完成，服务器返回错误状态";
        }
        catch (OperationCanceledException) when (_requestCancellation?.IsCancellationRequested == true)
        {
            stopwatch.Stop();
            ResponseStatus = "请求已取消或超时";
            ResponseSummary = $"{stopwatch.Elapsed.TotalMilliseconds:F0} ms";
            StatusMessage = "请求已停止";
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            ResponseStatus = "连接失败";
            ResponseSummary = ex.Message;
            StatusMessage = "HTTP 请求失败";
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            ResponseStatus = "请求失败";
            ResponseSummary = ex.Message;
            StatusMessage = "发生未预期错误";
        }
        finally
        {
            IsSending = false;
        }
    }

    private bool CanCancelRequest() => IsSending;

    [RelayCommand(CanExecute = nameof(CanCancelRequest))]
    private void CancelRequest()
    {
        _requestCancellation?.Cancel();
    }

    [RelayCommand]
    private void ClearResponse()
    {
        ResponseStatus = "尚未发送请求";
        ResponseSummary = string.Empty;
        ResponseContentType = "text/plain";
        ResponseHeaders = string.Empty;
        ResponseBody = string.Empty;
        StatusMessage = "已清空响应";
    }

    [RelayCommand]
    private void FormatRequestBody()
    {
        TransformRequestBody(indented: true);
    }

    [RelayCommand]
    private void MinifyRequestBody()
    {
        TransformRequestBody(indented: false);
    }

    private void TransformRequestBody(bool indented)
    {
        if (SelectedRequest is null)
        {
            return;
        }

        if (StructuredBodyFormatter.TryTransform(
                SelectedRequest.Body,
                SelectedRequest.ContentType,
                indented,
                out var transformed,
                out var error))
        {
            SelectedRequest.Body = transformed;
            StatusMessage = indented ? "请求正文已格式化" : "请求正文已压缩";
            return;
        }

        StatusMessage = error;
        MessageBox.Show(
            error,
            indented ? "无法格式化正文" : "无法压缩正文",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    [RelayCommand]
    private void CopyResponseBody()
    {
        if (string.IsNullOrEmpty(ResponseBody))
        {
            return;
        }

        try
        {
            Clipboard.SetText(ResponseBody);
            StatusMessage = "响应正文已复制";
        }
        catch (Exception ex)
        {
            StatusMessage = $"复制失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveWorkspaceAsync()
    {
        if (await SaveDocumentAsync(_persistentWorkspacePath, CaptureWorkspace()))
        {
            WorkspacePath = _persistentWorkspacePath;
            IsDirty = false;
            StatusMessage = @"工作空间已保存到 Workspaces\Http\.Save";
        }
    }

    [RelayCommand]
    private async Task ExportWorkspaceAsync()
    {
        var path = SelectWorkspaceSavePath("导出 HTTP 工作空间", WorkspaceName);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (await SaveDocumentAsync(path, CaptureWorkspace()))
        {
            StatusMessage = $"工作空间已导出: {Path.GetFileName(path)}";
        }
    }

    [RelayCommand]
    private async Task ImportWorkspaceAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入 HTTP 工作空间",
            Filter = "HTTP 工作空间 (*.http-workspace.json)|*.http-workspace.json|JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            InitialDirectory = EnsureWorkspaceDirectory(),
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var info = new FileInfo(dialog.FileName);
            if (info.Length > MaximumWorkspaceBytes)
            {
                throw new InvalidDataException("工作空间文件超过 10 MB，已拒绝导入。");
            }

            var json = await File.ReadAllTextAsync(dialog.FileName);
            var document = JsonSerializer.Deserialize<HttpWorkspaceDocument>(json, WorkspaceJsonOptions)
                           ?? throw new InvalidDataException("工作空间内容为空。");
            ValidateWorkspaceDocument(document);

            LoadWorkspace(document);
            WorkspacePath = null;
            IsDirty = true;
            StatusMessage = $"已导入 {Requests.Count} 个请求，请保存到本地工作空间";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            MessageBox.Show(
                $"无法导入工作空间。\n\n{ex.Message}",
                "导入失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            StatusMessage = "导入失败";
        }
    }

    internal static Uri BuildRequestUri(string rawUrl, IEnumerable<HttpNameValueItem> queryParameters)
    {
        if (!Uri.TryCreate(rawUrl?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new UriFormatException("请输入完整的 http:// 或 https:// 地址。");
        }

        var builder = new UriBuilder(uri);
        var segments = new List<string>();
        var existingQuery = builder.Query.TrimStart('?');
        if (!string.IsNullOrWhiteSpace(existingQuery))
        {
            segments.Add(existingQuery);
        }

        segments.AddRange(queryParameters
            .Where(item => item.IsEnabled && !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => $"{Uri.EscapeDataString(item.Name.Trim())}={Uri.EscapeDataString(item.Value ?? string.Empty)}"));
        builder.Query = string.Join("&", segments);
        return builder.Uri;
    }

    private static bool ShouldCreateContent(string method, string body) =>
        !string.IsNullOrEmpty(body) || method is "POST" or "PUT" or "PATCH";

    private static HttpContent CreateContent(string body, string contentType)
    {
        var content = new StringContent(body ?? string.Empty, Encoding.UTF8);
        if (MediaTypeHeaderValue.TryParse(contentType, out var parsedContentType))
        {
            parsedContentType.CharSet ??= "utf-8";
            content.Headers.ContentType = parsedContentType;
        }
        return content;
    }

    private static async Task<(byte[] Payload, bool Truncated)> ReadLimitedAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using (stream)
        await using (var buffer = new MemoryStream(Math.Min(maximumBytes, 128 * 1024)))
        {
            var chunk = new byte[64 * 1024];
            while (true)
            {
                var read = await stream.ReadAsync(chunk, cancellationToken);
                if (read == 0)
                {
                    return (buffer.ToArray(), false);
                }

                var remaining = maximumBytes - (int)buffer.Length;
                if (read > remaining)
                {
                    if (remaining > 0)
                    {
                        await buffer.WriteAsync(chunk.AsMemory(0, remaining), cancellationToken);
                    }
                    return (buffer.ToArray(), true);
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }
        }
    }

    private static string FormatResponseHeaders(HttpResponseMessage response)
    {
        return string.Join(
            Environment.NewLine,
            response.Headers
                .Concat(response.Content.Headers)
                .OrderBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
                .Select(header => $"{header.Key}: {string.Join(", ", header.Value)}"));
    }

    internal static string FormatResponseBody(
        byte[] payload,
        MediaTypeHeaderValue? contentType,
        bool truncated,
        out string effectiveContentType)
    {
        var mediaType = contentType?.MediaType ?? string.Empty;
        var inferredContentType = InferStructuredContentType(payload);
        effectiveContentType = !string.IsNullOrEmpty(inferredContentType)
            ? inferredContentType
            : string.IsNullOrEmpty(mediaType) ? "text/plain" : mediaType;
        var isText = mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
                     mediaType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
                     mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
                     mediaType.Contains("javascript", StringComparison.OrdinalIgnoreCase) ||
                     mediaType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) ||
                     !string.IsNullOrEmpty(inferredContentType);
        if (!isText && payload.Length > 0)
        {
            effectiveContentType = string.IsNullOrEmpty(mediaType) ? "application/octet-stream" : mediaType;
            return $"[二进制响应，已接收 {FormatBytes(payload.Length)}{(truncated ? "，内容已截断" : string.Empty)}]";
        }

        Encoding encoding;
        try
        {
            encoding = string.IsNullOrWhiteSpace(contentType?.CharSet)
                ? Encoding.UTF8
                : Encoding.GetEncoding(contentType.CharSet.Trim('"'));
        }
        catch (ArgumentException)
        {
            encoding = Encoding.UTF8;
        }

        var text = encoding.GetString(payload);
        if (StructuredBodyFormatter.TryTransform(
                text,
                effectiveContentType,
                indented: true,
                out var formatted,
                out _))
        {
            text = formatted;
        }

        return truncated ? text + Environment.NewLine + Environment.NewLine + "[响应超过 10 MB，后续内容已截断]" : text;
    }

    private static string InferStructuredContentType(byte[] payload)
    {
        var index = 0;
        if (payload.Length >= 3 && payload[0] == 0xEF && payload[1] == 0xBB && payload[2] == 0xBF)
        {
            index = 3;
        }

        while (index < payload.Length && payload[index] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            index++;
        }

        if (index >= payload.Length)
        {
            return string.Empty;
        }

        return payload[index] switch
        {
            (byte)'{' or (byte)'[' => "application/json",
            (byte)'<' => "application/xml",
            _ => string.Empty
        };
    }

    private HttpWorkspaceDocument CaptureWorkspace() => new()
    {
        FormatVersion = 1,
        Name = string.IsNullOrWhiteSpace(WorkspaceName) ? "HTTP 工作空间" : WorkspaceName.Trim(),
        UpdatedAt = DateTimeOffset.Now,
        Requests = Requests.Select(request => request.ToDocument()).ToList()
    };

    private void LoadWorkspace(HttpWorkspaceDocument document)
    {
        _suppressDirty = true;
        try
        {
            foreach (var request in Requests)
            {
                DetachRequest(request);
            }
            Requests.Clear();

            foreach (var requestDocument in document.Requests ?? [])
            {
                var request = HttpRequestItem.FromDocument(requestDocument);
                AttachRequest(request);
                Requests.Add(request);
            }

            WorkspaceName = string.IsNullOrWhiteSpace(document.Name) ? "HTTP 工作空间" : document.Name;
            SelectedRequest = Requests.FirstOrDefault();
            SelectedHeader = null;
            SelectedQueryParameter = null;
            ClearResponse();
        }
        finally
        {
            _suppressDirty = false;
        }
    }

    private bool TryLoadPersistentWorkspace()
    {
        if (!File.Exists(_persistentWorkspacePath))
        {
            return false;
        }

        try
        {
            var info = new FileInfo(_persistentWorkspacePath);
            if (info.Length > MaximumWorkspaceBytes)
            {
                throw new InvalidDataException("自动保存的工作空间超过 10 MB，已跳过加载。");
            }

            var json = File.ReadAllText(_persistentWorkspacePath);
            var document = JsonSerializer.Deserialize<HttpWorkspaceDocument>(json, WorkspaceJsonOptions)
                           ?? throw new InvalidDataException("自动保存的工作空间内容为空。");
            ValidateWorkspaceDocument(document);
            LoadWorkspace(document);
            WorkspacePath = _persistentWorkspacePath;
            StatusMessage = $"已自动加载 {Requests.Count} 个请求";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            StatusMessage = $"自动加载工作空间失败: {ex.Message}";
            return false;
        }
    }

    private async Task<bool> SaveDocumentAsync(string path, HttpWorkspaceDocument document)
    {
        try
        {
            var directory = Path.GetDirectoryName(path)
                            ?? throw new InvalidOperationException("无法确定 HTTP 工作空间保存目录。");
            Directory.CreateDirectory(directory);
            var store = new AtomicJsonConfigStore<HttpWorkspaceDocument>(path, warning => StatusMessage = warning);
            await store.SaveAsync(document);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            MessageBox.Show(
                $"无法保存工作空间。\n\n{ex.Message}",
                "保存失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            StatusMessage = "保存失败";
            return false;
        }
    }

    private string? SelectWorkspaceSavePath(string title, string workspaceName)
    {
        var safeName = string.Concat((string.IsNullOrWhiteSpace(workspaceName) ? "HTTP 工作空间" : workspaceName)
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = "HTTP 工作空间 (*.http-workspace.json)|*.http-workspace.json|JSON 文件 (*.json)|*.json",
            DefaultExt = ".http-workspace.json",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = EnsureWorkspaceDirectory(),
            FileName = safeName + ".http-workspace.json"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string EnsureWorkspaceDirectory()
    {
        var directory = Path.Combine(PathHelper.ExeDir, "Workspaces", "Http");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string GetDefaultPersistentWorkspacePath() =>
        Path.Combine(PathHelper.ExeDir, "Workspaces", "Http", PersistentWorkspaceDirectoryName, PersistentWorkspaceFileName);

    private static void ValidateWorkspaceDocument(HttpWorkspaceDocument document)
    {
        if (document.FormatVersion is < 1 or > 1)
        {
            throw new InvalidDataException($"不支持的工作空间版本: {document.FormatVersion}");
        }
    }

    private HttpRequestItem AddRequestInternal(string name, bool markDirty)
    {
        var request = new HttpRequestItem { Name = name };
        AttachRequest(request);
        Requests.Add(request);
        SelectedRequest = request;
        if (markDirty)
        {
            MarkDirty();
        }
        return request;
    }

    private void AttachRequest(HttpRequestItem request)
    {
        request.PropertyChanged += WorkspaceItemChanged;
        request.QueryParameters.CollectionChanged += NameValueCollectionChanged;
        request.Headers.CollectionChanged += NameValueCollectionChanged;
        foreach (var item in request.QueryParameters.Concat(request.Headers))
        {
            item.PropertyChanged += WorkspaceItemChanged;
        }
    }

    private void DetachRequest(HttpRequestItem request)
    {
        request.PropertyChanged -= WorkspaceItemChanged;
        request.QueryParameters.CollectionChanged -= NameValueCollectionChanged;
        request.Headers.CollectionChanged -= NameValueCollectionChanged;
        foreach (var item in request.QueryParameters.Concat(request.Headers))
        {
            item.PropertyChanged -= WorkspaceItemChanged;
        }
    }

    private void NameValueCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (HttpNameValueItem item in e.OldItems)
            {
                item.PropertyChanged -= WorkspaceItemChanged;
            }
        }
        if (e.NewItems is not null)
        {
            foreach (HttpNameValueItem item in e.NewItems)
            {
                item.PropertyChanged += WorkspaceItemChanged;
            }
        }
        MarkDirty();
    }

    private void WorkspaceItemChanged(object? sender, PropertyChangedEventArgs e) => MarkDirty();

    private void MarkDirty()
    {
        if (!_suppressDirty)
        {
            IsDirty = true;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / (1024d * 1024d):F2} MB";
        }
        if (bytes >= 1024)
        {
            return $"{bytes / 1024d:F1} KB";
        }
        return $"{bytes} B";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
        _httpClient.Dispose();
        foreach (var request in Requests)
        {
            DetachRequest(request);
        }
    }
}

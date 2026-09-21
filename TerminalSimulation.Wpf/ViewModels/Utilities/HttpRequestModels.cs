using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TerminalSimulation.Wpf.ViewModels.Utilities;

public sealed partial class HttpNameValueItem : ObservableObject
{
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _value = string.Empty;

    internal HttpNameValueDocument ToDocument() => new()
    {
        IsEnabled = IsEnabled,
        Name = Name,
        Value = Value
    };

    internal static HttpNameValueItem FromDocument(HttpNameValueDocument source) => new()
    {
        IsEnabled = source.IsEnabled,
        Name = source.Name ?? string.Empty,
        Value = source.Value ?? string.Empty
    };
}

public sealed partial class HttpRequestItem : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty] private string _name = "新请求";
    [ObservableProperty] private string _method = "GET";
    [ObservableProperty] private string _url = string.Empty;
    [ObservableProperty] private string _body = string.Empty;
    [ObservableProperty] private string _contentType = "application/json";

    public ObservableCollection<HttpNameValueItem> QueryParameters { get; } = new();
    public ObservableCollection<HttpNameValueItem> Headers { get; } = new();

    internal HttpRequestDocument ToDocument() => new()
    {
        Id = Id,
        Name = Name,
        Method = Method,
        Url = Url,
        Body = Body,
        ContentType = ContentType,
        QueryParameters = QueryParameters.Select(item => item.ToDocument()).ToList(),
        Headers = Headers.Select(item => item.ToDocument()).ToList()
    };

    internal static HttpRequestItem FromDocument(HttpRequestDocument source)
    {
        var item = new HttpRequestItem
        {
            Id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id,
            Name = string.IsNullOrWhiteSpace(source.Name) ? "未命名请求" : source.Name,
            Method = string.IsNullOrWhiteSpace(source.Method) ? "GET" : source.Method.ToUpperInvariant(),
            Url = source.Url ?? string.Empty,
            Body = source.Body ?? string.Empty,
            ContentType = string.IsNullOrWhiteSpace(source.ContentType) ? "application/json" : source.ContentType
        };

        foreach (var parameter in source.QueryParameters ?? [])
        {
            item.QueryParameters.Add(HttpNameValueItem.FromDocument(parameter));
        }

        foreach (var header in source.Headers ?? [])
        {
            item.Headers.Add(HttpNameValueItem.FromDocument(header));
        }

        return item;
    }
}

internal sealed class HttpWorkspaceDocument
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "HTTP 工作空间";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<HttpRequestDocument> Requests { get; set; } = [];
}

internal sealed class HttpRequestDocument
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Method { get; set; }
    public string? Url { get; set; }
    public string? Body { get; set; }
    public string? ContentType { get; set; }
    public List<HttpNameValueDocument>? QueryParameters { get; set; }
    public List<HttpNameValueDocument>? Headers { get; set; }
}

internal sealed class HttpNameValueDocument
{
    public bool IsEnabled { get; set; } = true;
    public string? Name { get; set; }
    public string? Value { get; set; }
}

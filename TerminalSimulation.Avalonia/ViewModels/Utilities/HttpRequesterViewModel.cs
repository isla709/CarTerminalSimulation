using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TerminalSimulation.Avalonia.ViewModels.Utilities
{
    public partial class HttpKeyValueItem : ObservableObject
    {
        [ObservableProperty] private bool _isEnabled = true;
        [ObservableProperty] private string _key = "";
        [ObservableProperty] private string _value = "";
    }

    public partial class ActualRequestModel : ObservableObject
    {
        [ObservableProperty] private string _method = "";
        [ObservableProperty] private string _url = "";
        public ObservableCollection<HttpKeyValueItem> Headers { get; } = new();
        [ObservableProperty] private string _bodyType = "";
        [ObservableProperty] private string _bodyContent = "";
    }

    public partial class HttpRequesterViewModel : ObservableObject
    {
        [ObservableProperty] private string _id = Guid.NewGuid().ToString("N");
        [ObservableProperty] private string _name = "新建请求";
        [ObservableProperty] private bool _isSelected = false;
        [ObservableProperty] private bool _isRenaming = false;

        public global::Avalonia.Media.IBrush BackgroundBrush => IsSelected ? new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#1A000000")) : global::Avalonia.Media.Brushes.Transparent;

        partial void OnIsSelectedChanged(bool value)
        {
            OnPropertyChanged(nameof(BackgroundBrush));
        }

        private static readonly HttpClient _httpClient = new HttpClient();

        [ObservableProperty] private string _method = "GET";
        public ObservableCollection<string> Methods { get; } = new() { "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS" };

        [ObservableProperty] private string _url = "https://httpbin.org/get";
        
        // Request
        public ObservableCollection<HttpKeyValueItem> Params { get; } = new();
        public ObservableCollection<HttpKeyValueItem> Headers { get; } = new();
        
        [ObservableProperty] private int _bodyTypeIndex = 0; // 0=none, 1=raw, 2=form-data, 3=x-www-form-urlencoded
        
        [ObservableProperty] private int _rawBodyTypeIndex = 0; // 0=JSON, 1=Text, 2=XML, 3=HTML
        [ObservableProperty] private string _rawBody = "";

        public ObservableCollection<HttpKeyValueItem> FormData { get; } = new();
        public ObservableCollection<HttpKeyValueItem> FormUrlEncodedData { get; } = new();

        // Response
        [ObservableProperty] private string _responseBody = "";
        public ObservableCollection<HttpKeyValueItem> ResponseHeaders { get; } = new();
        
        [ObservableProperty] private string _statusCodeStr = "";
        [ObservableProperty] private bool _hasResponse = false;
        [ObservableProperty] private string _timeStr = "";
        [ObservableProperty] private string _sizeStr = "";
        
        [ObservableProperty] private bool _isSending = false;
        
        [ObservableProperty] private int _responseFormatModeIndex = 0; // 0=Pretty, 1=Raw, 2=Preview
        [ObservableProperty] private int _responseLanguageIndex = 0; // 0=JSON, 1=XML, 2=HTML, 3=Text
        [ObservableProperty] private string _responseEncodingStr = "UTF-8";

        public bool IsPreviewMode => ResponseFormatModeIndex == 2;
        public bool IsNotPreviewMode => ResponseFormatModeIndex != 2;

        public ObservableCollection<string> AvailableEncodings { get; } = new()
        {
            "UTF-8", "GB2312", "GBK", "ASCII", "HEX", "Base64", "Binary", "UTF-16", "UTF-32", "ISO-8859-1"
        };

        private byte[]? _rawResponseBytes;

        public ActualRequestModel ActualRequest { get; } = new();

        public HttpRequesterViewModel()
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            // Add some empty slots
            Params.Add(new HttpKeyValueItem());
            Headers.Add(new HttpKeyValueItem());
            FormData.Add(new HttpKeyValueItem());
            FormUrlEncodedData.Add(new HttpKeyValueItem());
        }

        [RelayCommand]
        private void AddParam() => Params.Add(new HttpKeyValueItem());
        [RelayCommand]
        private void RemoveParam(HttpKeyValueItem item) { if (Params.Count > 1) Params.Remove(item); else { item.Key = ""; item.Value = ""; } }

        [RelayCommand]
        private void AddHeader() => Headers.Add(new HttpKeyValueItem());
        [RelayCommand]
        private void RemoveHeader(HttpKeyValueItem item) { if (Headers.Count > 1) Headers.Remove(item); else { item.Key = ""; item.Value = ""; } }

        [RelayCommand]
        private void AddFormData() => FormData.Add(new HttpKeyValueItem());
        [RelayCommand]
        private void RemoveFormData(HttpKeyValueItem item) { if (FormData.Count > 1) FormData.Remove(item); else { item.Key = ""; item.Value = ""; } }

        [RelayCommand]
        private void AddFormUrlEncoded() => FormUrlEncodedData.Add(new HttpKeyValueItem());
        [RelayCommand]
        private void RemoveFormUrlEncoded(HttpKeyValueItem item) { if (FormUrlEncodedData.Count > 1) FormUrlEncodedData.Remove(item); else { item.Key = ""; item.Value = ""; } }

        [RelayCommand]
        private void SetBodyType(string indexStr)
        {
            if (int.TryParse(indexStr, out int index))
            {
                BodyTypeIndex = index;
            }
        }

        [RelayCommand]
        private void SetBodyAndRawType(string type)
        {
            switch (type?.ToLower())
            {
                case "none": BodyTypeIndex = 0; break;
                case "form-data": BodyTypeIndex = 2; break;
                case "x-www-form-urlencoded": BodyTypeIndex = 3; break;
                case "json": BodyTypeIndex = 1; RawBodyTypeIndex = 0; break;
                case "text": BodyTypeIndex = 1; RawBodyTypeIndex = 1; break;
                case "xml": BodyTypeIndex = 1; RawBodyTypeIndex = 2; break;
                case "html": BodyTypeIndex = 1; RawBodyTypeIndex = 3; break;
            }
        }

        [RelayCommand]
        private async Task SendRequest()
        {
            if (string.IsNullOrWhiteSpace(Url)) return;
            IsSending = true;
            ResponseBody = "Sending request...";
            ResponseHeaders.Clear();
            StatusCodeStr = "";
            HasResponse = false;
            TimeStr = "";
            SizeStr = "";

            try
            {
                var finalUrl = Url;
                var activeParams = Params.Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Key)).ToList();
                if (activeParams.Any())
                {
                    var uriBuilder = new UriBuilder(finalUrl);
                    var query = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);
                    foreach (var p in activeParams)
                    {
                        query[p.Key] = p.Value;
                    }
                    uriBuilder.Query = query.ToString();
                    finalUrl = uriBuilder.ToString();
                }

                var request = new HttpRequestMessage(new HttpMethod(Method), finalUrl);

                ActualRequest.Method = Method;
                ActualRequest.Url = finalUrl;
                ActualRequest.Headers.Clear();
                ActualRequest.BodyContent = "";
                ActualRequest.BodyType = "";

                foreach (var h in Headers.Where(x => x.IsEnabled && !string.IsNullOrWhiteSpace(x.Key)))
                {
                    request.Headers.TryAddWithoutValidation(h.Key, h.Value);
                }

                // Add default browser-like headers if missing
                if (!request.Headers.Contains("User-Agent"))
                    request.Headers.TryAddWithoutValidation("User-Agent", "TerminalSimulation/1.0 (Windows NT 10.0; Win64; x64) AvaloniaUI");
                if (!request.Headers.Contains("Accept"))
                    request.Headers.TryAddWithoutValidation("Accept", "*/*");
                if (!request.Headers.Contains("Accept-Encoding"))
                    request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
                if (!request.Headers.Contains("Connection"))
                    request.Headers.TryAddWithoutValidation("Connection", "keep-alive");
                if (!request.Headers.Contains("Host") && request.RequestUri != null)
                    request.Headers.Host = request.RequestUri.Host;

                if (Method != "GET" && Method != "HEAD")
                {
                    if (BodyTypeIndex == 1) // raw
                    {
                        var mediaType = RawBodyTypeIndex switch
                        {
                            0 => "application/json",
                            1 => "text/plain",
                            2 => "application/xml",
                            3 => "text/html",
                            _ => "text/plain"
                        };
                        request.Content = new StringContent(RawBody ?? "", Encoding.UTF8, mediaType);
                        ActualRequest.BodyType = mediaType;
                        ActualRequest.BodyContent = RawBody ?? "";
                    }
                    else if (BodyTypeIndex == 2) // form-data
                    {
                        var mp = new MultipartFormDataContent();
                        foreach (var fd in FormData.Where(x => x.IsEnabled && !string.IsNullOrWhiteSpace(x.Key)))
                        {
                            mp.Add(new StringContent(fd.Value ?? ""), fd.Key);
                        }
                        request.Content = mp;
                        ActualRequest.BodyType = "multipart/form-data";
                        ActualRequest.BodyContent = string.Join("\n", FormData.Where(x => x.IsEnabled).Select(x => $"{x.Key}={x.Value}"));
                    }
                    else if (BodyTypeIndex == 3) // x-www-form-urlencoded
                    {
                        var dict = FormUrlEncodedData.Where(x => x.IsEnabled && !string.IsNullOrWhiteSpace(x.Key))
                                                     .ToDictionary(x => x.Key, x => x.Value ?? "");
                        request.Content = new FormUrlEncodedContent(dict);
                        ActualRequest.BodyType = "application/x-www-form-urlencoded";
                        ActualRequest.BodyContent = string.Join("&", dict.Select(x => $"{x.Key}={System.Web.HttpUtility.UrlEncode(x.Value)}"));
                    }
                }

                // Populate ActualRequest headers by reading the final request state
                foreach (var h in request.Headers)
                {
                    ActualRequest.Headers.Add(new HttpKeyValueItem { Key = h.Key, Value = string.Join("; ", h.Value) });
                }

                if (request.Content != null)
                {
                    foreach (var h in request.Content.Headers)
                    {
                        ActualRequest.Headers.Add(new HttpKeyValueItem { Key = h.Key, Value = string.Join("; ", h.Value) });
                    }
                }

                var sw = Stopwatch.StartNew();
                var response = await _httpClient.SendAsync(request);
                sw.Stop();

                TimeStr = $"{sw.ElapsedMilliseconds} ms";
                StatusCodeStr = $"{(int)response.StatusCode} {response.ReasonPhrase}";
                HasResponse = true;

                foreach (var header in response.Headers)
                {
                    ResponseHeaders.Add(new HttpKeyValueItem { Key = header.Key, Value = string.Join("; ", header.Value), IsEnabled = true });
                }
                if (response.Content != null)
                {
                    foreach (var header in response.Content.Headers)
                    {
                        ResponseHeaders.Add(new HttpKeyValueItem { Key = header.Key, Value = string.Join("; ", header.Value), IsEnabled = true });
                    }
                }

                _rawResponseBytes = await response.Content.ReadAsByteArrayAsync();
                var sizeKilo = _rawResponseBytes.Length / 1024.0;
                SizeStr = sizeKilo >= 1.0 ? $"{sizeKilo:0.00} KB" : $"{_rawResponseBytes.Length} B";

                // Auto detect language
                var responseMediaType = response.Content.Headers.ContentType?.MediaType?.ToLower() ?? "";
                if (responseMediaType.Contains("json")) ResponseLanguageIndex = 0;
                else if (responseMediaType.Contains("xml")) ResponseLanguageIndex = 1;
                else if (responseMediaType.Contains("html")) ResponseLanguageIndex = 2;
                else ResponseLanguageIndex = 3;

                // Auto detect encoding
                var charset = response.Content.Headers.ContentType?.CharSet;
                if (!string.IsNullOrEmpty(charset))
                {
                    var matching = AvailableEncodings.FirstOrDefault(x => x.Equals(charset, StringComparison.OrdinalIgnoreCase));
                    if (matching != null) ResponseEncodingStr = matching;
                }

                UpdateResponseBodyDisplay();
            }
            catch (Exception ex)
            {
                ResponseBody = $"Error: {ex.Message}";
                StatusCodeStr = "Error";
            }
            finally
            {
                IsSending = false;
            }
        }

        partial void OnResponseFormatModeIndexChanged(int value) 
        {
            OnPropertyChanged(nameof(IsPreviewMode));
            OnPropertyChanged(nameof(IsNotPreviewMode));
            UpdateResponseBodyDisplay();
        }
        partial void OnResponseLanguageIndexChanged(int value) => UpdateResponseBodyDisplay();
        partial void OnResponseEncodingStrChanged(string value) => UpdateResponseBodyDisplay();

        [RelayCommand]
        private void SetResponseFormatMode(string mode)
        {
            if (int.TryParse(mode, out int m))
            {
                ResponseFormatModeIndex = m;
            }
        }

        [RelayCommand]
        private void UpdateResponseBodyDisplay()
        {
            if (_rawResponseBytes == null) return;

            if (ResponseEncodingStr == "HEX")
            {
                ResponseBody = BitConverter.ToString(_rawResponseBytes).Replace("-", " ");
                return;
            }
            if (ResponseEncodingStr == "Base64")
            {
                ResponseBody = Convert.ToBase64String(_rawResponseBytes);
                return;
            }
            if (ResponseEncodingStr == "Binary")
            {
                ResponseBody = string.Join(" ", _rawResponseBytes.Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));
                return;
            }

            Encoding encoding = Encoding.UTF8;
            try
            {
                encoding = Encoding.GetEncoding(ResponseEncodingStr);
            }
            catch { }

            var text = encoding.GetString(_rawResponseBytes);

            if (ResponseFormatModeIndex == 0) // Pretty
            {
                if (ResponseLanguageIndex == 0) // JSON
                {
                    try
                    {
                        var doc = JsonDocument.Parse(text);
                        text = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
                    }
                    catch { }
                }
                else if (ResponseLanguageIndex == 1) // XML
                {
                    try
                    {
                        var xdoc = System.Xml.Linq.XDocument.Parse(text);
                        text = xdoc.ToString();
                    }
                    catch { }
                }
            }
            else if (ResponseFormatModeIndex == 2) // Preview
            {
                // In a real app we'd load an HTML viewer. For now, text is enough.
            }

            ResponseBody = text;
        }

        [RelayCommand]
        private void FormatJson()
        {
            if (string.IsNullOrWhiteSpace(RawBody)) return;
            try
            {
                var doc = JsonDocument.Parse(RawBody);
                RawBody = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                // Ignore if invalid JSON
            }
        }
    }
}

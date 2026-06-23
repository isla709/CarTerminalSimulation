using System;
using System.Net;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using TerminalSimulation.Avalonia.ViewModels.Utilities;

namespace TerminalSimulation.Avalonia
{
    public partial class HttpRequesterControl : UserControl
    {
        private TextEditor? _rawBodyEditor;
        private TextEditor? _responseBodyEditor;
        private global::AvaloniaWebView.WebView? _previewWebView;

        public HttpRequesterControl()
        {
            InitializeComponent();
            
            _rawBodyEditor = this.FindControl<TextEditor>("RawBodyEditor");
            _responseBodyEditor = this.FindControl<TextEditor>("ResponseBodyEditor");

            if (_rawBodyEditor != null)
            {
                _rawBodyEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("JavaScript");
                _rawBodyEditor.TextChanged += RawBodyEditor_TextChanged;
            }

            if (_responseBodyEditor != null)
            {
                _responseBodyEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("JavaScript");
            }

            var webViewContainer = this.FindControl<Border>("ResponseWebViewContainer");
            if (webViewContainer != null)
            {
                try
                {
                    _previewWebView = new global::AvaloniaWebView.WebView();
                    webViewContainer.Child = _previewWebView;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to init WebView: {ex.Message}");
                }
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void RawBodyEditor_TextChanged(object? sender, EventArgs e)
        {
            if (DataContext is HttpRequesterViewModel vm && _rawBodyEditor != null)
            {
                if (vm.RawBody != _rawBodyEditor.Text)
                {
                    vm.RawBody = _rawBodyEditor.Text;
                }
            }
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is HttpRequesterViewModel vm)
            {
                vm.PropertyChanged += Vm_PropertyChanged;
                // Initial sync
                if (_rawBodyEditor != null && _rawBodyEditor.Text != vm.RawBody) 
                    _rawBodyEditor.Text = vm.RawBody;
                if (_responseBodyEditor != null && _responseBodyEditor.Text != vm.ResponseBody) 
                    _responseBodyEditor.Text = vm.ResponseBody;
            }
        }

        private void UpdateWebViewContent(HttpRequesterViewModel vm)
        {
            if (_previewWebView == null || vm.ResponseFormatModeIndex != 2) return;

            string content = vm.ResponseBody ?? "";
            
            // If it's not HTML, wrap it in a <pre> tag to emulate a browser's plain text viewer
            if (vm.ResponseLanguageIndex != 2) // 2 is HTML
            {
                string encoded = WebUtility.HtmlEncode(content);
                content = $"<html><body style=\"background-color: white; color: black; margin: 0; padding: 8px; font-family: monospace; word-wrap: break-word; white-space: pre-wrap;\">{encoded}</body></html>";
            }
            
            _previewWebView.HtmlContent = content;
        }

        private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is HttpRequesterViewModel vm)
            {
                if (e.PropertyName == nameof(vm.RawBody))
                {
                    if (_rawBodyEditor != null && _rawBodyEditor.Text != vm.RawBody)
                    {
                        _rawBodyEditor.Text = vm.RawBody;
                    }
                }
                else if (e.PropertyName == nameof(vm.ResponseBody))
                {
                    if (_responseBodyEditor != null && _responseBodyEditor.Text != vm.ResponseBody)
                    {
                        _responseBodyEditor.Text = vm.ResponseBody;
                    }
                    UpdateWebViewContent(vm);
                }
                else if (e.PropertyName == nameof(vm.ResponseFormatModeIndex))
                {
                    UpdateWebViewContent(vm);
                }
                else if (e.PropertyName == nameof(vm.ResponseLanguageIndex))
                {
                    UpdateWebViewContent(vm);
                }
                else if (e.PropertyName == nameof(vm.RawBodyTypeIndex))
                {
                    if (_rawBodyEditor != null)
                    {
                        string? lang = vm.RawBodyTypeIndex switch
                        {
                            0 => "JavaScript", // JSON
                            2 => "XML",
                            3 => "HTML",
                            _ => null
                        };
                        _rawBodyEditor.SyntaxHighlighting = string.IsNullOrEmpty(lang) ? null : HighlightingManager.Instance.GetDefinition(lang);
                    }
                }
            }
        }
    }
}

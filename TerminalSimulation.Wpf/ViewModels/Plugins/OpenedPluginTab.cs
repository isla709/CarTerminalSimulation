using System;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalSimulation.Wpf.ViewModels.Utilities;

namespace TerminalSimulation.Wpf.ViewModels.Plugins
{
    /// <summary>
    /// 表示一个在“实用工具”栏中动态打开的插件页签
    /// </summary>
    public partial class OpenedPluginTab : ObservableObject, IDisposable
    {
        public string Title { get; }
        public string IconKind { get; }
        public FrameworkElement Content { get; }
        public UtilityToolDefinition Tool { get; }

        private readonly Action<OpenedPluginTab> _closeAction;
        private bool _isDisposed;

        public OpenedPluginTab(UtilityToolDefinition tool, FrameworkElement content, Action<OpenedPluginTab> closeAction)
        {
            Tool = tool;
            Title = tool.Name;
            IconKind = tool.IconKind;
            Content = content;
            _closeAction = closeAction;
        }

        [RelayCommand]
        private void Close()
        {
            _closeAction(this);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            if (Content.DataContext is IDisposable disposableVm)
            {
                disposableVm.Dispose();
            }
        }
    }
}

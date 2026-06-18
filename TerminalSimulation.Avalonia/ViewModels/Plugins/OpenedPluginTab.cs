using System;
using Avalonia;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls;
using TerminalSimulation.PluginBase.Avalonia;

namespace TerminalSimulation.Avalonia.ViewModels.Plugins
{
    /// <summary>
    /// 表示一个在“实用工具”栏中动态打开的插件页签
    /// </summary>
    public partial class OpenedPluginTab : ObservableObject
    {
        public string Title { get; }
        public string IconKind { get; }
        public Control Content { get; }
        public IPlugin Plugin { get; }

        private readonly Action<OpenedPluginTab> _closeAction;

        public OpenedPluginTab(IPlugin plugin, Control content, Action<OpenedPluginTab> closeAction)
        {
            Plugin = plugin;
            Title = plugin.Name;
            IconKind = plugin.IconKind;
            Content = content;
            _closeAction = closeAction;
        }

        [RelayCommand]
        private void Close()
        {
            _closeAction(this);
            
            // 如果内容继承了IDisposable，尝试释放
            if (Content.DataContext is IDisposable disposableVm)
            {
                disposableVm.Dispose();
            }
        }
    }
}

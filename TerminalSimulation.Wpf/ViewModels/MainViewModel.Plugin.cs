using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using TerminalSimulation.PluginBase;
using TerminalSimulation.Wpf.Plugins;
using TerminalSimulation.Wpf.Views;
using TerminalSimulation.Wpf.ViewModels.Plugins;
using TerminalSimulation.Wpf.ViewModels.Utilities;
using JT808.Protocol;

namespace TerminalSimulation.Wpf.ViewModels
{
    public partial class MainViewModel : IPluginContext
    {
        public ObservableCollection<IPlugin> UtilityPlugins { get; } = new ObservableCollection<IPlugin>();
        public ObservableCollection<IPlugin> MainPlugins { get; } = new ObservableCollection<IPlugin>();
        public ObservableCollection<UtilityToolDefinition> UtilityTools { get; } = new ObservableCollection<UtilityToolDefinition>();
        public ObservableCollection<OpenedPluginTab> OpenedUtilityTabs { get; } = new ObservableCollection<OpenedPluginTab>();

        [ObservableProperty]
        private int _selectedUtilityTabIndex;

        [ObservableProperty]
        private bool _isUtilityPickerOpen;

        [ObservableProperty]
        private bool _isUtilityPickerClosing;

        [ObservableProperty]
        private bool _isUtilityPickerBusy;

        public event Action<List<byte>>? OnLocationReporting;

        private void InitializePlugins()
        {
            UtilityTools.Add(new UtilityToolDefinition(
                id: "builtin:http-request",
                name: "HTTP 请求",
                description: "发送和调试 HTTP 请求，管理并导入导出请求工作空间",
                iconKind: "Web",
                allowMultipleInstances: true,
                isBuiltIn: true,
                contentFactory: static () => new HttpRequestToolView()));

            var manager = new PluginManager();
            manager.LoadPlugins(this);
            foreach (var plugin in manager.LoadedPlugins)
            {
                if (plugin.Location == PluginLocation.Utility)
                {
                    UtilityPlugins.Add(plugin);
                    UtilityTools.Add(UtilityToolDefinition.FromPlugin(plugin));
                }
                else
                {
                    MainPlugins.Add(plugin);
                }
            }
        }

        [RelayCommand]
        private async Task OpenUtilityToolAsync(UtilityToolDefinition tool)
        {
            if (tool == null || IsUtilityPickerBusy)
            {
                return;
            }

            IsUtilityPickerBusy = true;
            try
            {
                if (!tool.AllowMultipleInstances)
                {
                    var existing = OpenedUtilityTabs.FirstOrDefault(t => t.Tool.Id == tool.Id);
                    if (existing != null)
                    {
                        await CloseUtilityPickerAnimatedAsync();
                        SelectedUtilityTabIndex = OpenedUtilityTabs.IndexOf(existing) + 1;
                        return;
                    }
                }

                // Let the picker finish its exit motion before replacing it with a
                // potentially heavyweight plugin view. This also prevents a native
                // video surface from appearing through the closing overlay.
                await CloseUtilityPickerAnimatedAsync();

                var content = tool.CreateContent();
                if (content == null)
                {
                    throw new InvalidOperationException("工具未返回可显示的界面。");
                }

                var tab = new OpenedPluginTab(tool, content, CloseUtilityTab);
                OpenedUtilityTabs.Add(tab);
                SelectedUtilityTabIndex = OpenedUtilityTabs.Count;
            }
            catch (Exception ex)
            {
                CloseUtilityPickerImmediately();
                Log("工具", $"无法打开工具“{tool.Name}”: {ex.Message}");
                MessageBox.Show(
                    $"无法打开工具“{tool.Name}”。\n\n{ex.Message}",
                    "打开工具失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                IsUtilityPickerBusy = false;
            }
        }

        [RelayCommand]
        private void ShowUtilityPicker()
        {
            if (IsUtilityPickerOpen)
            {
                return;
            }

            IsUtilityPickerClosing = false;
            IsUtilityPickerBusy = false;
            IsUtilityPickerOpen = true;
        }

        [RelayCommand]
        private async Task CloseUtilityPickerAsync()
        {
            await CloseUtilityPickerAnimatedAsync();
        }

        private async Task CloseUtilityPickerAnimatedAsync()
        {
            if (!IsUtilityPickerOpen || IsUtilityPickerClosing)
            {
                return;
            }

            IsUtilityPickerClosing = true;
            if (SystemParameters.ClientAreaAnimation)
            {
                await Task.Delay(180);
            }

            CloseUtilityPickerImmediately();
        }

        private void CloseUtilityPickerImmediately()
        {
            IsUtilityPickerOpen = false;
            IsUtilityPickerClosing = false;
        }

        private void CloseUtilityTab(OpenedPluginTab tab)
        {
            var dynamicIndex = OpenedUtilityTabs.IndexOf(tab);
            if (dynamicIndex < 0)
            {
                return;
            }

            var removedTabIndex = dynamicIndex + 1; // 0 is the built-in message analyzer.
            OpenedUtilityTabs.RemoveAt(dynamicIndex);
            tab.Dispose();

            if (SelectedUtilityTabIndex == removedTabIndex)
            {
                SelectedUtilityTabIndex = Math.Min(removedTabIndex, OpenedUtilityTabs.Count);
            }
            else if (SelectedUtilityTabIndex > removedTabIndex)
            {
                SelectedUtilityTabIndex--;
            }
        }

        partial void OnIsUtilitiesVisibleChanged(bool value)
        {
            if (!value)
            {
                CloseUtilityPickerImmediately();
                IsUtilityPickerBusy = false;
            }
        }

        void IPluginContext.Log(string message)
        {
            Log("插件", message);
        }

        async Task IPluginContext.SendCustomMessageAsync(ushort msgId, byte[] bodyBytes)
        {
            try
            {
                var package = new JT808Package
                {
                    Header = new JT808Header
                    {
                        MsgId = msgId,
                        TerminalPhoneNo = TerminalPhoneNo,
                        MsgNum = 0
                    }
                };

                await SendPackageAsync<JT808.Protocol.MessageBody.JT808_0x0001>(package, bodyBytes);
                Log("插件", $"已发送自定义报文: 0x{msgId:X4}, 长度: {bodyBytes.Length}");
            }
            catch (Exception ex)
            {
                Log("插件", $"发送自定义报文失败: {ex.Message}");
            }
        }
        
        private void TriggerOnLocationReporting(List<byte> rawAppendBytesList)
        {
            OnLocationReporting?.Invoke(rawAppendBytesList);
        }
    }
}

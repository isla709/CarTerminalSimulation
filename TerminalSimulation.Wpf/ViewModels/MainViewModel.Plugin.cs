using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using TerminalSimulation.PluginBase;
using TerminalSimulation.Wpf.Plugins;
using TerminalSimulation.Wpf.ViewModels.Plugins;
using JT808.Protocol;

namespace TerminalSimulation.Wpf.ViewModels
{
    public partial class MainViewModel : IPluginContext
    {
        public ObservableCollection<IPlugin> UtilityPlugins { get; } = new ObservableCollection<IPlugin>();
        public ObservableCollection<IPlugin> MainPlugins { get; } = new ObservableCollection<IPlugin>();
        public ObservableCollection<OpenedPluginTab> OpenedUtilityTabs { get; } = new ObservableCollection<OpenedPluginTab>();

        public event Action<List<byte>>? OnLocationReporting;

        private void InitializePlugins()
        {
            var manager = new PluginManager();
            manager.LoadPlugins(this);
            foreach (var plugin in manager.LoadedPlugins)
            {
                if (plugin.Location == PluginLocation.Utility)
                {
                    UtilityPlugins.Add(plugin);
                }
                else
                {
                    MainPlugins.Add(plugin);
                }
            }
        }

        [RelayCommand]
        private void OpenUtilityPlugin(IPlugin plugin)
        {
            if (!plugin.AllowMultipleInstances)
            {
                var existing = OpenedUtilityTabs.FirstOrDefault(t => t.Plugin == plugin);
                if (existing != null)
                {
                    // Focus logic could be added here if needed
                    return;
                }
            }

            var content = plugin.GetConfigurationPanel();
            var tab = new OpenedPluginTab(plugin, content, t => OpenedUtilityTabs.Remove(t));
            OpenedUtilityTabs.Add(tab);
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

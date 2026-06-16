using System;
using System.Windows;
using TerminalSimulation.PluginBase;
using TerminalSimulation.Plugins.XunjieCloud.ViewModels;
using TerminalSimulation.Plugins.XunjieCloud.Views;

namespace TerminalSimulation.Plugins.XunjieCloud
{
    public class XunjieCloudPlugin : IPlugin
    {
        public string Id => "A5E3F8B2-C1D4-4E5F-8A9B-0C1D2E3F4A5B";
        public string Name => "迅洁云视频流拉取";
        public string Description => "支持通过账号密码登录迅洁云平台，拉取车载终端实时监控视频流";
        public string Author => "Official";
        public string Version => "1.0.0";
        public PluginLocation Location => PluginLocation.Utility;
        public bool AllowMultipleInstances => true;
        public string IconKind => "Puzzle";

        private IPluginContext? _context;

        public void Initialize(IPluginContext context)
        {
            _context = context;
            _context.Log("[迅洁云插件] 插件已加载初始化");
        }

        public FrameworkElement GetConfigurationPanel()
        {
            var viewModel = new XunjieCloudStreamViewModel();
            var view = new XunjieCloudStreamView
            {
                DataContext = viewModel
            };
            return view;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace TerminalSimulation.PluginBase
{
    public enum PluginLocation
    {
        Utility,
        MainTab
    }

    /// <summary>
    /// 车载终端插件核心接口
    /// </summary>
    public interface IPlugin
    {
        /// <summary>
        /// 插件唯一标识 (建议使用 Guid.ToString())
        /// </summary>
        string Id { get; }

        /// <summary>
        /// 插件名称
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 插件描述
        /// </summary>
        string Description { get; }

        /// <summary>
        /// 插件作者
        /// </summary>
        string Author { get; }

        /// <summary>
        /// 插件版本
        /// </summary>
        string Version { get; }

        /// <summary>
        /// 插件显示的位置
        /// </summary>
        PluginLocation Location { get; }

        /// <summary>
        /// 是否允许被多次实例化多开 (用于实用工具栏)
        /// </summary>
        bool AllowMultipleInstances { get; }

        /// <summary>
        /// 插件在 Tab 页签上显示的图标 (适配 MaterialDesignPackIcon 的 Kind 名称，如 "Puzzle")
        /// </summary>
        string IconKind { get; }

        /// <summary>
        /// 初始化插件 (由主程序在加载后调用)
        /// </summary>
        /// <param name="context">宿主环境上下文</param>
        void Initialize(IPluginContext context);

        /// <summary>
        /// 获取插件的配置面板UI (WPF FrameworkElement)
        /// </summary>
        /// <returns>返回将在“实用工具插件”Tab 中渲染的面板</returns>
        FrameworkElement GetConfigurationPanel();
    }
}

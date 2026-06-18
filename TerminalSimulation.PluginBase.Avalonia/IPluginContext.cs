namespace TerminalSimulation.PluginBase.Avalonia
{
    /// <summary>
    /// 主程序提供给插件的宿主环境上下文接口 (Avalonia 版本)
    /// </summary>
    public interface IPluginContext
    {
        /// <summary>
        /// 向主程序的"通信日志"面板输出日志
        /// </summary>
        /// <param name="message">日志内容</param>
        void Log(string message);

        /// <summary>
        /// 0x0200 位置汇报组装时的回调事件。
        /// 插件可订阅此事件，在每次发送位置汇报前，将自定的 RAW 附加字节流（如串口读取的数据）追加到 List 中。
        /// </summary>
        event Action<List<byte>> OnLocationReporting;

        /// <summary>
        /// 向服务端直接发送任意自定义协议报文
        /// </summary>
        /// <param name="msgId">消息ID</param>
        /// <param name="bodyBytes">消息体内容字节流</param>
        Task SendCustomMessageAsync(ushort msgId, byte[] bodyBytes);
    }
}

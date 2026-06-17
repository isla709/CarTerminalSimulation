# 车载定位终端模拟系统 - 插件开发指引

本程序支持通过插件（Plugin）系统进行灵活的扩展。你可以非常容易地开发新的独立功能（如自定义协议解析、实用工具、测试面板等）并将其集成到主程序中，而无需修改主程序的任何代码。

## 1. 快速创建插件项目 (推荐)

我们在项目根目录提供了一个 Python 辅助脚本，帮助你一键生成标准的插件工程文件和模板代码。

**使用方法：**
1. 确保安装了 Python
2. 在项目根目录打开终端（Terminal / CMD / PowerShell）
3. 执行命令：
   ```bash
   python create_plugin.py <你的插件英文名称>
   # 例如：
   # python create_plugin.py VideoAnalysis
   ```
4. 脚本会自动为你创建 `TerminalSimulation.Plugins.VideoAnalysis` 文件夹、`.csproj` 项目文件、基础的 `IPlugin` 实现类。
5. 运行 `dotnet build` 或使用 Visual Studio 进行编译。编译完成后，在你的插件工程的 `bin` 目录下会直接生成一个后缀为 `.Plugin` 的文件。你只需要把这个 `.Plugin` 文件**手动复制或分发**到主程序的 `Plugins` 目录中，重启主程序即可加载。

---

## 2. 插件核心概念

### 基础库引用
所有的插件必须引用 `TerminalSimulation.PluginBase` 项目。
这个库包含了插件必须要实现的接口 `IPlugin` 以及宿主上下文 `IPluginContext`。

### IPlugin 接口要求
你需要在一个 `public class` 中实现 `IPlugin` 接口，主程序启动时会利用反射 (Reflection) 自动寻找并实例化实现了该接口的类。

> [!IMPORTANT]
> 必须确保你的实现类是公开的 (`public`)。

主要属性与方法说明：

- **`Id` (string)**：插件的全局唯一标识。建议使用 GUID 或者具有极强辨识度的包名结构。
- **`Name` (string)**：插件名称，将显示在 UI 的标签页上。
- **`Description` (string)**：插件简介。
- **`Author` (string)**：作者信息。
- **`Version` (string)**：插件版本号。
- **`Location` (PluginLocation)**：指定插件显示的位置。
  - `PluginLocation.Utility`：显示在“实用工具”面板（侧边栏或小工具集合）。
  - `PluginLocation.MainTab`：显示在主程序的顶部主选项卡。
- **`AllowMultipleInstances` (bool)**：是否允许用户多次点击打开该插件的多个独立窗口（适用于 Utility 类型）。
- **`IconKind` (string)**：图标名称，必须填入 `MaterialDesignPackIcon` 的有效 Kind 名称（如 `"Puzzle"`, `"Cloud"`, `"Wrench"` 等）。
- **`Initialize(IPluginContext context)`**：初始化方法。当插件被主程序加载时会最先调用。可以在此保存 `context` 供后续进行与主程序的通信交互。
- **`GetConfigurationPanel()`**：返回 `FrameworkElement`。这是你的插件向主程序提交的 UI 视图。你可以返回任何 WPF 的控件、`UserControl` 或各种布局容器。

### IPluginContext 宿主上下文

通过 `IPluginContext`，你可以和主程序进行互动：
- **`Log(string message)`**：向主程序的日志面板（通信日志/系统日志）打印信息。
- **`SendCustomMessageAsync(ushort msgId, byte[] bodyBytes)`**：让主程序的网络模块帮你向服务器发送任意的自定义（或标准）JT808 报文数据。
- **`OnLocationReporting`**：事件。主程序每次组装发送 `0x0200` 位置汇报时触发，你可以订阅这个事件，在这个阶段把你想要发送的自定义或扩展附加项（如串口读取的扩展状态流）追加进报文里。

---

## 3. 插件开发工作流

1. **生成模板**：使用 `python create_plugin.py <PluginName>`。
2. **编写 UI**：在插件工程里添加 `UserControl`（如 `MainView.xaml`）以及对应的 ViewModel。
3. **返回 UI**：在 `IPlugin` 的实现类 `GetConfigurationPanel()` 中，返回这个实例化后的 `MainView` 控件：
   ```csharp
   public FrameworkElement GetConfigurationPanel()
   {
       var view = new MainView();
       // 绑定 ViewModel
       view.DataContext = new MainViewModel(_context);
       return view;
   }
   ```
4. **利用 Context 交互**：当插件需要向服务端推送数据时，调用 `_context.SendCustomMessageAsync(...)`。
5. **编译与部署**：编译插件项目。因为我们在项目配置中设置了 `<TargetExt>.Plugin</TargetExt>`，编译后你的工程 `bin/Release` （或 Debug）目录下会直接生成一个同名的 `.Plugin` 文件。将这个文件复制粘贴到主程序的 `Plugins` 文件夹内，重启主程序即可在界面上看到你的插件！

> [!TIP]
> 如果你在 UI 中使用了一些第三方库，确保它们在主程序中已经被包含，或者将它们正确输出到了插件目录。

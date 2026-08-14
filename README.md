# 车载定位终端模拟系统 (JT808 / JT1078)

[![Dotnet Version](https://img.shields.io/badge/.NET-8.0--windows-blue.svg)](https://dotnet.microsoft.com/)
[![UI Library](https://img.shields.io/badge/UI-Material--Design--3-purple.svg)](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit)
[![Version](https://img.shields.io/badge/version-preview4-orange.svg)](#)

欢迎使用**车载定位终端模拟系统**。这是一个基于 Windows Presentation Foundation (WPF) 与 .NET 8.0 构建的现代化、高性能车载终端模拟软件。系统深度实现了 **JT808 (道路运输车辆卫星定位系统终端通讯协议及数据格式)** 及 **JT1078 (道路运输车辆卫星定位系统视频通信协议)** 标准规范，旨在帮助车联网平台开发人员、硬件工程师以及测试人员在没有实体车载终端设备的情况下，轻松模拟各种高并发连接、复杂位置轨迹、音频/视频流推送及平台控制交互。

> [!NOTE]
> 本项目采用支持 Per-Monitor V2 DPI 与响应式布局的现代 WPF UI，支持自定义主题壁纸，内嵌进程内 FFmpeg 视频转码管道，并提供类浏览器式按需多开动态插件系统。

---

## 🚀 核心功能特性

### 1. 协议通信与连接管理 (JT808)
*   **双版本协议支持**：可在界面一键切换 **JT808-2013** 与 **JT808-2019** 双版本协议格式。
*   **基础信令全链路模拟**：支持终端注册（`0x0100`）、终端鉴权（`0x0102`）、心跳保活（`0x0002`）以及终端注销流程。
*   **心跳与保活策略**：支持设定自定义心跳间隔，或开启/关闭自动心跳。
*   **控制指令智能响应**：
    *   **0x8300 (文本信息下发)**：实时接收并显示，集成 Windows Speech API 自动进行文本语音合成 (TTS) 播报。
    *   **0x8104 (终端参数查询)** 与 **0x8107 (终端属性查询)**：自动匹配本地配置并组装应答。
    *   **0x8900 (数据下行透传)** 与 **0x8500 (车辆控制)** 等平台控制命令的拦截与解析。

### 2. 0x0200 位置汇报与传感器模拟
*   **多模式循迹上报**：支持手动单次发送位置包，或者设定毫秒级时间间隔自动进行循环上报。
*   **多维度坐标参数**：可动态指定当前经度、纬度、高程、速度、方向（`0-359°`）及系统时钟。
*   **状态与报警图形化配置**：图形化多选框勾选配置各项车辆状态（如 ACC 状态、定位状态、油路、电路等）与报警标志（如超速报警、疲劳驾驶、终端违规开盖等）。
*   **扩展附件通道**：
    *   支持里程附件（`0x01` 附加信息）。
    *   支持油量附件（`0x02` 附加信息）。
    *   支持用户自定义输入十六进制原始数据追加到位置汇报帧尾部，以模拟特定的硬件传感器包。

### 3. JT1078 高性能实时音视频推流引擎
*   **36路并发推流通道**：最多支持配置多达 36 个视频逻辑通道。每个通道独立管理生命周期，可单独建立连接、独立上报流量以及单独关闭。
*   **本地视频智能转码管道 (FFmpeg Integrated)**：
    *   支持加载常见的本地媒体格式（`.mp4`, `.mkv`, `.avi` 等）。
    *   内置 FFmpeg 后台转码机制，自动将非标准视频源重制为符合 JT1078 规范的工业级标准流：强制 `25 FPS` 帧率、`2秒 GOP` (关键帧间隔)、`1Mbps` 码率的 `H.264` 裸流，彻底免疫因用户视频源关键帧跨度太大导致的流媒体网关切片卡死或下级播放器黑屏问题。
*   **双向控制信令拦截**：
    *   **0x9101 (实时音视频传输请求)**：拦截平台点播命令，自动定位逻辑通道并自动拉起 TCP 推流管道。
    *   **0x9102 (音视频传输控制)**：拦截平台控制命令，支持单通道关闭或批量关闭（`ChannelNo = 0`），安全切断网络套接字。
*   **实时性能监控面板**：支持单独监控每个视频通道的实时发送速率（KB/s、MB/s）及总发送流量，并提供全局流量大盘汇总。
*   **底层重置与被动重连**：当遇到网络异常断线时，底层套接字将安全销毁并释放文件句柄，重置通道 UI 指示灯状态。

### 4. 气泡式数据透传与串口转发
*   **聊天气泡式 UI**：透传消息以直观的气泡形式交互展现，直观区分平台下行报文与终端上行报文。
*   **多种字符编码**：支持 UTF-8、GBK 文本编码以及 HEX 十六进制直接发收。
*   **物理串口对接 (System.IO.Ports)**：支持一键绑定真实的 Windows COM 串口，将从串口读入的冷数据直接封装透传发送给车联网平台，同时将平台下发的透传指令重写回物理串口。

### 5. 嵌入式 GIS 地图与轨迹动效
*   **双向通信桥接**：基于 `WebView2` 高速内核，桥接 WPF 后端与 JavaScript 前端环境。
*   **多图源切换**：内置 Leaflet.js 地图框架，无缝支持高德地图与多种高科技感地图滤镜（如深蓝夜色、科技灰等）。
*   **位置动态联动**：在地图上双击即可将坐标自动同步回 0x0200 坐标编辑栏。车辆移动时，地图小车图标不仅会根据 `Direction` 航向角参数实时平滑旋转，还具有动态小车跑动效果。

### 6. JT808 报文即时结构化分析仪
*   **一键解析**：支持直接输入十六进制原始报文（可包含或省略首尾 7E），瞬间完成拆解。
*   **表格解析模式**：扁平化输出。每一行清晰对比字段名称、数据类型、起始字节偏移、数据长度、原十六进制数据以及最终的业务解析结果，极其便于核对通信协议。
*   **树状解析模式**：以属性层级展开复杂的嵌套报文（例如 0x0200 中的各类附加包），支持快捷键一键复制键值对。

---

## 🔬 技术特色与设计方案

### 1. 微包聚合推流策略 (JT1078)
JT1078 协议需要将大型视频帧分割为若干个不超过 950 字节的 RTP 数据包进行传输。
*   **传统的低效方案**：在推送循环中对每个 950 字节的小包直接调用 `await Socket.WriteAsync`，这会导致操作系统在内核态与用户态频繁切换，在大流量多通道并发时极易引发线程池饥饿和 TCP 拥塞窗口震荡。
*   **微包聚合方案**：本系统在内存中使用 `MemoryStream` 将单帧产生的数十个 RTP 数据包进行序列化与拼接，一次性将整帧的聚合 Payload 投递到套接字缓冲区。此举大幅减少了 I/O 系统调用的次数，消除了上下文切换开销，使推流在高带宽占用下依然保持绝对平滑。

### 2. 高精度时间线调度 (SpinWait 机制)
常规的 `Thread.Sleep(1)` 依靠 Windows 系统的时钟中断，具有约 15.6ms 的固有精度限制，无法满足 25 FPS (每帧间隔 40ms) 的精确物理时间间隔控制。
*   本系统设计了**混合等待调度器**：采用粗粒度的 `Task.Delay` 提前释放线程控制权以维护 CPU 健康度，在临近发送时间点的尾部采用纳秒级 `SpinWait.SpinUntil` 混合自旋等待进行精确卡点，确保音视频推流时间戳 (PTS) 匀速行进。
*   **时间戳防溢出保护**：针对 JT1078 `Timestamp` 发生器，基于本地时钟 `DateTimeOffset.UtcNow` 与帧间增量实现双路漂移校正，彻底杜绝了 32 位 PTS 寄存器溢出给流媒体接收端造成的音视频流中断。

### 3. LibVLC 动态压缩与单文件发布
为了提供极佳的分发体验，系统配置了单文件发布（`PublishSingleFile=true`）。
*   WPF 视频预览依赖 LibVLC 原生 DLL（文件体积超过 100MB 且包含复杂的目录结构）。
*   本系统在 MSBuild 编译链中引入了自定义 Target `EmbedLibVLC`：在编译阶段自动将 LibVLC 依赖的物理 DLL 动态压缩为单个 `libvlc.zip` 并作为嵌入式资源编译进可执行文件。程序首次启动时在后台静默解压释放，完美解决了“单文件发布”与“原生库分发体积庞大”之间的矛盾。

---

## 🧩 类浏览器式动态插件系统

为了使系统具备无限的扩展能力，程序设计了一套全新的**动态插件架构**。

### 1. 交互与生命周期设计
*   **固定标签常驻**：系统的核心工具“报文解析器”作为固定标签页常驻第一位。
*   **加号 (PopupBox) 按需打开**：在实用工具选项卡的最右侧，还原了类似浏览器的“+”号按钮。点击加号会弹出一个下拉菜单，列出所有已成功加载的插件。
*   **按需开启与独立销毁**：点击菜单项时，系统会**动态创建**对应插件的 UI 视窗和后台逻辑，并将其作为一个新的 Tab 页签插入。点击 Tab 上的 “X” 关闭按钮即可立刻释放该插件实例，销毁对应的 ViewModel 并回收系统内存。
*   **多开并发支持 (AllowMultipleInstances)**：
    *   插件可以通过定义 `AllowMultipleInstances` 属性声明自身是否允许多开。
    *   对于允许多开的插件，每次新建 Tab 都会生成一套**完全隔离的全新运行状态**（例如：拉取不同终端设备视频流的实例，其各自的播放状态与配置完全独立，互不窜扰）。
    *   对于不需要多开的插件，点击加号菜单时系统将自动聚焦到已打开的既有 Tab。
    *   全局基础设置（如插件账号登录状态）则通过全局的 SettingsManager 保持跨实例的共享。

### 2. 插件核心契约
插件需要引用 `TerminalSimulation.PluginBase` 库，并编写一个公开类实现 `IPlugin` 接口：

```csharp
public interface IPlugin
{
    string Id { get; }                     // 插件全局唯一ID (建议使用 Guid)
    string Name { get; }                   // 插件名称，显示在 Tab 页签上
    string Description { get; }            // 插件详细说明
    string Author { get; }                 // 插件作者
    string Version { get; }                // 插件版本号
    PluginLocation Location { get; }       // 插件加载位置 (Utility 或 MainTab)
    bool AllowMultipleInstances { get; }  // 是否允许打开多个实例
    string IconKind { get; }               // 页签图标名称 (适配 MaterialDesign PackIcon)
    
    void Initialize(IPluginContext context); // 初始化，接收宿主上下文
    FrameworkElement GetConfigurationPanel(); // 返回插件的 WPF 视窗 UI 实例
}
```

插件通过 `IPluginContext` 与主系统交互：
*   `Log(string message)`：向主系统日志控制台输出消息。
*   `SendCustomMessageAsync(ushort msgId, byte[] bodyBytes)`：委托主程序发送任意自定义 JT808 报文。
*   `OnLocationReporting`：监听 0x0200 位置汇报组装事件，在组装阶段向位置包追加自定义硬件传感器附件字节。

### 3. 一键创建插件脚手架
项目根目录下附带了 `create_plugin.py` 辅助脚本，可用于一键生成符合规范的插件子工程。

**使用步骤**：
1. 打开终端，执行：
   ```bash
   python create_plugin.py <PluginName>
   # 例如：python create_plugin.py SensorSimulator
   ```
2. 脚本会自动完成以下动作：
   * 在根目录下创建 `TerminalSimulation.Plugins.<PluginName>` 子文件夹。
   * 自动生成对应的 `.csproj` 配置文件并添加 `PluginBase`、`CommunityToolkit.Mvvm`、`MaterialDesignThemes` 的依赖。
   * 自动配置编译输出文件的扩展名为 `.Plugin`。
   * 生成继承自 `IPlugin` 的基础 C# 模板代码。
   * 自动将新生成的插件项目注册到解决方案文件 `TerminalSimulation.slnx` 中。
3. 使用 Visual Studio 2022 或 `dotnet build` 直接进行整体编译即可。

---

## 📂 项目结构说明

```
CarTerminalSimulation/
├── TerminalSimulation.slnx                # 新版轻量化解决方案声明文件
├── create_plugin.py                       # 插件一键脚手架脚本
├── PLUGIN_DEVELOPMENT_GUIDE.md            # 插件开发技术指引
├── README.md                              # 本说明文件
│
├── TerminalSimulation.Network/            # 异步 TCP 通信网络底座 (net8.0)
│
├── TerminalSimulation.Protocol/           # JT808/JT1078 协议解析与打包逻辑层 (net8.0)
│
├── TerminalSimulation.PluginBase/         # 插件 SDK 接口定义库 (net8.0-windows)
│   ├── IPlugin.cs                         # 插件核心标准接口
│   └── IPluginContext.cs                  # 宿主通信上下文接口
│
├── TerminalSimulation.Wpf/                # 主程序入口及 WPF 展现层 (net8.0-windows)
│   ├── Plugins/                           # 插件加载管理器 (PluginManager)
│   ├── ViewModels/                        # MVVM 视图模型
│   │   ├── MainViewModel.cs               # 主控 VM
│   │   ├── MainViewModel.Configuration.cs # 配置快照与持久化编排
│   │   ├── MainViewModel.Plugin.cs        # 动态插件加载与 Tab 维护逻辑
│   │   └── VideoChannelItem.cs            # JT1078 通道控制及 FFmpeg 转码逻辑
│   ├── Services/                           # 连接、配置、凭据、位置、串口、TTS、视频与日志服务
│   ├── Views/                              # 主窗体拆分后的独立页面
│   ├── MainWindow.xaml                    # 主窗体 XAML 布局
│   └── Regions.json                       # 离线行政区域字典数据
│
└── TerminalSimulation.Plugins.XXXX/       # 具体插件实现目录 (后缀为 .Plugin)
```

---

## 🛠️ 技术栈

| 依赖库/技术 | 版本/规范 | 作用 |
| :--- | :--- | :--- |
| **.NET SDK** | `8.0-windows` | 核心运行时底座 |
| **WPF** | `.NET 8.0 WPF` | 现代化桌面应用程序开发框架 |
| **MaterialDesignThemes** | `5.3.2` | Material Design UI 扁平化风格视觉库 |
| **CommunityToolkit.Mvvm** | `8.4.2` | MVVM 模式驱动，包含源生成器自动补全 |
| **JT808** | `2.7.8` | JT808 标准协议底层解析器 |
| **JT1078** | `1.1.0` | JT1078 音视频协议底层封装器 |
| **Microsoft.Web.WebView2** | `1.0.3967.48` | 内置 Chromium 高性能浏览器内核 (用于渲染地图) |
| **LibVLCSharp.WPF** | `3.9.7.1` | 基于 LibVLC 的本地视频画面预览控件 |
| **FFmpeg Native API** | 审核固定版本 | 通过进程内原生桥接调用所需编解码、封装、缩放与重采样模块 |
| **System.IO.Ports** | `8.0.0` | 串口 COM 通信 API |
| **System.Speech** | `8.0.0` | Windows 原生语音合成 (TTS) 发声服务 |
| **System.Text.Json** | `9.0.0` | 配置文件导入与导出高性能 JSON 序列化 |

---

## ⚙️ 编译与运行部署

### 1. 开发环境要求
*   **操作系统**：Windows 10 / Windows 11 (x64)
*   **开发工具**：Visual Studio 2022 (版本 17.8+) 或 Rider 2023.3+
*   **SDK 环境**：安装有 [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
*   **额外环境**：[Python 3.x](https://www.python.org/) (用于执行 `create_plugin.py` 脚本，非开发插件可选)

### 2. 获取源码与编译
1. 克隆代码仓库：
   ```bash
   git clone <repository-url>
   cd CarTerminalSimulation
   ```
2. 执行还原与编译：
   ```bash
   dotnet restore
   dotnet build --configuration Release
   ```
3. 编译完成后，所有的插件 `.Plugin` 会由构建脚本自动分发至 `TerminalSimulation.Wpf/bin/Release/net8.0-windows/win-x64/Plugins` 目录下。直接运行主程序 `TerminalSimulation.Wpf.exe` 即可启动。

### 3. 输出与分发
本项目不采用单文件发布。正常编译产物位于 `TerminalSimulation.Wpf/bin/<Configuration>/net8.0-windows/win-x64/`，分发时必须保留 `Plugins/`、`ffmpeg-native/`、LibVLC 运行库和配置资源的目录结构。应用不会联网下载或回写 FFmpeg。


---

## 📘 常见问题 (FAQ)

### Q: 启动推流时为什么提示 FFmpeg 未找到？
**A**: 程序不再启动 `ffmpeg.exe`/`ffprobe.exe`。转码通过 `TerminalFfmpeg.Native.dll` 在进程内调用精简的 FFmpeg `libavcodec/libavformat/libavutil/libswscale/libswresample` 与 `libx264`。正式发布前需按 `FFmpeg.Native/README.md` 构建 GPL 原生模块；缺失时请重新安装完整发布包。

> 许可说明：当前 H.264 编码选择 `libx264`，因此 FFmpeg 原生模块及组合发布需按 GPL 要求提供许可证、构建配置和对应源码获取方式。应用发布前必须完成法务与源码分发检查。

### 安全边界

* 迅洁云保存的账号密码使用 Windows DPAPI（当前用户范围）保护；旧版 AES 数据会在成功读取后自动迁移。
* `Plugins/` 中的 `.Plugin` 会获得与主程序相同的本机权限。程序记录每个插件的绝对路径和 SHA-256，但不会阻止第三方插件；仅安装你信任的插件。
* `config.json` 与 `utility_settings.json` 使用临时文件加原子替换；损坏的主配置会保留为 `.corrupt-<时间戳>` 后回退默认值。

### 故障排查

* 启动失败时查看程序目录下的 `startup-crash.log`。
* FFmpeg 转码失败时确认 `ffmpeg-native/TerminalFfmpeg.Native.dll` 及其依赖 DLL 完整存在；程序不会尝试联网下载。
* 插件加载失败时在通信日志中核对插件绝对路径、SHA-256 与加载异常；插件与主程序拥有相同权限。
* 已保存的迅洁云密码无法由当前 Windows 用户解密时，应用会清空密码并提示重新输入，不会回退明文保存。

### Q: 为什么我自己新建的插件 DLL 主程序检测不到？
**A**: 
1. 确保你的插件类是 `public` 的，并且实现了 `IPlugin` 接口。
2. 确保插件编译输出的文件后缀名为 `.Plugin`（如果是手动创建的项目，可以在 `.csproj` 中配置 `<TargetExt>.Plugin</TargetExt>`）。
3. 检查插件是否被拷贝到了主程序输出目录的 `Plugins/` 文件夹中。
4. 可以查看主界面底部的“系统日志”或“通信日志”，如果插件加载失败，这里会打印反射加载时抛出的具体报错异常堆栈。

---

*祝您使用愉快！如有关于 JT808 / JT1078 的协议集成疑问或插件定制问题，请参考 [PLUGIN_DEVELOPMENT_GUIDE.md](file:///e:/Project/CarTerminalSimulation/PLUGIN_DEVELOPMENT_GUIDE.md)。*

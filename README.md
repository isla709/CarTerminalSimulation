# 车载定位终端模拟系统 (JT808 / JT1078)

[![Dotnet Version](https://img.shields.io/badge/.NET-8.0--windows-blue.svg)](https://dotnet.microsoft.com/)
[![UI Library](https://img.shields.io/badge/UI-Material--Design--3-purple.svg)](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit)
[![Version](https://img.shields.io/badge/version-preview11-orange.svg)](#)

欢迎使用**车载定位终端模拟系统**。这是一个基于 Windows Presentation Foundation (WPF) 与 .NET 8.0 构建的现代化、高性能车载终端模拟软件。系统深度实现了 **JT808 (道路运输车辆卫星定位系统终端通讯协议及数据格式)** 及 **JT1078 (道路运输车辆卫星定位系统视频通信协议)** 标准规范，旨在帮助车联网平台开发人员、硬件工程师以及测试人员在没有实体车载终端设备的情况下，轻松模拟各种高并发连接、复杂位置轨迹、音频/视频流推送及平台控制交互。

> [!NOTE]
> 本项目采用支持 Per-Monitor V2 DPI 与响应式布局的现代 WPF UI，支持自定义主题壁纸，内嵌进程内 FFmpeg 视频转码管道，并提供类浏览器式按需多开动态插件系统。

---

## Preview11 更新概览

跨平台迁移前请阅读 [功能、技术特点与迁移交接清单](docs/cross-platform-migration-handoff.md)，其中包含平台耦合、存储兼容约束、源码入口及验收边界。

Preview11 引入持久更新内核和运行时自适配。降级旧版应用时保留新版更新能力，自动选择自包含、系统或私有 .NET 运行时；旧框架依赖版本在没有系统 .NET 时也能再次直接启动。版本切换采用文件事务、启动验证与失败恢复，并保留版本线及不可逆兼容级别保护。

更新窗口采用全新的现代化卡片布局、分阶段进度、平滑过渡和完成反馈，支持安全取消，成功后自动关闭。详见 [Preview11 更新架构与验证说明](docs/preview11-update-architecture.md)。

Preview11 起以单个安装 EXE 发布：手动打开提供安装 / 原目录修复向导、当前用户快捷方式和卸载；由更新器调用时只准备载荷，不弹安装向导。安装包同时保留旧 ZIP 读取兼容性，新内核自动适配两种格式。后续发布使用 `Scripts/New-Release.ps1`，结果位于 `artifacts/publish/`。详见[安装与双格式更新](docs/installer-preview11.md)及[Preview11 更新日志](docs/preview11-release-notes.md)。

## Preview10 更新概览

Preview10 聚焦真实驾驶轨迹模拟与地图操作体验：新增车辆预设、加减速与换挡模型、弯道处理、手动油门 / 刹车及可选随机驾驶事件；同步增强 `old/new` 两套地图的搜索联想、搜索记录、单点选择和轨迹库管理。两套地图的记录与车辆设置独立保存，重复路线自动合并。

主程序和独立更新器新增多尺寸系统图标，启动封面正确裁剪圆角。发现更高版本时可在设置图标下方显示非强制提示气泡，支持“不再提示”和“查看详情”，不会自动安装更新。

完整介绍及升级注意事项见 [Preview10 更新日志](docs/preview10-release-notes.md)；操作说明见 [地图交互](docs/map-ui-preview10.md)、[真实驾驶模拟](docs/vehicle-simulation.md)、[更新提示](docs/update-notification.md) 和 [系统图标](docs/application-icons.md)。

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

### 5. 轨迹模拟与双地图
*   **双向通信桥接**：基于 `WebView2` 高速内核，桥接 WPF 后端与 JavaScript 前端环境。
*   **双地图方案**：完整保留腾讯地图 JavaScript API GL 与 Leaflet 两套相互独立的地图选点实现，可通过 `config.json` 手动选择。
*   **三种选点模式**：导航路线、自由绘制和单点选择。单点模式每次点击替换上一点，应用后同步到位置编辑栏；路线模式可用于轨迹模拟。
*   **搜索与记录**：地点联想、键盘选择、搜索记录管理，以及可恢复、重命名、删除的轨迹库。重复轨迹按模式和有序点去重，保留最新参数；`old/new` 不共用记录。
*   **真实驾驶速度**：保留固定速度模式，另可配置速度上限、期望巡航水平、驾驶风格、动力与变速箱，使用四种车辆预设；支持提前减速入弯、弯心后加速、手动挡操作。Preview11 将随机事件升级为 14 类分阶段交通情景，包括多轮红灯、走走停停、幽灵堵车、行人礼让、合流、施工等，逐类可设开关、概率、机会间隔、时长及轮数，提供三种交通预设。详见[驾驶模型与交通情景设置](docs/vehicle-simulation.md)。
*   **位置动态联动**：右下角显示坐标系、速度及驾驶状态，车辆图标随航向旋转。轨迹、模拟与位置上报保持所选坐标系一致；切换时可选择直接解释坐标或转换以保持地图位置。

地图方案暂不在设置页面中公开。关闭程序后编辑主程序目录中的 `config.json`：

```json
{
  "MapProvider": "new"
}
```

`MapProvider` 支持 `new`（默认，当前地图方案）和 `old`（原地图方案）。修改后重新启动程序生效；未知值会安全回退为 `new`。

搜索 / 轨迹记录位于 `Workspaces/Map/{new|old}/history.json`，车辆设置位于各自的 `vehicle.json`。路线导出使用 `.ctsroute`，保留原始坐标系、点及模拟配置。真实驾驶与随机事件是协议测试用的近似模型，不代表实测车辆性能或真实交通识别。

### 6. JT808 报文即时结构化分析仪
*   **一键解析**：支持直接输入十六进制原始报文（可包含或省略首尾 7E），瞬间完成拆解。
*   **表格解析模式**：扁平化输出。每一行清晰对比字段名称、数据类型、起始字节偏移、数据长度、原十六进制数据以及最终的业务解析结果，极其便于核对通信协议。
*   **树状解析模式**：以属性层级展开复杂的嵌套报文（例如 0x0200 中的各类附加包），支持快捷键一键复制键值对。

### 7. HTTP 请求调试工具
*   **按需多开**：可从实用工具“+”选择器重复打开多个相互隔离的工作实例。
*   **完整请求编辑**：支持 GET、POST、PUT、PATCH、DELETE、HEAD、OPTIONS，请求参数、请求头、Content-Type、原始正文、超时和主动取消。
*   **结构化响应查看**：展示状态码、耗时、响应大小、响应头及响应正文；JSON 自动格式化，响应最大读取 10 MB，避免超大响应阻塞界面。
*   **工作空间管理**：一个工作空间可保存多条请求，支持复制、删除以及 JSON 工作空间的保存、导入和导出。

### 8. 系统日志

原“通信日志”页面现为“系统日志”，统一接收通信、系统及插件消息。三个分类可单独选择或任意组合，默认全部显示；切换筛选不删除隐藏记录，支持自动滚动、复制原始报文/整条日志及全部清空。历史最多保留最近 2000 条，后台突发日志合并刷新 UI。详见 [Preview11 系统日志](docs/system-log-preview11.md)。

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

### 3. LibVLC 动态压缩与目录式发布
本项目采用目录式发布，不采用单文件发布；原生运行库需随完整目录分发。
*   WPF 视频预览依赖 LibVLC 原生 DLL（文件体积超过 100MB 且包含复杂的目录结构）。
*   本系统在 MSBuild 编译链中引入了自定义 Target `EmbedLibVLC`：在编译阶段将 LibVLC 依赖压缩为 `libvlc.zip` 并嵌入程序集，运行时按需释放，以简化原生库分发。该机制不改变整个应用的目录式发布方式。

---

## 🧩 类浏览器式动态插件系统

为了使系统具备无限的扩展能力，程序设计了一套全新的**动态插件架构**。

### 1. 交互与生命周期设计
*   **固定标签常驻**：系统的核心工具“报文解析器”作为固定标签页常驻第一位。
*   **中央工具选择器**：实用工具选项卡最右侧保留类似浏览器的“+”号按钮。点击后会在窗口中央打开图标式工具列表；各种工具统一呈现，支持点击遮罩或按 `Esc` 关闭。
*   **标签溢出处理**：标签栏会在可用宽度内独立滚动；超出范围时显示左右滚动按钮，同时支持鼠标滚轮，并自动将当前选中标签滚入可视区域。“+”按钮始终固定在右侧。
*   **按需开启与独立销毁**：点击工具项时，系统会**动态创建**对应插件的 UI 视窗和后台逻辑，将其插入为新 Tab 并立即切换过去。点击 Tab 上的 “X” 会释放插件实例，并自动选择相邻标签。
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
├── Directory.Build.props                  # 本地内部插件的可选构建配置
├── Shared/                                # Windows 应用标识等共享源码
├── docs/                                  # 更新日志、地图与车辆模拟操作说明
├── TerminalSimulation.Updater/            # 独立 update.exe 及其系统图标
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
│   ├── MapApp/                             # new 地图独立 Web UI
│   ├── MapAppOld/                          # old 地图独立 Web UI 与固定版本依赖
│   ├── Assets/                             # 主程序多尺寸图标
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
   dotnet build --configuration Debug
   ```
3. 默认构建输出至 `TerminalSimulation.Wpf/bin/Debug/net8.0-windows/win-x64/`，已配置的插件输出至其中的 `Plugins/`。运行 `TerminalSimulation.Wpf.exe` 即可启动。需要 Release 时显式使用 `dotnet build --configuration Release`，输出至对应 `bin/Release/`。

构建前将腾讯地图 Key 放入本地 `docs/KEY.txt`，或设置环境变量 `CTS_TENCENT_MAP_KEY`；构建会调用 [凭据生成脚本](Scripts/Generate-TencentMapCredential.ps1) 生成中间源码。本地明文 Key 与生成文件不得提交；客户端内嵌凭据不能替代服务端访问控制与配额管理。

### 3. 输出与分发
本项目不采用单文件发布。正常编译产物位于 `TerminalSimulation.Wpf/bin/<Configuration>/net8.0-windows/win-x64/`，分发时必须保留 `Plugins/`、`ffmpeg-native/`、LibVLC 运行库和配置资源的目录结构。应用不会联网下载或回写 FFmpeg。

交付 / 打包产物放在 `artifacts/{结果特征或版本名}/`，发布产物放在 `artifacts/publish/`。公开构建应显式传入 `-p:IncludeXunjieCloudPlugin=false` 并使用新的发布目录，避免包含本地内部插件或残留二进制，详见 [内部插件约定](docs/internal-plugins.md)。


---

## 📘 常见问题 (FAQ)

### Q: 启动推流时为什么提示 FFmpeg 未找到？
**A**: 程序不再启动 `ffmpeg.exe`/`ffprobe.exe`。转码通过 `TerminalFfmpeg.Native.dll` 在进程内调用精简的 FFmpeg `libavcodec/libavformat/libavutil/libswscale/libswresample` 与 `libx264`。正式发布前需按 `FFmpeg.Native/README.md` 构建 GPL 原生模块；缺失时请重新安装完整发布包。

> 许可说明：当前 H.264 编码选择 `libx264`，因此 FFmpeg 原生模块及组合发布需按 GPL 要求提供许可证、构建配置和对应源码获取方式。应用发布前必须完成法务与源码分发检查。

### 安全边界

* 迅洁云为本地内部插件，不再随公开仓库提供源码；仅本地安装该插件时，其保存密码使用 Windows DPAPI（当前用户范围）保护，旧版 AES 数据在成功读取后迁移。
* `Plugins/` 中的 `.Plugin` 会获得与主程序相同的本机权限。程序记录每个插件的绝对路径和 SHA-256，但不会阻止第三方插件；仅安装你信任的插件。
* `config.json` 与 `utility_settings.json` 使用临时文件加原子替换；损坏的主配置会保留为 `.corrupt-<时间戳>` 后回退默认值。

### 故障排查

* 启动失败时查看程序目录下的 `startup-crash.log`。
* FFmpeg 转码失败时确认 `ffmpeg-native/TerminalFfmpeg.Native.dll` 及其依赖 DLL 完整存在；程序不会尝试联网下载。
* 插件加载失败时在“系统日志”页面勾选“插件日志”，核对插件绝对路径、SHA-256 与加载异常；插件与主程序拥有相同权限。
* 本地内部迅洁云插件的密码无法由当前 Windows 用户解密时，会清空密码并提示重新输入，不会回退明文保存。

### Q: 为什么我自己新建的插件 DLL 主程序检测不到？
**A**: 
1. 确保你的插件类是 `public` 的，并且实现了 `IPlugin` 接口。
2. 确保插件编译输出的文件后缀名为 `.Plugin`（如果是手动创建的项目，可以在 `.csproj` 中配置 `<TargetExt>.Plugin</TargetExt>`）。
3. 检查插件是否被拷贝到了主程序输出目录的 `Plugins/` 文件夹中。
4. 可以查看主界面的“系统日志”页面并勾选“插件日志”，核对反射加载时抛出的异常信息。

---

*祝您使用愉快！如有关于 JT808 / JT1078 的协议集成疑问或插件定制问题，请参考 [插件开发指引](PLUGIN_DEVELOPMENT_GUIDE.md)。*

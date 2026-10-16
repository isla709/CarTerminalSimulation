# CarTerminalSimulation 功能与技术迁移交接清单

本文面向后续会话制定、实施跨平台迁移。它描述当前实现、必须保留的行为和平台耦合，不替代目标平台/技术栈选型，也不表示跨平台迁移已经完成。

## 1. 快照与阅读边界

- 整理日期：2026-10-15；当前主程序版本序列：**Preview11**；持久更新引擎：**3.0.0**，旧更新协议接口仍为 **v2**。
- 当前 Git HEAD：`37c089a`（Preview10）。**工作区还有未提交的 Preview11 更新系统及 UI 修改，也有未跟踪的新源码/脚本。只检出 HEAD 会丢失这些实现。** 开始迁移前先读 `git status`，保存当前完整工作树，不能自行 reset、清理或覆盖。
- 依据：当前工程文件、网络/协议/业务/UI 源码及 `docs/`。发生冲突时优先核对实际调用路径，不把 README 的概括或旧注释当作全部实现。
- 上一轮 Windows 验证：不包含内部插件的 **184 项测试通过**；本次只整理文档，没有重新执行测试。
- 尚未指定目标操作系统、CPU 架构、桌面框架，以及是否保留 C# 后端。因此以下“可复用”是候选判断，不是已在 Linux/macOS 编译验证的结论。

## 2. 产品定位与现有架构

这是供车联网平台联调使用的**车载定位终端模拟与协议调试桌面程序**：模拟 JT808 终端、位置及轨迹、JT1078 音视频，提供串口透传、报文分析、HTTP 调试和插件扩展。

主通信围绕一个终端会话组织；最多 36 个音视频逻辑通道，**不能等同于已具备 36 辆车或大规模多终端压测能力**。现有驾驶模型也不是高保真车辆动力学或真实交通识别系统。

当前主要依赖：

| 工程/资源 | 当前定位 | 跨平台边界 |
| --- | --- | --- |
| `TerminalSimulation.Network` | `net8.0`，TCP 收发与会话生命周期 | 复用优先级高，仍需目标平台网络测试 |
| `TerminalSimulation.Protocol` | `net8.0`，JT808/JT1078 编解码与推流 | 复用优先级高；时间调度、第三方包兼容要验证 |
| `TerminalSimulation.PluginBase` | `net8.0-windows`、`UseWPF`，插件契约 | 接口直接返回 `FrameworkElement`，不是跨平台 SDK |
| `TerminalSimulation.Wpf` | `net8.0-windows`、`win-x64`；UI 与大量业务服务混放 | 不能整体移植；需拆出业务和平台适配 |
| `TerminalSimulation.Updater` | 框架依赖 WPF 更新引擎，使用随包私有 .NET 运行时 | 通用更新规则可提取，UI/运行时检测/启动验证需重做适配 |
| `Updater.Native` | Windows C++ 更新入口、旧应用宿主 | Win32/COM/Job Object/hostfxr，需平台独立实现 |
| `FFmpeg.Native` | C ABI 的进程内转码桥 | 算法可保留；构建、DLL 加载、字符串 ABI 不可直接照搬 |
| `MapApp` / `MapAppOld` | 两套 HTML/CSS/JavaScript 地图 | 前端资产有复用价值，但必须替换 WebView 宿主桥接 |
| `Shared/WindowsAppIdentity.cs` | Windows AppUserModelID | 平台实现，不能进入通用业务内核 |
| `TerminalSimulation.Tests` | 当前整体为 Windows 测试工程，包含 WPF 测试 | 应拆出可独立执行的 Core 测试与平台 UI 测试 |

主要 NuGet 版本，以 `.csproj` 为准：JT808 `2.7.8`、JT1078 `1.1.0`、JT808 JT1078 扩展 `2.7.8`、CommunityToolkit.Mvvm `8.4.2`、MaterialDesignThemes `5.3.2`、AvalonEdit `6.3.1.120`、WebView2 `1.0.3967.48`、LibVLCSharp.WPF `3.9.7.1`、System.IO.Ports/System.Speech `8.0.0`、System.Text.Json/ProtectedData `9.0.0`。协议工程还引用 Microsoft.Extensions.DependencyInjection `10.0.9`。

注意：`net8.0` 是目标框架，不等于开发机只安装了 .NET SDK 8。`.slnx`、构建脚本、实际 SDK 及原生工具链需单独核对。

## 3. 各功能及技术特点

本节未单独注明工程的 `Services/`、`ViewModels/`、`Views/` 及窗口文件，均相对 `TerminalSimulation.Wpf/`。

### 3.1 JT808 连接、终端身份与信令

**功能**：平台地址/端口、终端手机号、协议版本、注册身份等参数；注册、获取鉴权码、鉴权、注销、心跳及开关；下行消息处理与应答。

**实现**：

- `TerminalNetworkClient` 使用 `TcpClient`/`NetworkStream`、异步读写、取消令牌、生命周期锁和会话 ID。
- 发送通过单读者 `Channel` 串行化，多调用方不会并发打乱同一 TCP 流；每项等待写入完成。**队列目前无界，不是有界背压设计。**
- 接收按 `0x7E` 边界组帧，支持 TCP 粘包/拆包；单帧上限 64 KiB。转义、校验、2013/2019 头及消息体由协议库处理。
- `JT808Manager` 封装序列化、反序列化、分析与字符编码；业务连接由 `TerminalConnectionSession` 适配。
- 已有明确下行处理：`0x8100` 注册应答、`0x8107` 属性查询、`0x8104` 参数查询、`0x8300` 文本、`0x8900` 透传、`0x9101/0x9102` 音视频请求/控制；其他部分下行可走通用应答。

**迁移约束**：保留消息流水号、版本标识、编码、帧边界、断开取消和旧会话失效语义。`MainViewModel.Protocol.cs` 中的消息名称翻译表**不是完整业务支持清单**，不能据此宣称所有 JT808 指令已实现。不要把被动断线清理描述成无限自动重连。

**入口**：`TerminalSimulation.Network/TerminalNetworkClient.cs`、`TerminalSimulation.Protocol/JT808Manager.cs`、`TerminalSimulation.Wpf/Services/TerminalConnectionSession.cs`、`ViewModels/MainViewModel.Protocol.cs`。

### 3.2 位置上报、状态、报警与附件

**功能**：单次/周期 `0x0200` 汇报；编辑经纬度、高程、速度、方向、时间；位字段式状态/报警选择；标准附件与自定义 RAW 附件；配置导入导出。

另有基础车辆数据模拟：按当前速度累计里程、按 L/100km 油耗降低油量、将记录速度附件同步到主速度，以及可选网络信号/GNSS 数量波动。这些不是“真实驾驶”开关的同义功能，迁移不能遗漏。

**技术特点**：

- 位置快照统一参与组包，避免后台读 UI 过程中混用不同时间的字段。
- 附件包括里程 `0x01`、油量 `0x02`、记录速度 `0x03`、报警事件 `0x04`、扩展车辆信号 `0x25`、IO `0x2A`、模拟量 `0x2B`、网络信号 `0x30`、GNSS 数量 `0x31` 等。
- 插件可在汇报组装事件中追加附件字节。
- 真实驾驶模型的实际速度回写位置字段，协议速度精度为 0.1 km/h；模拟本身不自动开启周期上报。
- 基础里程/油量模拟目前按每秒回调的增量累加，分别使用 0.1 km/0.1 L 的协议量纲并保留小数累积；它与路线服务按单调时钟积分的方式不同，改计时机制前应有等价测试。

**迁移约束**：保持坐标系、数值范围、单位、取整精度、符号/状态位、附件长度和时间语义。平台协议要求与本程序当前兼容模式应分别说明，不能静默改写用户选择的坐标。

**入口**：`ViewModels/MainViewModel.cs`、`MainViewModel.Configuration.cs`、`MainViewModel.Protocol.cs`、`Views/DevicePropertiesView.*`。

### 3.3 轨迹模拟：双地图选点与导航

**共同功能**：导航路线、自由绘制、单点选择；搜索及联想、起终点设置、候选路线、车辆跟随、航向图标、清理/应用/开始/停止；Web 内独立设置卡片；默认城市或自动网络定位。

| 项目 | `new` | `old` |
| --- | --- | --- |
| 地图前端 | `MapApp/`，腾讯地图 JavaScript API GL | `MapAppOld/`，Leaflet 与本地固定版本依赖 |
| 地点服务 | 腾讯搜索、建议、IP 定位 | Photon 搜索/联想，另有网络定位逻辑 |
| 路线服务 | 腾讯驾车规划，解析折线/步骤/候选信息 | OSRM，WGS-84 GeoJSON 路线与备选 |
| 视图 | 2D/3D | 2D，不虚构 3D 控件 |
| 地图样式 | 默认及当前绑定的 Style 1“墨渊” | 现有五种地图风格、四种路线样式 |
| 凭据 | 构建时嵌入腾讯 Key | 不使用腾讯 Key，但依赖公共服务可用性 |

**宿主实现**：WebView2 虚拟 HTTPS 域名映射本地资源，通过 WebMessage 和 `ExecuteScriptAsync` 双向通信；搜索、规划、历史、文件对话框等由 C# 宿主编排。迁移时须盘点消息类型、请求 ID/取消、JSON 结构及 JS 回调，不只是换一个 HTML 容器。

**交互特点**：400 ms 联想防抖、输入法组合期间不发请求、过期响应不覆盖新查询、键盘选择、缓存；old 服务额外限流/缓存。拖动阈值避免拖图误选点，禁用浏览器原生图像拖放；手动拖图暂停跟随，定位按钮恢复。

**重要约束**：

- `config.json.MapProvider` **仅用 `old` / `new`**，默认 `new`，未知值回退；暂不加入普通设置 UI，修改后重启。
- 两套前端/检索路径完整保留，不把 old 变为 new 的皮肤，也不把两套历史混合。
- 网络定位是近似网络位置，不是保证可得的精确 GPS；失败应允许正常使用地图，不弹阻塞式脚本错误。
- 地图/路线服务的实际坐标要在服务边界转换；old 不显示其服务未提供的红绿灯统计；公共服务不等于离线能力或 SLA。
- 腾讯客户端 Key 的加密嵌入只是分发手段，不能承诺客户端密钥不可提取。迁移必须重新检查 Web 宿主来源、白名单、网络权限、配额和错误提示；不复制明文凭据。
- 保留 SDK 版权/归属标识；移除的是应用自定义标题中多余的“腾讯地图”强调，不是服务商版权。

**入口**：`MainWindow.xaml.cs` 地图桥接、`TencentMapService.cs`、`OldMapService.cs`、`TencentMapKeyProvider.cs`、两套 Web 目录及 `docs/map-ui-preview10.md`。

### 3.4 坐标系、路线文件与独立历史

**功能**：WGS-84、GCJ-02、BD-09；路线专用 `.ctsroute` 保存/打开；搜索记录、轨迹库的恢复/重命名/删除/清空与重复合并。

**不可改变的行为**：

1. **原始轨迹、模拟与 JT808 上报保持用户选择的同一逻辑坐标系**。渲染到底图的转换属于显示边界，不改变保存值。
2. 打开 WGS-84 文件切换为 WGS-84；打开 GCJ-02 文件切换为 GCJ-02；不能强制全部转成 WGS-84。
3. 切换坐标系必须明确区分“直接按新坐标系解释相同数值”和“转换数值以保持地图位置不变”。
4. `.ctsroute` 保存原始坐标系、点、选点模式、速度及车辆配置；单点恰好一个点，路线至少两个点，加载校验范围/结构。
5. old/new 历史及车辆配置独立；搜索最多 50 条、轨迹最多 30 条。轨迹库是**模拟路线快照**，不是 GPS 实测采集记录。
6. 去重按模式与有序物理位置，不按名字、速度或车型预设；转换后逐点 0.5 米容差可判重，但不改写保存值。反向路线、不同模式不应误合并。
7. 重复保存更新最新参数/时间，自动记录保留用户重命名；首次历史去重留专用 `.dedupe-*.bak`。

**技术特点**：C# 坐标转换、JSON 文档校验、串行写入与原子保存；历史不依赖 WebView 浏览器缓存。

**入口**：`GeoCoordinateConverter.cs`、`MapHistoryStore.cs`、`MainWindow.xaml.cs` 路线文件流程。注意：坐标转换器顶部仍有“JT808 上报使用 WGS-84”的旧概括注释，**不能覆盖上述当前业务约定与实际调用行为**。

### 3.5 真实驾驶轨迹与随机事件

**功能**：固定速度模式保留；真实驾驶配置最大速度、期望平均/巡航速度、风格、加速/制动能力、动力、变速箱及挡位参数；四种车辆预设；手动挡自动/手动操作、换挡、油门与刹车；驾驶遥测面板。

**模型特点**：

- 路线按距离采样，结合转角/近似半径与前视减速包络决定建议速度；弯前减速、弯心开始加速，90° 右转更谨慎，连续同向折点合并，终点停车。
- 汽油、柴油、电动、混动与 manual/automatic/CVT/DCT/singleSpeed 为近似模型；支持最多 12 挡、齿比校验、转速限制和换挡动力中断。
- 手动操作控制油门/刹车/挡位；过转降挡会拒绝；自动操作随时恢复。
- 约 100 ms 更新位置/遥测，积分子步不大于约 50 ms；使用单调时钟，暂停后限制时间步长，避免跳跃。
- 巡航有平滑波动；“期望平均”不是数学上强制保证的全程平均值。
- Preview11 交通情景替换旧五种测试事件：单轮/多轮红灯、无保护左转、行人礼让、走走停停、幽灵堵车、慢车跟行、邻车切入、合流、公交占道、施工、应急避让、减速带及可选铁路道口（14 类）。总开关默认关闭，仅自动操作生效，停车倒计时只在实际停稳后减少；多轮包含起步反应、空间队列推进、再次停车和恢复。
- `TrafficEventOptions/Rule` 独立配置逐类开关、机会概率、平均机会间隔、阶段时长、轮数、缓行速度，以及全局冷却和直路虚拟路口间距。独立指数机会时钟 + 概率抽签，一次一事件，不积压。两套地图设置独立，随车辆配置、路线文件/轨迹库保存；旧配置补默认值。`TrafficEventCatalog` 是 Web UI 元数据的 C# 唯一来源。
- 随机情景支持注入随机种子测试；速度受车辆、弯道、终点约束。只改变纵向速度，不虚构横向轨迹或真实交通实体；当前没有实时信号/路况数据，转向条件只按路线几何判断。切手动立即取消事件，回自动不恢复旧事件。

**迁移约束**：同一实际速度用于推进轨迹与位置上报；恢复路线恢复配置，不自动重放上次手动控制。不要将几何推断路口和虚拟红灯宣传成实时交通/行人识别。没有经过车型标定的扭矩、离合、坡度、载荷、轮胎附着模型。

**入口**：`VehicleSimulation.cs`（Options/Driver/Telemetry）、`TrafficEventOptions.cs`（目录/逐类配置）、`VehicleEventController.cs`（调度/阶段机）、`LocationSimulationService.cs`（路径/速度包络/位置积分）、Web `vehicle.js/css`、`docs/vehicle-simulation.md`。模型本身多为普通 C#，但当前 `GeoPoint` 类型仍在 ViewModels 中，提取时要清理这种反向依赖。

### 3.6 JT1078 音视频推流

**功能**：最多 36 路逻辑通道；本地文件、通道预览/静音、缩略图、转码、实时推流与单通道/全部关闭；视频、音频或音视频组合；每通道和全局流量统计；处理平台 `0x9101/0x9102`。

**协议特点**：

- `JT1078Pusher` 使用独立 TCP 推流会话，H.264 帧分割为不超过 950 字节的协议负载，单帧多个协议包聚合后写网络，减少小包系统调用。
- G.711A 与 AAC 分支，音视频时戳、序号、关键帧及分片标记分别组织；时间戳以 Unix 毫秒为起点递增。
- 匀速模式采用 `Stopwatch`、粗粒度 `Task.Delay` 加末段 `SpinWait`，并处理明显落后；不是纳秒级实时调度保证。
- 断开/停止需要取消循环、释放流/套接字、更新 UI；不能保留后台幽灵推流。

**迁移约束**：先迁移并回归协议字节与时序，再替换播放器控件。36 路是功能配置上限，不是已完成所有机器/平台下 36 路长稳压力证明。需要目标平台的接收端互通、并发、丢线和长时间测试。

**入口**：`TerminalSimulation.Protocol/JT1078Pusher.cs`、`Services/Jt1078VideoPushSession.cs`、`ViewModels/VideoChannelItem.cs`。

### 3.7 进程内媒体转码与预览

**转码**：`IInProcessMediaTranscoder` → `NativeFfmpegTranscoder` → `TerminalFfmpeg.Native` C ABI；调用 libavformat/libavcodec/libavutil/libswscale/libswresample 与 libx264，不启动/下载 `ffmpeg.exe` 或 `ffprobe.exe`。

- C API 提供 `tf_probe`、`tf_transcode`、`tf_thumbnail`、`tf_last_error` 和进度回调。
- 当前实际参数：非 `.h264` 文件优先采用探测帧率，失败回退 25 FPS；GOP 约 2 秒、至少 10 帧；调用参数 MaxBitrate **2,000,000**、BufferSize **4,000,000**。原生 `.h264` 直接使用并默认 25 FPS。
- README 中“强制 25 FPS/1 Mbps”的旧描述与当前调用不一致，迁移不得据此重写参数。
- 桥接目前用进程级锁和单活动进度回调串行转码；托管取消只在原生调用前后检查，**不是随时可中断的原生转码**。
- C ABI 用 Windows `wchar_t` 路径、`__cdecl`，C# 用 Unicode P/Invoke，库名/目录为 `.dll`；C 中存在 Windows 路径适配。不能因为是 C ABI 就直接换成 `.so/.dylib`，须统一字符串编码、调用约定、导出、回调和生命周期。

**预览**：LibVLCSharp.WPF/LibVLC，播放器延迟创建，结束释放；构建把 x64 LibVLC 压成资源，运行时释放。视频 HWND 存在裁剪/遮挡处理。

**迁移约束**：按 OS/架构重建 FFmpeg、x264 和播放器原生依赖；保留媒体 probe/转码/缩略图/音轨回退行为；当前 Windows LibVLC 压缩包不可跨平台复用。仓库将此 FFmpeg+x264 组合标为 GPL 发布路径，必须保留其许可/来源材料并交由发布审查，不在迁移时遗漏。

**入口**：`Services/Media/`、`FFmpeg.Native/native/`、`FFmpeg.Native/build-win-x64.ps1`、`VideoChannelItem.cs`、`FFmpeg.Native/README.md`。

### 3.8 数据透传、串口与文本播报

**透传**：聊天式收发气泡；UTF-8、GBK、HEX；`0x0900` 上行与 `0x8900` 下行，可转接物理串口。`ISerialPortService` 已隔离枚举、打开、收发、关闭，当前实现为 System.IO.Ports。

**文本**：`0x8300` 文本展示及 TTS，支持系统声音选择；`ITtsService` 当前由 System.Speech.Synthesis 实现，后台任务调用同步 Speak。

**迁移约束**：保留文本/原始字节区分、编码和串口回调线程语义；不能继续硬编码 COM 名称。目标平台的设备权限、拔插、忙占用、串口底层支持和 TTS 后端需分别实现/测试。当前 TTS 的取消也是前后检查，不能声称中途立即停止。

**入口**：`SerialPortService.cs`、`SystemTtsService.cs`、`ApplicationServices.cs`、`Views/DataPassthroughView.*`、`Views/TextDownlinkView.*`。

### 3.9 报文分析与系统日志

**分析**：十六进制输入，经 JT808 分析结果 JSON 生成树和扁平表；字段、原始值、长度/偏移、类型、中文解释；部分 IO 位有自定义细化。解析错误留在工具中显示。

**技术边界**：表格偏移/类型根据分析 JSON 中的十六进制字段标记推导，不是另一套独立协议解析器；应保留回归样例，不把展示层推断当作所有消息的精确类型定义。

**日志**：UI 分类显示发送/接收/解析/错误，包含时间及连接/通道/消息 ID 上下文；`ConsoleLogger` 可附着父控制台或按 `--console/-c/--verbose/-v` 创建控制台，使用 kernel32。

Preview11 已将原“通信日志”页面改名为“系统日志”。`Services/ApplicationLog.cs` 提供与 UI/控制台是否开启无关的进程级有界历史，记录明确的通信/系统/插件分类；`SystemLogViewModel` 使用 WPF 集合视图组合筛选、合并后台刷新，不删除被隐藏消息。原 `CommunicationLogView` 类名保留，界面名称已更新。迁移应保留分类元数据、全部清空、最近 2000 条和筛选/记录分离语义，而非用显示文字临时猜分类。

**迁移约束**：协议分析和展示 DTO 应拆开；保留复制、清空、上下文关联与错误诊断；把 Windows 控制台附着从通用日志接口中分离，不承诺尚未实现的统一磁盘日志轮转。

**入口**：`JT808Manager.cs`、`MainViewModel.Protocol.cs`、`Services/AppLogger.cs`、`ConsoleLogger.cs`、`Views/MessageAnalyzerView.*`、`Views/CommunicationLogView.*`。

### 3.10 HTTP 调试与代码编辑

**功能**：多工具实例；多请求工作空间；GET/POST/PUT/PATCH/DELETE/HEAD/OPTIONS；参数、头、Content-Type、正文、超时、取消；状态码、耗时、大小、响应头/正文；复制/删除/保存/导入/导出。

**技术特点**：

- HttpClient、自动解压、重定向；请求级取消/超时，ResponseHeadersRead，工作空间和响应读取各有 10 MiB 上限；超大响应截断，不当作完整响应。
- 根据 Content-Type 和内容识别 JSON/XML，格式化/压缩/校验；JSON 显示可读 Unicode，二进制响应不强行作为文本解析。
- AvalonEdit 实现行号、语法高亮、缩进、括号/引号配对与 JSON 片段/键名/值补全；不是 VSCode/Monaco 或完整语言服务器。
- `StructuredBodyFormatter` 是普通 C# 候选；编辑器、剪贴板、MessageBox、Open/SaveFileDialog 是 UI 适配。

**不可改变的语义**：

- “保存”直接写固定路径 `Workspaces/Http/.Save/workspace.http-workspace.json`，下次创建工具自动加载；“导出”才打开选择器创建外部文件；“导入”读取工作空间。
- 多开实例运行内存状态独立，**默认持久化文件仍是同一个固定路径，并非每实例一个永久命名空间**。迁移应保持兼容或先单独设计多实例保存冲突策略，不能文档中误称已完全隔离存储。
- 当前是显式保存后自动加载，不等同于所有编辑实时自动落盘；持久化应保留未保存标记与失败提示。

**入口**：`ViewModels/Utilities/HttpRequestToolViewModel.cs`、`HttpRequestModels.cs`、`StructuredBodyFormatter.cs`、`Views/Controls/StructuredCodeEditor.*`、`Views/HttpRequestToolView.*`。

### 3.11 动态插件与实用工具工作区

**功能**：固定报文分析标签；“+”工具选择器；按需创建、多开声明、重复打开聚焦、关闭释放；标签溢出滚动；插件可放 Utility 或 MainTab。

**实现**：扫描 `Plugins/*.Plugin`，`Assembly.LoadFrom` + 反射寻找 `IPlugin`；记录绝对路径和 SHA-256；宿主提供日志、任意自定义消息发送、位置附件组装事件。内置 HTTP 工具复用相似 Tab 生命周期。

**迁移约束**：

- 当前插件拥有主进程权限，哈希仅审计，不是签名校验或沙箱。
- `IPlugin.GetConfigurationPanel()` 返回 WPF `FrameworkElement`，现有二进制 UI 插件不能自动兼容其他桌面框架；应先拆出与 UI 无关的 Core 契约，再设计新 UI 扩展契约/版本协商。
- 不要声称 `Assembly.LoadFrom` 已支持可靠热卸载；关闭 Tab 主要是实例/UI/资源销毁。
- `TerminalSimulation.Plugins.XunjieCloud` 为本地内部插件，Git 已不跟踪，公开迁移不要求其源码；默认本地存在时可能参与构建，公开包必须排除。内部插件迁移另行确定授权与范围。
- 本地凭据服务使用当前 Windows 用户 DPAPI；无法跨系统直接解密迁移。不能恢复成明文密码，需重新登录或明确设计安全迁移。

**入口**：`TerminalSimulation.PluginBase/`、`Plugins/PluginManager.cs`、`MainViewModel.Plugin.cs`、`ViewModels/Plugins/`、`create_plugin.py`、`PLUGIN_DEVELOPMENT_GUIDE.md`、`docs/internal-plugins.md`。

### 3.12 主窗口、主题、设置与原生内容过渡

**功能**：连接区、系统日志/轨迹模拟/音视频/实用工具等页面；弹窗式设置（个性化、关于）；浅/深色、主题色、自定义背景、半透明/Blur/Acrylic、透明度；启动封面、版本简介、系统图标、响应式布局与 Tab 动效。

**技术特点**：WPF + MaterialDesign + MVVM；`TabTransitionAnimator` 淡入/水平位移/轻缩放，使用稳定基值、SnapshotAndReplace 和 FillBehavior.Stop，避免结束回抽。

**必须保留的特殊处理**：

- 地图的 WebView 原生窗口不能直接当普通 WPF 内容滑动。当前切换时隐藏真实 WebView，由遮罩参与动画，完成后同一渲染周期恢复；用版本计数防止快速切换的旧回调恢复错误页面。
- LibVLC 视频窗口存在 HWND airspace 问题，当前用 user32/gdi32 裁剪区域及尺寸/滚动更新处理。跨平台要按新合成器验证，不照搬 Win32 区域裁剪。
- 启动图片有显式圆角 Clip，不能只给外 Border 圆角就认为子图已裁剪。
- 系统图标包含可执行资源、窗口/任务栏等；当前使用 ICO/PE 资源与 AppUserModelID，其他平台要做相应打包资产与身份。
- 主 UI 尚无完整 i18n 适配；发布不生成无用语言目录，不能把迁移顺便假设为已国际化。

**入口**：`MainWindow.*`、`ThemeSettingsWindow.*`、`ThemeSettingsControl.*`、`AboutSettingsControl.*`、`SplashWindow.*`、`GlassBehavior.cs`、`Helpers/TabTransitionAnimator.cs`、`ResponsiveLayoutPolicy.cs`、`Shared/WindowsAppIdentity.cs`。

### 3.13 多源版本管理与非强制更新提示

**功能**：GitHub Releases、GitCode Releases、通用 HTTPS manifest；聚合版本及其多个来源；来源连通性/延迟/试读速度检测与推荐；由用户决定升级、同序列切换或允许的回退；设置图标下非模态更新气泡，“不再提示/查看详情”。

**技术约束**：

- `update-sources.json` 配置当前 schema 3；manifest/版本目录/安装计划使用 schema 2；一个 Release 多构建时用可识别的版本目录/资产绑定，不假设同名附件可重复。
- 同版本不同来源需 channel、line、epoch、哈希、大小、删除清单一致才合并；冲突来源不静默混用。
- 来源探测最多试读 256 KiB，超时约 8 秒；推荐源不是下载成功保证，完整下载仍校验 SHA-256。
- `line` 是版本线，`channel` 是 stable/preview/beta/test 序列，不能混为一个字段。非 stable 构建只显示相同序列；stable 需显式开关才显示测试序列。
- `compatibilityEpoch` 为不可逆兼容级别；高水位要持久保存，不能因主程序降级被绕过。
- Preview 编号/构建日期/数字发布修订号用于排序；同日随机构建后缀不冒充有序发行版本，避免反复提示。
- 气泡不是强制弹窗；不自动安装；同版一次运行不反复打扰；关闭提示不影响手动检查/管理。

**入口**：`Services/Updates/`、`MainViewModel.Update.cs`、`Views/UpdateAvailableWindow.*`、`Views/UpdateSourceSelectionWindow.*`、`docs/automatic-updates.md`、`docs/update-notification.md`。

### 3.14 Preview11 持久更新内核与运行时自适配

**核心要求**：应用可以降级，更新内核不能被旧应用包降级覆盖；安装过 Preview11 兼容层后可经新入口完成 Preview11 → Preview9 → Preview11，避免重新执行 Preview9 原更新器缺陷。

**实现结构**：根目录原生 `update.exe` + `.Updater/engines/{version}` 框架依赖 WPF 引擎 + 私有标准布局运行时 + 旧应用原生兼容宿主 + 持久状态/文件事务。三个根目录托管 `update.*` 文件继续满足旧主程序四文件检查，实际运行不依赖这些兼容文件。

**安装过程**：锁定安装目录 → 校验版本线/epoch → 等主程序退出/恢复中断事务 → 下载和 SHA-256 → ZIP 安全解压或安装 EXE 协作暂存 → 校验组件和运行时预检 → 备份/写前日志/原子替换 → 启动并验证目标主窗口 → 提交。失败/取消逆序恢复；恢复失败保留待恢复状态，不虚报成功。

**Preview11 安装包**：3.1.0 引擎加入原生 PE + ZIP 双格式单 EXE；手动运行 WPF 安装向导，更新调用只准备载荷。默认安装到 Program Files，并仅为当前账户的应用专属目录授予更新权限；保留已登记路径、自选专用目录、HKCU 卸载登记、COM 快捷方式与独立卸载工作进程。主程序和更新引擎共用随包私有运行时；更新引擎会在事务期间将运行时复制到临时目录；`.Installation` 记录与 `uninstall.exe` 受降级保护，卸载只删已知文件并保留数据/备份。安装包保留旧 ZIP 读取桥接，不修改历史发布包；详见 `docs/installer-preview11.md`。迁移时应改为目标系统原生安装/登记方案，不原样复用 Windows PE、自定义 bootstrap、registry 与 .lnk。

**运行时决策**：

- 自包含目标恢复包内 EXE/运行时，不混用旧运行库。
- 框架依赖目标优先预检 `.Updater/runtimes/` 的干净私有 Core/Desktop 缓存，缺失或不兼容才回退到系统运行时；不全局安装 .NET，不修改全局环境变量。
- 旧主程序 EXE 可由原生宿主替代，在原进程路径运行旧托管 DLL，保留 `Environment.ProcessPath`/`AppContext.BaseDirectory`，以后再次双击仍有效；不是只修首次启动。
- 高版本内核单独校验、暂存、自检、激活，保留 last-good；原生入口等待工作进程就绪，失败回退；历史包的普通覆盖/删除不能触碰持久内核。原生入口的前向替换也只走专门校验/事务路径。
- **内部引擎 3.1.0 不等于最低旧协议版本 3**。`minimumUpdaterVersion` 仍是 `2.0.0`，否则旧客户端看不到 Preview11。

**Windows 耦合**：WPF UI；registry/ProgramFiles 框架检测；win-x64 PE/DLL；COM STA 与 hostfxr；Win32 Job Object；以进程主窗口标题/可响应为就绪条件。通用事务/版本规则值得复用，但不能直接拿 Windows 私有 Desktop Runtime 在其他系统运行。

**UI 当前约定**：五阶段状态、单向平滑进度、安全取消/完成自动关闭、尊重系统动画开关；已移除营销式辅助小字；左上角中心箭头固定向上，仅外圈旋转；完成切换为绿色勾选。

**保障边界**：只支持受检的兼容 OS/架构/版本；原始旧更新器本身已坏且从未建立兼容层时需先完整安装一次。手动整目录覆盖/删除 `.Updater` 不在往返保障内。当前没有发行者数字签名体系，SHA-256 依赖可信来源。保留历史事务备份，目前不自动清除。

**入口**：`TerminalSimulation.Updater/`、`Updater.Native/`、`Scripts/Build-UpdaterBundle.ps1`、`Scripts/New-UpdatePackage.ps1`、`docs/preview11-update-architecture.md`。

## 4. 数据和路径兼容清单

当前偏向可写程序目录模式，资源目录与可执行目录由 `PathHelper.AppDir/ExeDir` 区分。**迁移到受保护应用目录/应用包时不能继续假设旁边可写**，应统一资源、用户配置、工作区、缓存、更新状态路径服务，并设计旧路径迁移。

| 路径/格式 | 内容及迁移要求 |
| --- | --- |
| `config.json` | 主程序、终端、外观、地图等配置；保留字段/默认值兼容与损坏恢复 |
| `utility_settings.json` | 项目文档约定的工具/插件设置；各工具具体存储以调用方为准，不假设统一数据库 |
| `Workspaces/Http/.Save/workspace.http-workspace.json` | HTTP 默认保存，FormatVersion 1；保留保存/导出区别及旧文件导入 |
| `Workspaces/Map/new/history.json`、`old/history.json` | 两套搜索和路线历史，独立迁移，不并库 |
| `Workspaces/Map/new/vehicle.json`、`old/vehicle.json` | 两套车辆配置，独立迁移 |
| `*.ctsroute` | 原始坐标系+点+模式+速度+车辆配置，不因扩展名非 JSON 而丢失结构兼容 |
| `update-sources.json` | 多源更新配置、通道相关选项；迁移后需加 OS/架构包选择设计 |
| `.Updater/` | 内核、运行时、兼容级别高水位、事务；不当作普通可删缓存 |
| `.Installation/install.json` | 安装身份、自选目录、快捷方式和文件所有权；不随旧应用 ZIP 覆盖，移植需转换为目标系统安装登记 |
| `Themes/` 及背景资源 | 自定义图像/主题路径；绝对路径需重定位策略 |
| `Plugins/*.Plugin` | 信任边界、依赖与新 SDK 兼容策略；不能无检查载入旧 WPF UI 插件 |
| `h264/` | 转码视频/音轨缓存，启动有受限路径清理；不误删用户原视频 |
| `thumbnails/` | 通道缩略图缓存；不得与用户保存路线/媒体混淆 |
| `Regions.json`、`MapApp*`、`ffmpeg-native/`、LibVLC 资源 | 随包只读资源/按平台原生库，不是用户可变配置 |
| `startup-crash.log`、更新计划旁日志、事务备份 | 故障诊断；路径、隐私与容量策略需明确 |

`AtomicJsonConfigStore<T>` 已提供串行保存、临时文件、原子替换、`.bak`、`.corrupt-*` 与临时文件恢复。不同文件系统上的替换/权限语义仍要验收，且这不是全应用跨进程数据库事务。

当前旧应用宿主使用的 `app-runtime.txt` / `app-hostfxr.txt` 记录选定运行时路径；跨平台或搬迁安装目录时应专门检查并重定位，不能假设所有保存值都已是可移动相对路径。

## 5. 建议的迁移切分（不是已完成重构）

```text
新桌面宿主 / UI
  ├─ 平台适配：窗口、WebView、播放器、文件选择、剪贴板、TTS、串口、凭据、更新启动
  └─ 应用服务：终端会话、消息调度、配置/历史、HTTP、地图检索、驾驶、插件生命周期
       └─ 通用核心：协议 DTO/编解码、帧处理、坐标、路线、车辆模型、格式化、版本规则
```

已有适配缝隙：`IConnectionSession`、`IAppLogger`、`IConfigStore<T>`、`ICredentialStore`、`ISerialPortService`、`ITtsService`、`ILocationSimulationService`、`IVideoPushSession`、`IInProcessMediaTranscoder`。它们现在大多还是 WPF 工程内部接口，并非独立跨平台程序集。

应新增/拆分的边界：UI 调度、用户路径、文件/剪贴板对话、地图 JS 桥、媒体预览、平台身份/主题、更新执行/运行时探测/启动就绪、插件 Core 与 UI 契约。

建议实施顺序：

1. 固定完整工作树和回归样例，确定目标 OS/架构与功能验收矩阵，不先大改 UI。
2. 提取 DTO 与纯逻辑，拆开 Core 测试；避免新 Core 引用 WPF ViewModels、Dispatcher、FrameworkElement。
3. 跑通终端连接、注册/鉴权、位置与自定义附件，先做到协议字节和行为等价。
4. 接入双地图 Web 宿主、坐标与历史；优先复用现有两套前端协议，不趁迁移合并存储。
5. 接入驾驶模型、HTTP/编辑器、串口/TTS、配置/凭据、插件新 SDK。
6. 分 OS/架构构建媒体桥与播放器，验证原生合成、推流时序及资源退出。
7. 最后完成平台安装包、系统图标、签名/权限/发布审查、多平台更新包选择与持久内核适配。

同一版本的“多镜像来源”与“多 OS/架构产物”不是一回事。当前更新包命名和绑定以 win-x64 为中心，跨平台发布必须先扩展产物选择规则；不得把 Linux/macOS ZIP 合并为同一个可互换下载源。

## 6. 回归与验收移交

| 范围 | 现有证据/入口 | 迁移后必须补充 |
| --- | --- | --- |
| 网络/协议 | `TerminalNetworkClientTests`、`Jt808BodyLengthTests`、`SimulatedPlatformIntegrationTests` | 粘拆包、异常帧、并发发送、取消、断线旧回调、实际平台互通 |
| JT1078 | `JT1078PusherTests`、原生 smoke test | 接收端播放/音轨、时戳、36 路资源与长稳测试，不用短测试代替 |
| 坐标/路线/驾驶 | `GeoCoordinateConverterTests`、`LocationSimulationServiceTests`、`VehicleSimulationTests`、`VehicleDrivingEventTests` | 坐标往返、直接切换、保持位置转换、急弯/终点/暂停/手动/事件确定性 |
| 地图 | `TencentMapServiceTests`、`OldMapServiceTests`、`MapHistoryStoreTests`；`Scripts/Test-MapUi.cjs new/old` | 新 WebView 的真实 SDK、IME、拖图、DPI、网络失败、权限/来源与 GPU |
| HTTP/配置 | `HttpRequestToolTests`、`AtomicJsonConfigStoreTests` | 10 MiB 边界、XML/JSON、编码、取消、多实例同路径冲突、只读目录 |
| UI | `WpfViewSmokeTests`、`ResponsiveLayoutPolicyTests`，下载/错误/成功 96/144/192 DPI 渲染 | 新框架真实窗口、小屏/缩放/多显示器、键盘焦点、减少动画、原生 Web/视频合成 |
| 更新 | `AutomaticUpdateTests`、`UpdaterBootstrapTests`、`UpdateDeploymentTests`、`UpdateKernelTests`、`UpdateNotificationTests`；`Scripts/Test-Preview11RoundTrip.ps1` | 每个目标 OS/架构的安装、锁占用、中断、启动失败、权限、兼容高水位与往返 |
| 凭据/插件 | `WindowsCredentialStoreTests`，部分内部测试按插件存在性启用 | 新 OS 安全存储、旧凭据不可解密处理、插件新 SDK 与释放 |

Windows 已有真实包往返：Preview11 → Preview9 → Preview11 → Preview8.2 → Preview9 → Preview11；覆盖系统/强制私有运行时路径、旧 EXE 再次独立启动、错误哈希、目标退出失败、坏候选内核拒绝激活与 last-good 恢复。**完全无 .NET 的干净 Windows VM、未来真实高版本内核发行包前向升级，以及所有非 Windows 验证仍未完成。**

前端离线测试使用模拟服务/SDK，不消耗真实腾讯 Key 配额；这不能证明线上 Key 权限、服务可用性或真实 SDK 跨宿主兼容。

## 7. 工程与安全约定

- 先读根 `AGENTS.md`：默认 `bin/Debug`，显式 Release 为 `bin/Release`；交付/打包到 `artifacts/{特征或版本}/`，发布到 `artifacts/publish/`。不主动提交/推送/发布。
- 公开构建显式 `-p:IncludeXunjieCloudPlugin=false`，使用干净输出目录，避免旧内部插件二进制残留。
- **不要读取展示、复制进文档或提交 `docs/KEY.txt`、真实 Key、生成的凭据中间源码、用户配置/密码或内部插件。** 迁移测试使用假 Key/离线服务；线上验证单独授权。
- 不能因迁移删除 `.Updater`、路线、配置、主题或工作区。资源/数据路径调整必须有可恢复迁移方案。
- 当前构建含 Windows PowerShell、MinGW 路径、Windows 原生资源、x64 LibVLC、FFmpeg 工具链及隔离运行时缓存生成；不是把 RID 改成 linux-x64/osx-arm64 就能完成构建。
- 当前不采用单文件发布；不要在迁移中隐式改变分发结构、插件加载或运行时资源定位。
- 不把 Windows DPAPI 密文当作跨平台可解密数据，不降级成明文保存；不把插件 SHA 审计当沙箱；不把客户端加密 Key 当服务端安全控制。

## 8. 下一会话的启动提示词

> 请先阅读 AGENTS.md、docs/cross-platform-migration-handoff.md 和相关功能源码。以当前完整工作树（包含未提交 Preview11 修改）为基线，不要 reset、删除工作区或丢失未跟踪源码。先与我确认目标 OS/CPU、UI 框架、是否保留 C#、旧插件/历史文件兼容范围、分发和更新方式，再给出分阶段迁移计划。必须保留双地图独立记录、轨迹/模拟/上报统一逻辑坐标系、HTTP 保存/导出语义、更新版本线/通道/兼容高水位和持久内核保护。不要访问真实 Key 或打包内部插件；先提取可测试业务核心，以现有回归和协议样例建立等价验收，不要仅根据 README 的旧宣传参数重写行为。

## 9. 深入阅读索引

- `README.md`：总览（其中视频固定帧率/码率等旧概括需结合当前代码核实）。
- `docs/map-ui-preview10.md`、`docs/vehicle-simulation.md`：地图/车辆行为与模型边界。
- `docs/automatic-updates.md`、`docs/preview11-update-architecture.md`、`docs/update-notification.md`：多源、持久内核、验证记录与提示策略。
- `docs/application-icons.md`：系统图标；`docs/internal-plugins.md`：公开/内部边界。
- `PLUGIN_DEVELOPMENT_GUIDE.md`、`TerminalSimulation.PluginBase/`：现有 WPF 插件契约。
- `FFmpeg.Native/README.md`、`native/terminal_ffmpeg.h` 及实现：原生媒体 ABI/构建/发布注意。
- `TerminalSimulation.Tests/`、`Scripts/Test-*`：回归与 QA 脚本；脚本未执行不等于当前验证已通过。

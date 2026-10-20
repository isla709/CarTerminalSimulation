# TerminalSimulation.Flutter 跨平台移植版

CTS 的 Flutter/Dart 移植正在实施，目标为 Windows、Linux、Android 和 HarmonyOS。
目录归属、Git 忽略与换行/内容过滤规则见 [仓库管理说明](docs/repository-layout.md)。
完整功能、平台状态和未完成项见 [实施记录](docs/migration-status.md)。
腾讯 GL 单地图/WebView、原生文件接口和依赖约束见 [地图接入说明](docs/map-integration.md)。
地图的手机/平板/桌面布局及验收矩阵见 [地图响应式设计](docs/map-responsive-design.md)。
地图操作面板已接入 Flutter 控件，灰底与表单对齐改造、当前贴边布局及剩余验收见 [Flutter 面板方案](docs/map-native-panel-plan.md)。
车辆模型、14 类交通情景及 C# 数值对照见 [驾驶移植说明](docs/driving-integration.md)。

## 本地地图 Key

构建前在**仓库根目录**的 `docs/KEY.txt` 放入构建者自己的腾讯地图 Key（UTF-8，单行；允许 BOM 和末尾换行）。该文件已被 Git 忽略，不提供真实 Key 示例。每次构建都会读取本机文件重新生成加密资源；没有环境变量覆盖、运行时输入或旧凭据回退。

请使用下面的 `Build.ps1` / `Build.sh` 作为构建入口。生成器仅在 `bin/{Debug|Release}/Flutter/{平台}/source/` 中产生 AES-256-GCM 加密资源，应用从包内读取；原始文本文件、Key 明文、dart-define 和生成的 Key 源码均不进入产物或版本库。生成输入在构建结束后清理，最终包保留加密资源。

谁构建就使用谁的本地 Key；该 Key 需具备腾讯 JavaScript API GL 与 WebService 所需权限。直接运行 `flutter build/run` 不执行本项目的凭据生成步骤；修改 Key 后须通过脚本重建，不能通过热重载切换。

客户端运行时必须把 Key 交给腾讯 SDK 和服务。此设计避免源码/构建日志携带明文，不承诺应用包内的凭据无法被逆向提取。详见 [构建凭据说明](docs/map-build-credentials.md)。

## 本地使用

```powershell
flutter pub get
flutter test
flutter analyze
./tool/Build.ps1 -Platform windows
./tool/Build.ps1 -Platform android -AndroidSdk D:\App\AndroidSDK
./tool/Build.ps1 -Platform ohos -DevecoHome 'D:\App\DevEco Studio'
# 本机鸿蒙 x86_64 模拟器
./tool/Build.ps1 -Platform ohos -OhosTargetPlatform ohos-x64
```

Linux 原生环境使用 `bash tool/Build.sh`（或 `bash tool/Build.sh Release`）；需预装 Flutter 的 GTK/CMake/Clang 构建依赖。本机尚未验证 Linux 构建。

默认 Debug，显式 Release 使用 `-Configuration Release`。所有构建在仓库 `bin/Debug/Flutter/{平台}/` 或 `bin/Release/Flutter/{平台}/` 下进行。脚本使用 ASCII 构建副本，避开 Flutter Windows 着色器工具处理中文路径的问题。项目源码目录名保持不变。构建配置和工具环境仅在脚本调用期间生效。

PowerShell 构建可用 `-OfflineDependencies` 复用已经完整下载的依赖缓存；缓存缺失仍会中止。构建统一先解析依赖，再以 `--no-pub` 编译，避免重复联网解析。

HarmonyOS 使用 CPF-Flutter 3.41.9+ohos-1.0.2；可通过 `-FlutterSdk` 指定路径。当前工具路径为仓库 `bin/Debug/toolchains/flutter-oh`。API 17 最低兼容，API 26 目标。默认输出未签名 HAP；提供好本机签名配置后才可使用 `-Codesign` 进行设备签名，不在仓库保存私钥或签名密码。

HarmonyOS 默认构建 ARM64；`-OhosTargetPlatform ohos-x64` 为本机模拟器生成独立的 `bin/Debug/Flutter/ohos-x64/source/build/ohos/hap/entry-default-unsigned.hap`。模拟器允许安装该未签名调试包，不能据此推断物理设备也允许。镜像和实例位于 `D:\App\HarmonyOS\images` 与 `D:\App\HarmonyOS\devices`，安卓 AVD 位于 `D:\App\AndroidAVD`。鸿蒙试用与网络排查见 [鸿蒙模拟器记录](docs/harmony-emulator-trial.md)。

真机：先构建 ARM64，再用 DevEco 打开生成的 `bin/Debug/Flutter/ohos/source/ohos` 工程完成自动签名。IDE 的 FlutterTask 已接入相同的 Key 加密注入与生成输入清理，支持直接构建运行。后续脚本重建会保留构建副本中的本机签名和应用包名，使用 `-Codesign` 输出签名 HAP；签名配置不会复制到公共源码。

## 协议对照

`tool/oracle/` 调用原 CTS 的 .NET 协议库，仅使用假数据生成 `test/fixtures/` 对照报文。生成方式：

```powershell
dotnet run --project tool/oracle/ProtocolOracle.csproj -- test/fixtures/jt808-csharp.json
```

目标仅覆盖已有实现，不能把对照报文测试描述为全部 JT808/JT1078 指令或实际车辆行为验证。

## 开发边界

- 保留原 WPF 项目；仅构建器按用户授权读取本机 `docs/KEY.txt`，不得输出 Key、复制到源码或提交；禁止访问内部插件和用户密码。
- 地图只采用原 `new` 的腾讯 JavaScript API GL 方案；移除地图选择器、旧框架及兼容服务分支。
- `.ctsroute` 保留原逻辑坐标系与车辆配置，地图显示转换不得改变保存/上报数据。
- 不包含 Web 客户端、动态插件系统、自动更新器和安装器。
- 手机/平板与桌面共同使用响应式布局；地图、媒体和原生材质必须经过各平台实际设备验证。

# HarmonyOS 模拟器试用

## 本机环境（2026-10-20）

| 项目 | 路径或配置 |
|---|---|
| DevEco / SDK | `D:\App\DevEco Studio`，API 26 / 26.0.0.105 |
| 模拟器 | 26.0.0.402 |
| 镜像 | HarmonyOS 7.0.0 / API 26，x86_64 |
| 镜像根目录 | `D:\App\HarmonyOS\images` |
| 实例根目录 | `D:\App\HarmonyOS\devices` |
| 实例 | `CTS_HarmonyOS_API26_phone`，1080×2400，DPI 420，4 GB RAM / 6 GB 磁盘 |
| HDC | `D:\App\DevEco Studio\sdk\default\openharmony\toolchains\hdc.exe` |
| HDC 目标 | `127.0.0.1:10001`，继续操作前重新查询连接 |
| 包 / Ability | `com.cts.terminal_simulation` / `EntryAbility` |

用户已明确授权接受镜像下载所需的 HarmonyOS SDK 与软件服务协议。镜像、实例均存放在 D 盘。DevEco 在用户 AppData 内仍保存少量配置和日志。

安卓 SDK 为 `D:\App\AndroidSDK`；原有 AVD 数据已迁到 `D:\App\AndroidAVD\Medium_Phone.avd`，用户目录的 `.ini` 索引引用这个绝对路径。安卓模拟器已经停止，本轮继续鸿蒙试用。

## 调试构建与安装

在仓库根目录运行 PowerShell 7：

```powershell
./TerminalSimulation.Flutter/tool/Build.ps1 -Platform ohos -OhosTargetPlatform ohos-x64
$hdc = 'D:\App\DevEco Studio\sdk\default\openharmony\toolchains\hdc.exe'
& $hdc list targets
& $hdc -t 127.0.0.1:10001 install -r 'bin/Debug/Flutter/ohos-x64/source/build/ohos/hap/entry-default-unsigned.hap'
& $hdc -t 127.0.0.1:10001 shell aa start -a EntryAbility -b com.cts.terminal_simulation
```

Debug HAP 在本机模拟器安装成功；真机签名和安装仍需独立处理。冷启动后应等界面稳定再读取层级或截图，过早截图可能只记录启动过渡帧。

## 网络与地图初始化

- 第一轮启动存在以太网有地址而 WLAN 未连接的状态。重启后原生设置显示 `VirtWifi` 已连接、WLAN 有 IPv4 地址，HarmonyOS 系统浏览器已实际载入腾讯地图 HTTPS 网站。
- 模拟器当前使用 `No proxy`。尝试过的手动主机代理连接检查失败，已撤回，不把主机代理配置当作已验证方案。
- 系统浏览器联网与应用内 WebView 加载分别记录。应用白屏不能仅按设备离线处理。
- ArkWeb 的 JavaScript 设置在创建原生 Web 组件时读取。宿主先配置 JavaScript、通信通道和导航代理，再挂载 Flutter WebView，最后发起页面请求。既避免首次创建时 JavaScript 禁用，也避免等待页面请求后才挂载视图的循环等待。
- 保留插件默认纹理视图路径；尝试过的混合合成路径未在当前环境中收到页面开始事件，不作为可用方案。
- 页面加入 40 秒加载超时、具体错误提示及标题栏重载入口。成功收到地图就绪消息后取消计时；晚到的成功消息可以清除本次超时提示。

### 当前地图试用阻点

本机 API 26 模拟器在应用内实际返回：

```text
WebGL context creation: disabled by enterprise policy or commandline switch
webgl: false
webgl2: false
experimental-webgl: false
```

诊断包已确认本地页面、注入配置、JavaScript 通道与 `window.ctsCanvas` 正常，腾讯 GL SDK 已下载且 `TMap` 为对象。直接构造地图时因 WebGL 上下文为 null 失败。因此当前环境的地图阻点是 WebGL 创建被禁用；不能据此声称所有鸿蒙模拟器版本或真机均不支持地图。没有修改系统安全策略或绕过模拟器限制。

最终代码在加载 SDK 前检测 WebGL，失败显示明确的运行环境提示，保留原生面板。所有平台继续使用同一个腾讯 GL 渲染器。模拟器中地图选点、2D / 3D 切换、路线绘制和车辆跟随尚不能完成验收；须在支持 WebGL 的鸿蒙设备继续。

## 2D / 3D 遮挡修复

Flutter 2D / 3D 开关放入地图工作台标题栏，不再覆盖地图视口。指南针、缩放和服务商版权继续使用腾讯 SDK 自带控件；标题文字在窄窗口下省略，视图按钮保持最小触控尺寸。

## 转到真机安装

当前原生工程最低要求 HarmonyOS 5.0.5 / API 17；ARM64 包与模拟器 x86_64 包分开生成。真机型号和系统版本需现场核对。

1. 手机开启开发者选项、USB 调试，通过数据线连接电脑，在手机上允许这台电脑调试。用上述 HDC 的 `list targets` 确认出现真机序列号。
2. 在 DevEco Studio 打开生成的 ARM64 工程：`D:\Project\VS\CarTerminalSimulation\bin\Debug\Flutter\ohos\source\ohos`。其 Flutter 资源和内嵌凭据已经由项目脚本准备，不要选择 `ohos-x64` 工程。
3. 在 `File > Project Structure > Project > Signing Configs` 勾选 `Automatically generate signature`，由使用者登录华为开发者账号，按 IDE 提示完成调试签名并应用。若界面显示 `Support HarmonyOS` 选项，应选择 HarmonyOS 签名。此处不需要为普通调试先注册应用市场应用。
4. 选择真机作为运行目标，运行 `entry`；也可以先用 `Build > Build Hap(s)/APP(s) > Build Hap(s)` 生成带签名 HAP，再通过 HDC 安装。
5. 检查首屏、地图工作台、原生缩放/指南针与标题栏开关；再继续绘制、搜索/规划、车辆设置与开始/停止。真机 WebGL 和实际 GPU 效果以现场运行结果为准。

自动签名流程见 [华为官方自动签名说明](https://developer.huawei.com/consumer/cn/doc/doccenter-deveco-studio/ide-signing-auto)。账号登录、认证和系统授权由使用者本人完成。签名资料留在本机，不写入 Git 工程。

## 真机试用（2026-10-20）

- 已连接 HUAWEI Mate 90 Pro Max（CMM-AL10），系统 7.0.0.109 / API 26，ARM64。
- DevEco 首次运行报 `9568423`，提示设备未在签名 Profile 中授权。切换回自动签名、应用当前设备的 Profile 后，以 `--no-incremental` 强制重建签名 HAP，HDC 安装及 Ability 启动成功。不会将设备 UDID、私钥、签名密码写入公共记录。
- DevEco 的 FlutterTask 会重新生成资源。项目脚本此前在结束时清理生成输入，导致直接点击 IDE 运行时包内缺少地图凭据。新增 `ohos/tool/map-credential-plugin.ts`：在 FlutterTask 前读取仓库未跟踪的 `docs/KEY.txt`，调用同一个加密生成器；任务结束/构建失败时清理临时输入。Key 不经命令参数、环境变量、源码或日志传递。
- Build.ps1 刷新构建副本时保留本机 DevEco 签名及包名，文件继续留在 Git 忽略的 `bin` 内；版本号等公共应用字段继续从源码同步。后续已签名构建可用 `-Platform ohos -Codesign`。
- 真机已显示真实腾讯地图、指南针、比例尺与服务商归属，页面可见搜索结果；2D / 3D 开关位于标题栏，与 SDK 指南针分开。当前真机包使用本机构建者在 DevEco 配置的应用标识，公共源码默认应用标识不变。
- 本轮实际运行截图为仓库 `bin/Debug/flutter-ui/harmony-device-map.jpeg`，仅作本地联调证据，不打包进应用。未完整验证路线规划、车辆跟随、文件保存恢复、后台恢复或长稳。
- 小地图高度（展开面板并弹出键盘）下，左侧工具条仍需要进一步自适应，防止挤压比例尺与版权区域；此次标题栏开关修复不等于完整响应式矩阵验收。
- 最终 `Build.ps1 -Platform ohos -Codesign -OfflineDependencies` 构建成功，输出为 `bin/Debug/Flutter/ohos/source/build/ohos/hap/entry-default-signed.hap`。核对本机包名和签名配置均被保留；157 个公开源码文件、三份本轮日志及签名 HAP 未发现 Key 明文，HAP 内含一个加密凭据与四个地图资源，生成输入已清理。日志为 `bin/Debug/flutter-harmony-signed-final.log` 与 `bin/Debug/harmony-device-package-audit.log`。没有为同步最后一次构建而重启用户正在试用的手机应用。

## 证据边界

本轮最终 Windows / Android / HarmonyOS ARM64 / HarmonyOS x86_64 Debug 构建成功。日志分别为仓库 `bin/Debug/flutter-map-final-{windows|android|ohos}.log` 与 `bin/Debug/flutter-harmony-final-x64.log`，Dart 静态分析无问题。156 个公开源码文件、四份最终构建日志、APK / 两份 HAP 和 Windows Flutter 资源扫描未发现本机 Key 明文；原始 Key 未打包，每份包含一个加密凭据资源，生成输入已清理。检查日志为 `bin/Debug/flutter-map-final-key-audit.log`。

最终 x86_64 包已在模拟器安装启动。原生面板可展开、滚动；速度输入 `[42,1636][524,1794]` 与坐标系选择 `[555,1636][1038,1794]` 上下对齐，截图为仓库 `bin/Debug/flutter-ui/harmony-native-fields.jpeg`。WebGL 能力提示实图为 `bin/Debug/flutter-ui/harmony-final-webgl-message.jpeg`。这些截图没有地图瓦片，不作为地图渲染成功的证据。

驾驶设置已实际打开，车辆预设、限速/巡航、风格、传动和动力字段可见，底部取消/保存按钮常驻，截图为 `bin/Debug/flutter-ui/harmony-native-vehicle.jpeg`。尚未据此宣称车辆模拟、全部字段保存恢复或完整键盘矩阵通过。

本轮为本机 x86_64 手机模拟器短时试用。不能代替 ARM64 手机、平板、2in1、API 17 兼容、完整尺寸/字体矩阵、后台恢复、长时间 GPU 稳定性或鸿蒙原生沉浸光感验收。没有连接真实 JT808 / JT1078 平台。本轮没有新增或运行自动化测试。

默认不开启 WebView 远程调试。仅在 Debug 构建显式添加 `-DebugMapWebView` 时启用，用于本机诊断；不适用于 Release。诊断不输出 Key、SDK 完整请求地址或凭据内容。

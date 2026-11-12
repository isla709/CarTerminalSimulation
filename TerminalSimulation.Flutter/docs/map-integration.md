# 腾讯 GL 单地图宿主

## 当前方案

- 跨平台版本统一使用原 CTS `MapApp`（原 new）的腾讯 JavaScript API GL。资源位于 `assets/map/`。旧框架、Leaflet 资源、地图切换器、`mapProvider` 字段及 Photon/OSRM 服务已删除，原 WPF 工程保持原状。
- 搜索、联想、驾车候选路线和粗略 IP 定位统一使用腾讯服务。请求通道独立取消，过期结果不能回写页面；定位失败允许继续手动选点，不切换其他地图框架。
- 腾讯服务坐标按 GCJ-02 处理；IP 定位按既有桥接契约转换为 WGS-84。显示转换不改变 `.ctsroute` 原始逻辑坐标系、驾驶积分和 JT808 汇报数值。
- 历史与配置统一保存为 `map-history.json`、`map-settings.json`、`map-vehicle.json`。历史保留 50 条搜索/30 条轨迹，支持去重、恢复、重命名、删除和清空。不同地图文档打开同一存储；页面回复使用摘要，避免把全部路线点用于列表渲染。
- 不导入 WPF 双地图设置；此前开发快照产生的分支命名配置文件不参与当前运行，也不删除用户数据。需要保留的路线仍可通过 `.ctsroute` 显式导入。
- 地图 Key 由构建者本机未跟踪的 `docs/KEY.txt` 在构建时注入，同一个固定 Key 用于 JavaScript API GL 与 WebService。运行时没有 Key 输入/覆盖入口，不写入应用普通设置；原始文件和明文均不打包。四平台统一使用加密资产，详见 [构建凭据说明](map-build-credentials.md)。真实服务授权和配额仍需联调。

## 响应式界面

详细布局、设备比例及验收项见 [地图响应式设计](map-responsive-design.md)。

2026-10-20 已接入 Flutter 原生操作面板和 Dart 地图协调器，WebView 仅渲染腾讯 GL；详见 [Flutter 面板方案](map-native-panel-plan.md)。当前采用上下/左右无缝衔接布局，原 HTML 面板、表单与 CSS 灰底外壳已移除。搜索、模式、规划、速度/坐标系、文件/历史、车辆/事件表单与模拟控制在 Flutter 中完成。

地图生产桥只接收带版本的 ready、point、view、脱敏 error 事件，入口上限 8192 个 UTF-16 单元；不再从网页触发文件/历史/模拟操作。完整路线只在几何版本变化时发送，遥测更新仅发送车辆和视图状态。旧 `MapSession` 不再被生产工作台导入。

以下段落属于旧 HTML 版布局记录，不代表新面板实现：

- 竖屏手机、窄窗口和竖屏小平板：底部控制面板，默认至多占可用高度的 55%；顶部保留地图区域。收起只隐藏编辑区，模拟开始/停止按钮保留。
- 横屏、平板分屏和桌面：侧栏，低高度使用紧凑头部。地图容器为侧栏预留实际空间，路线拟合边距随地图大小变化。
- 按腾讯 GL 的 `resize` 事件在非模拟、非软键盘状态下重新拟合已有路线，保持调整窗口后的路线可见性；接口依据 [腾讯 Map 官方参考](https://lbs.qq.com/webApi/javascriptGL/glDoc/docIndexMap)。
- 安全区、动态视口、键盘覆盖通过 CSS 和 VisualViewport 适配；联想列表随编辑区滚动。弹窗正文可滚动，页脚动作固定。
- 手机驾驶摘要可展开为完整控制面板，避免手动踏板和挡位挤在地图角落。应用主题、局部玻璃、实色回退及减少动画设置同步给地图。
- HTML 玻璃是 WebView 材质表现；鸿蒙原生沉浸光感仍按独立原生适配计划推进，不能将 CSS 模糊视为鸿蒙原生材质验收。

## 四平台宿主与文件接口

- Windows/Linux 使用 `webview_cef 0.6.2`；Android/HarmonyOS 使用 CPF `webview_flutter 4.13.0`，固定提交 `5a9d0055772c7973712c0eaaf2de0e5f0749f8b9`。依赖的其他平台元数据不代表本项目提供相应客户端。
- 工作台按需创建 WebView。退出时关闭页面请求和资源宿主；AppModel 保留终端连接及模拟。重新进入恢复路线和运行控件状态，重载使旧页面请求失效。
- 桌面使用 `file_selector 1.1.0`；Android SAF、HarmonyOS DocumentViewPicker 经 `cts/platform` 调用，读写分块并限制 25 MiB；路线解析/序列化在 isolate 内执行。

## 资源与桥接

每个地图实例绑定随机端口的 `127.0.0.1` 资源宿主，只提供打包资源白名单，只接受 GET/HEAD。URL 和消息桥使用随机 token。此宿主不提供浏览器客户端、远程服务、TCP 代理或任意本地文件读取。

Android 仅为 `127.0.0.1` 放行明文资源访问，远端请求使用 HTTPS。资源设置 no-store、no-referrer、nosniff 和 CSP。移动端阻止主页面跳离资源宿主；CEF 页面跳走时禁用桥并返回空白页。CEF 缺少公开的同步导航拦截 API，真实 SDK 的来源隔离与 CSP 兼容仍需进一步联调。

`host.js` 适配移动端 `CtsMap.postMessage` 和 CEF 函数式通道；CEF 消息返回 ACK 释放回调。Dart 回复函数名固定、参数 JSON 编码；错误日志不输出含 Key 的 URL。

## 构建与证据边界

- 普通构建写入仓库 `bin/Debug/Flutter/{平台}/`；CEF 运行依赖完整目录，不能只复制 EXE。Windows 原生工程使用 UTF-8。
- Android/HarmonyOS 使用仓库同盘依赖缓存，规避 Hvigor 模块路径和 Kotlin 跨盘缓存问题；Git 长路径配置仅在构建子进程生效。
- 2026-10-20，Flutter 原生面板最终源码完成 Windows EXE、Android APK、HarmonyOS 未签名 HAP 的 Debug 重建，日志为仓库 `bin/Debug/flutter-native-map-{windows|android|ohos}.log`。Dart 静态分析、两个 JS 及 PowerShell/Bash 语法检查通过；本轮未新增或运行自动化测试。
- APK/HAP 和 Windows Flutter 资源均只包含 `assets/map/` 的四个文件：index.html、canvas.js、host.js、host.css，已删除的 HTML 操作界面不残留于构建包；三个构建副本的九项运行源码/资源，共 27 项 SHA-256 比较一致。
- API 37 安卓可见模拟器已更新最终 APK；真实腾讯底图与归属可见，Flutter 面板没有外围灰底，速度/坐标系上下边界一致，驾驶设置为原生可滚动弹窗。截图及边界见 [实施记录](map-native-panel-plan.md#实施记录2026-10-20)。这些证据不等于四平台或全部功能验收。
- 仍待：腾讯搜索/规划权限和配额全流程、手机/平板中文输入与各比例视觉及无障碍、文件权限/取消、CEF 生命周期、长路线资源占用、真实 GPU 帧率。Linux 尚无本机构建环境；HarmonyOS HAP 未签名、未真机验收。

## Windows 地图黑屏修复（2026-10-20）

- 复现现象：Flutter 面板与地图消息桥正常，底图全黑；标准错误持续出现 `Could not create external texture->`。CEF 已输出 BGRA GPU 帧，因此继续追查共享纹理描述符的生命周期。
- `webview_cef 0.6.2` 的 GPU 回调返回堆内描述符，在 `release_callback` 中连同纹理引用一并删除；Flutter 3.38.5 的 `ExternalTextureD3d::PopulateTexture` 在执行该回调后仍读取 `visible_width` / `visible_height`，形成释放后读取。实现顺序可核对 [Flutter 对应版本源码](https://github.com/flutter/flutter/blob/3.38.5/engine/src/flutter/shell/platform/windows/external_texture_d3d.cc)。
- Windows CMake 在构建树生成修补后的插件源文件：返回线程局部的描述符副本，纹理引用仍按原回调释放，保留 GPU 共享纹理路径。公共 Pub 缓存不变；依赖升级后若补丁定位失效，构建会中止并提示复核。
- 本机 Windows Debug 重建后实际底图、SDK 归属、2D / 3D 切换、拖动、原生缩放和最大化重绘恢复；修复版标准错误为空。记录为本机手动试用，不代表其他显卡、Linux 或长时间运行验收；本轮未新增或运行自动化测试。构建日志：`bin/Debug/windows-map-build.log`。

## 鸿蒙 WebView 初始化与模拟器边界（2026-10-20）

- 公共宿主先配置 JavaScript、通道和导航代理，再挂载 WebView，最后请求本地页面。ArkWeb 首次创建须读到已启用 JavaScript 的设置；挂载不能放在等待页面请求完成之后。
- OHOS 构建使用 `ohos/flutter/map_webview.dart.template` 作为平台适配，默认保留插件纹理路径。只有显式 Debug 开关才能启用远程调试，常规调试包与 Release 均默认关闭。
- 地图脚本检测 WebGL；不可用时给出单独提示，不与网络/授权错误混淆。页面加载保留超时及重试；底部面板和业务状态仍由 Flutter / Dart 管理。
- 2D / 3D 应用开关已经移入标题栏，地图 SDK 指南针和缩放区不再叠放应用视图按钮。
- 本机 API 26 模拟器已联网且 SDK 下载成功，但 WebGL 上下文创建返回策略/启动参数禁用。地图绘制与车辆跟随未通过鸿蒙运行验收；详见 [鸿蒙模拟器试用](harmony-emulator-trial.md)。

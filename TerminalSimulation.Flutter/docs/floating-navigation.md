# 悬浮导航与 HarmonyOS 沉浸光感

更新于 2026-11-02。

## 本轮修正及已确认原因

之前的底栏使用 `initExpensiveOhosView`。当前 CPF Flutter 引擎的 `PlatformViewsController.configureForHybridComposition` 仅输出日志，并没有把视图加入显示树，造成创建成功但底栏不可见。之前的编译通过不能作为原生视觉验收。现已移除该原生导航 PlatformView 和适配模板，地图的既有纹理合成路径不改动。

顶部之前是 Column 中单独占位的固定行，内容无法滚到其下方，背景模糊没有实际内容可采样。现改为 Stack 上方的定位叠层；滚动内容仅在列表起始处预留顶部空间，滚动后内容可经过悬浮栏下方。

原生栏显示后不能点击的原因是容器使用 `HitTestMode.Block`，此模式同时阻止子节点命中。顶栏与底栏现使用 API 20 起提供的 `BLOCK_HIERARCHY`，允许自身及子节点响应，并阻止手势穿透；全屏宿主保留 `Transparent`，让栏外的 Flutter 内容正常操作。底栏通过 `onTabBarClick` 立即切换，`onChange` 共用带索引、可见性和重复选择校验的入口。

原生布局就绪通知可能早于 `configure` 的返回值。返回值中的旧 `ready: false` 现在不会覆盖已收到的就绪通知，避免返回主页面时同时显示原生栏和备用栏。

模拟器进一步显示，当前自定义标题结构只设置 `NavigationTitleOptions.systemMaterial` 时，顶栏仍为透明文字和图标，没有形成玻璃底板。现将材质直接设置到 TitleBar 内自定义标题 Row 的 `systemMaterial`，由这一层承载统一圆角及系统材质。外层 Navigation 只负责标题栏作用域与布局。

2026-11-02：关闭外层标题栏默认材质，关闭内层材质自带阴影，只在胶囊上设置一层柔和阴影；外层预留四周 32 vp，消除矩形阴影条。Flutter 桥接监听当前路由和次级路由动画状态，仅在转场结束且主页面为当前页时采样坐标，结束时主动请求一帧重新同步，避免将转场中的偏移固定到原生栏。

返回时的材质切换修正：将“当前可见/就绪”和“原生层拥有导航栏”分开。首次原生布局成功后，进入子页面、弹窗或后台只临时隐藏原生栏，不将导航交还给 Flutter；原生 Navigation/Tabs 使用 Hidden 保留实例和已确认的布局。返回转场结束后恢复可见性，无需重新创建材质，也不会先显示 Flutter 导航栏。关闭玻璃、通道失败或布局超时才回退 Flutter；屏幕尺寸/安全区改变时重新确认坐标，不复用旧尺寸。

主题菜单使用 Material 3 DropdownMenu 的页内覆盖层，替换会推入 PopupRoute 的 DropdownButtonFormField。展开主题列表不再触发原生导航的路由隐藏规则；禁用搜索和点击唤起输入焦点，保持纯选择行为。菜单由主壳层持有 MenuController，点击任意导航页签时主动关闭，避免菜单跟随到其他页面。二级工作台的隐藏与返回交接规则保持原有逻辑。

## 布局与几何

- 主页面小于 600 使用底部胶囊导航，600–1023 使用侧边图标导航，1024 及以上显示侧边图标和名称。底栏最大宽度 560、安全区内间距 12。
- 顶部状态栏和导航共用 StudioGeometry：外圆角 28、内边距 6、内圆角 22。顶栏高度随字号变化；导航在小宽度/大字号下保留图标、提示及无障碍名称。
- 主页面保持同一 IndexedStack / Positioned 层级，不因尺寸改变搬移输入控件。PageBody 在列表开头/末尾预留顶栏/底栏的空间，日志工作区单独避让；键盘出现时底栏隐藏并释放空间。
- 关闭玻璃或启用高对比度时回退 Flutter 实色栏；减少动态效果时关闭选中态过渡与原生光感/交互形变。

## 鸿蒙原生宿主层

- Index.ets 使用同一 Stack 放置 FlutterPage 与 CtsChromeHost，原生栏是 Flutter 渲染表面的兄弟节点，绕开未实现的混合合成及纹理快照采样边界。
- API 26 顶栏采用 Navigation 标题栏内自定义 Row 的 systemMaterial；底栏采用 Tabs 的 barFloatingStyle.systemMaterial。均为 THIN 的 ImmersiveMaterial；WithTheme 跟随应用主题，实际材质强度遵循系统和设备能力，不强制高算力档位。材质不可用时保留实色底板。
- Flutter 通过 cts/chrome 通道传递原生栏对应 RenderBox 的实际坐标、尺寸、主题、连接状态及字号。首次 Native Chrome 布局就绪后接管导航；临时隐藏期间保留所有权，初始化超时/通道失败恢复 Flutter 导航。
- 原生底栏有明确的栏宽和额外阴影空间，避免把浮动 Tabs 紧塞进只有栏高的视口；不使用全屏 PlatformView 截获内容区域手势。
- ModalRoute 当前性、键盘、生命周期与宿主状态共同控制可见性。进入子页面/弹窗或退到后台时隐藏原生栏，恢复主页面后重新布局同步；每个桥接实例有 owner 与 generation，忽略旧回执，释放时解除通道监听。
- API 17–25 不创建原生材质宿主；Windows/Linux/Android 使用 Flutter 悬浮栏。大屏侧栏仍为 Flutter 局部材质。Build.ps1 仅在 OHOS 构建副本替换 harmony_chrome.dart，并移除旧导航缓存文件，不修改公共 SDK/Pub 缓存。

## 证据边界

- 主题菜单修正：API 26 模拟器已确认展开时顶部和底部原生栏均保持显示，能够选择菜单项；菜单展开时点击“工具”，主题菜单正确关闭，无残留。证据为 bin/Debug/theme-menu-expanded.png、theme-menu-expanded.json、theme-menu-switch.json。Dart 静态分析、OHOS x86_64/ARM64 Debug 构建通过，日志为 bin/Debug/theme-menu-{x64,arm64}-build.log。未运行自动化测试，未验证真机。

- 导航交接修正后，API 26 模拟器已检查 HTTP 子页返回；返回后即时截图与延后截图均直接显示原生栏，未见 Flutter 栏接管，证据为 bin/Debug/handoff-{early,settled}-final.png。此前两次返回布局中的 CTS、连接状态及底栏文字坐标一致。即时截屏不是完整逐帧录像；本轮未验证真机或所有系统手势。最终 x86_64 与 ARM64 Debug 构建日志为 bin/Debug/chrome-handoff-{x64,arm64}-final-build.log。

- 2026-11-02：API 26 模拟器连续三次进入 HTTP 子页面并返回，CTS、连接状态及底部导航文字 bounds 均与进入前一致。证据为 bin/Debug/nav-return-{base,1,2,3}.json；顶部单层阴影截图为 bin/Debug/nav-video-start.png。本轮 OHOS x86_64、ARM64 和 Android Debug 构建通过，日志为 bin/Debug/chrome-return-video-*-build.log。未运行自动化测试。

- Dart 静态分析通过，Windows Debug 和 API 26 ARM64 未签名 HAP 构建通过。输出均在 bin/Debug/Flutter/{平台}/source/build/。
- Windows 实际查看顶部固定叠层与内容从下方滚过，未见标准错误；不以 Windows 效果代表鸿蒙原生材质已通过验收。
- 使用现有 D 盘 API 26 x86_64 模拟器安装 Debug HAP，并通过 HDC UI 输入与布局快照检查五个页签切换、重复选中、连接配置展开、HTTP 子页面入口、下拉菜单、返回恢复及栏外滚动。最终五个页签的内容均与入口匹配，顶栏状态只显示一次，原生栏之外的 Flutter 控件可以响应。
- 未建立平台连接或发送 HTTP 请求。检查期间曾触发默认本机回环连接，连接被拒绝后已取消重连。输入框可以获取焦点，本轮不以此认定软键盘与输入法已完成验证。
- 模拟器证据位于 bin/Debug/chrome-click-*.json、chrome-click-*.png；构建日志为 bin/Debug/chrome-click-ohos-x64-build.log 与 chrome-click-ohos-arm64-build.log。未新增测试文件。
- 顶栏材质修正后，API 26 模拟器截图已显示完整胶囊玻璃底板、高光边缘、阴影和滚动内容的模糊；进入 HTTP 子页面时原生栏隐藏，返回后 CTS 与连接状态各只显示一次。截图及布局位于 bin/Debug/chrome-header-{final,scroll-final,subpage,return}.*，x86_64 与 ARM64 Debug 均构建通过，日志为 bin/Debug/chrome-header-ohos-{x64,arm64}-build.log。
- 本轮不代表真机材质、所有主题/字号/输入法或长时间稳定性均已验证。真机仍需签名安装；本轮未对已连接真机进行安装或操作。

## 依据

- [官方沉浸光感适用范围](https://developer.huawei.com/consumer/cn/doc/harmonyos-guides/arkts-immersive-light-sense-overview)。
- 本机 API 26 的 navigation.d.ts、tabs.d.ts、with_theme.d.ts、@ohos.arkui.uiMaterial.d.ts；当前 Flutter OH 引擎 PlatformViewsController.ets 的实际实现。

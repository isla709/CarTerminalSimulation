# 应用与更新器图标

主程序使用“车辆 + 信号”，独立更新器使用“循环箭头 + 升级箭头”。两者统一采用蓝色圆角底板、白色符号和透明外缘。

## 资源与重新生成

- 主程序源图：`TerminalSimulation.Wpf/Assets/app-icon.svg`。
- 更新器源图：`TerminalSimulation.Updater/Assets/update-icon.svg`。
- 编辑 SVG 后，在项目根目录执行：

```powershell
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File Scripts/New-AppIcons.ps1
```

脚本生成 ICO 和 512px PNG，无需安装额外图片工具。ICO 内含 16、20、24、32、40、48、64、96、128、256px 的 32 位透明图标，适配 Windows 常见 DPI。SVG 使用脚本支持的 rect/path/circle 图形。

## Windows 集成

- 两个项目的 `ApplicationIcon` 将图标编入 Win32 EXE 资源，供资源管理器、快捷方式、任务管理器和系统默认窗口图标读取。
- WPF 所有自有窗口设置 `Window.Icon`，覆盖标题栏、任务栏、Alt+Tab 等窗口入口；标题栏和关于页面使用同一套标识。
- 主程序及更新器在启动时分别设置稳定的 AppUserModelID，避免升级版本号和更新器临时运行目录影响任务栏分组。
- 图标同时作为 WPF Resource 内嵌，不依赖 EXE 旁边的独立图片文件，更新器临时副本仍可正常读取。
- 当前没有系统托盘功能；图标接入不会额外新增托盘行为。Windows 文件关联（如路线文件图标）和安装器快捷方式应在后续实现相应注册功能时单独配置。

如已有固定到任务栏的旧快捷方式保留旧图标，可取消固定并重新固定。资源管理器的图标缓存可能不会立即刷新；无需修改注册表或删除系统缓存。

## Flutter 跨平台版

Flutter 版沿用车辆与信号的识别图形，使用翡翠青绿渐变（`#20C9A6` → `#087E8B`）；WPF 版继续使用原蓝色图标。版本通过颜色区分，图标中不叠加小字号框架名称。

源文件位于 `TerminalSimulation.Flutter/assets/brand/`：

- `foreground.svg`：透明的白色车辆与信号，车窗和车灯为镂空，适合分层合成。
- `background.svg`：铺满画布的直角背景，无外圈透明留白。
- `app-icon.svg`：由上述两层生成的完整直角图标。
- `app-icon-store-1024.png`、`app-icon-store-216.png`：应用市场上传图，与包内图标使用相同图形、色彩及比例。
- `app-icon.png`：应用内及 Linux 窗口使用的 512px 圆角版本。

重新生成全部平台资源及上传文件：

```powershell
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File TerminalSimulation.Flutter/tool/New-AppIcons.ps1 -Export
```

不传 `-Export` 时只更新源码资源；传入时同时输出 `artifacts/flutter-branding/`。不依赖在线图片服务，也不会修改 WPF 图标。

平台接入：

- HarmonyOS：AppScope 与入口 Ability 均引用 `cts_layered_icon.json`。前景与背景分别为 1024×1024 PNG，背景完全不透明，无预裁圆角，系统生成场景遮罩；启动页单独引用 256×256 透明前景 `cts_start_icon.png`，减少冷启动解码开销。
- Android：保留五档密度的传统图标；API 26 及以上采用前景、背景自适应图标，由启动器生成外形。前景按 108dp 画布安全区缩放，避免圆形遮罩裁切车辆与信号。
- Windows：EXE 的 ICO 包含 16、20、24、32、40、48、64、96、128、256px，共十档图像；标题栏、任务栏与应用内标识统一。
- Linux：从可执行文件旁的 Flutter 资源读取图标，不依赖启动时的工作目录。Wayland 任务栏的应用匹配还依赖桌面环境和后续 `.desktop` 安装集成。

规范依据：

- [华为应用市场素材规范](https://developer.huawei.com/consumer/en/doc/agc-help-app-visual-asset-spec-0000002277607976)：手机、平板与 PC 应用上传正方形 216×216 或 1024×1024 PNG，最大 3MB。
- 本机 DevEco Studio 的 `plugins/codelinter/arkPerfCheck/docs/private-layered-image-insight-policy-cn.md` 及 `plugins/harmony/lib/templates/ability/common/media/layered_image.json`：使用 `layered-image` 描述前景和背景，应用配置引用该资源。

图标资源准备与编译成功不代表已通过应用市场审核；最终桌面遮罩、光效应在真机上查看。

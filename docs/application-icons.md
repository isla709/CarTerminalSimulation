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

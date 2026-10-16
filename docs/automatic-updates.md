# 自动更新发布规范

主程序负责检查更新与展示更新说明；安装目录中的独立 `update.exe` 负责下载、SHA-256 校验、等待主程序退出、备份替换、失败回滚和重新启动。主程序不在自身进程中覆盖文件。

升级系统的强制兼容性、运行时和发布门禁见[升级系统兼容性与发布规则](update-system-compatibility-rules.md)。

Preview11 起，根目录 `update.exe` 是不依赖 .NET 的原生兼容入口；实际 WPF 更新引擎、独立运行时及旧版启动适配器保存在受保护的 `.Updater` 中。入口按 `current.txt` 选择引擎，工作进程确认就绪失败时尝试 `last-good.txt`。降级应用不会降级这套内核。完整布局、兼容边界与测试方法见 [Preview11 更新架构](preview11-update-architecture.md)。

引擎按自身 `update.deps.json` 将完整依赖复制到独立临时目录后运行，不再借用主程序目录中的运行库。引擎使用 `System.Text.Json 9`，清单同时包含其传递依赖。根目录仍保留三个 `update.*` 托管兼容文件，供旧主程序的四文件存在性检查通过，但实际执行由原生入口负责。启动器阶段异常写入更新计划旁的 `update-bootstrap.log`。

## 自包含与框架依赖版本之间切换

Preview9 / Preview10 包携带运行时，Preview7 / Preview8 的更新器和主程序则依赖系统 .NET。向旧版切换不能只覆盖 ZIP 中的文件：遗留在安装目录中的 `hostfxr.dll` 等文件会使框架查找落在错误的目录，即使系统已经安装 .NET 8 也可能出现缺少框架提示。

目标包为框架依赖模式时，更新器读取旧依赖清单，清理目标没有提供的旧运行时文件，并修复残留 host。优先预检兼容的系统运行时，否则使用私有缓存。原主程序 EXE 由带原应用图标的原生兼容宿主替代，托管 DLL 仍保持旧版内容，之后再次双击也使用选定的运行时。不会向系统安装 .NET 或修改全局环境变量。自包含目标恢复使用包内原 EXE 和运行时。

Preview11 的备份和写前日志保存在 `.Updater/transactions/{id}/backup/`。成功启动后才提交；替换、取消或启动失败时逆序恢复，进程异常结束留下的事务由下次更新恢复。只有更新器记录拥有的旧文件及依赖清单识别的旧运行时会自动清理；不会目录级清空安装目录。

若原始 Preview9 更新器本身已经无法启动，需要先使用完整 Preview11 包建立一次兼容层，再迁移配置和工作区。不能追溯修复从未安装该兼容层的旧版本，也不覆盖手动解压旧 ZIP 并删除 `.Updater` 的操作。

更新窗口为状态文字预留独立空间，长错误信息支持滚动查看；版本号可换行，按钮与进度条不占用文字区。回归渲染覆盖 100%、150%、200% DPI。

## 更新源配置

首次启动会在程序目录创建 `update-sources.json`。仓库中的 `update-sources.example.json` 是完整示例。更新源按 `priority` 从高到低检查，并汇总当前版本线内的全部兼容版本供用户选择。自动检查更新“关于”页状态；Preview10 起，发现更高版本时还可在设置图标下方显示非模态气泡，不弹出安装窗口，也不会自动安装。

气泡提供“不再提示”和“查看详情”。不再提示会持久化为 `config.json.ShowUpdateNotifications=false`，可在“设置 → 关于”重新开启；关闭提示不影响检查更新或手动版本管理。同一版本在一次运行中最多自动提示一次，后台 / 最小化时延迟至窗口激活后提示。详见 [新版本提示气泡](update-notification.md)。

- `github`：读取指定仓库的 Releases。公开仓库不需要令牌。
- `gitcode`：读取 GitCode 公开仓库的 Releases，使用 `/api/v5/repos/{owner}/{repo}/releases`，公开发布不需要登录。
- `manifest`：直接读取任意 HTTPS 托管平台、对象存储或 CDN 上的清单。
- `channel`：清单通道建议使用 `stable`、`preview`、`beta` 或 `test`。Preview/Beta/Test 程序只在同一测试序列内显示版本；正式版默认只显示 stable。
- 正式版可在“设置 → 关于 → 测试版本通道”开启测试版本开关，开启后才会显示 Preview、Beta、Test 等预发布序列。该开关不改变版本线和兼容级别限制。
- `line`：独立于版本号和 channel 的版本线标识，默认 `main`。运行中的程序只接受与编译时版本线相同的包，修改配置不能绕过此限制。
- `allowInsecureHttp`：默认必须为 `false`。只有明确受信任的内网镜像才可打开。

## GitHub Release 特别格式

Preview11 首发使用单独安装 EXE。后续正式 Release 固定同时发布安装 EXE 和便携 ZIP；根级 `package` 继续指向 ZIP 以兼容旧升级器，新客户端按安装记录自动优先选择 EXE 或 ZIP。详见[安装与双格式更新](installer-preview11.md)。

每个可升级的 Release 必须同时上传：

1. 名为 `update-manifest.json` 的清单资产。
2. 清单 `package.fileName` 指定的 ZIP，以及 `packages` 数组列出的安装 EXE。

GitHub 源不信任清单中自带的下载地址，而是按文件名匹配同一个 Release 的资产并使用其 `browser_download_url`。旧版只读取 ZIP 的 `package` 字段；新版可在两个已校验资产间自动选择。草稿 Release 会被忽略；正式版未开启测试版本时会忽略 prerelease Release。

## GitCode Release 格式

GitCode Release 与 GitHub 使用相同的发布资产：`update-manifest.json`、EXE、ZIP 和 `SHA256SUMS.txt`。程序通过 GitCode v5 Release API 获取附件，并按清单中的文件名绑定下载地址；无需在清单中硬编码 GitCode 地址。

默认配置使用 `Neruya/CarTerminalSimulation`。如果 GitCode 仓库的空间地址或仓库路径不同，请修改 `update-sources.json` 中 GitCode 源的 `owner` 和 `repository`。旧版 schema 1、2 配置会自动迁移为 schema 3：schema 1 会补充默认 GitCode 源，旧版自动生成的 `isla709/CarTerminalSimulation` 会迁移到 `Neruya/CarTerminalSimulation`；迁移完成后可以单独启停或移除任一来源，程序不会覆盖用户已配置的其他镜像。

## 多源聚合与下载选择

同一 `line + version + 包格式` 在多个更新源出现时会合并成一个候选；同一格式必须具有一致的 channel、兼容级别、SHA-256、包大小和删除清单。EXE 与 ZIP 是不同容器，摘要和大小自然不同，客户端先按安装形态选定格式，再聚合该格式的可用来源；存在冲突的来源会被排除并写入诊断日志。

点击升级、切换或回退后，程序会并行对该版本的所有下载地址执行小范围 Range 请求，显示可用性、首包延迟和试读速度。可用源按速度、延迟和配置优先级综合排序，默认选中推荐源；用户仍可手动选择任何检测通过的来源。真正安装时 `update.exe` 仍会对完整发布包执行 SHA-256 校验。

## 清单格式

```json
{
  "schemaVersion": 2,
  "product": "TerminalSimulation",
  "version": "preview10-build.20261014.1",
  "channel": "preview",
  "line": "main",
  "compatibilityEpoch": 1,
  "publishedAt": "2026-10-14T12:00:00+08:00",
  "releaseNotes": "Preview10：真实驾驶轨迹模拟与地图交互完善。",
  "minimumUpdaterVersion": "2.0.0",
  "package": {
    "format": "zip",
    "fileName": "TerminalSimulation-win-x64-preview10-build.20261014.1.zip",
    "url": "https://downloads.example.com/TerminalSimulation-win-x64-preview10-build.20261014.1.zip",
    "sha256": "64 位十六进制 SHA-256",
    "size": 12345678
  },
  "packages": [
    {
      "format": "cts-setup-v1",
      "fileName": "TerminalSimulation-Setup-win-x64-preview10-build.20261014.1.exe",
      "url": "https://downloads.example.com/TerminalSimulation-Setup-win-x64-preview10-build.20261014.1.exe",
      "sha256": "安装 EXE 的 64 位十六进制 SHA-256",
      "size": 23456789
    }
  ],
  "delete": []
}
```

GitHub Release 中 `package.url` 可以省略；通用托管源必须提供绝对或相对清单地址的 URL。`sha256` 必填。`delete` 仅用于确实需要移除的旧程序文件，路径必须相对安装目录。

以上日期、构建号、大小及地址仅为格式示例，不代表已经生成或发布的 Preview10 包；正式发布必须使用实际产物的信息。

`compatibilityEpoch` 是单调递增的不可逆兼容级别。同一级别内允许任意升级和回退；升级到更高级别后，不再显示或安装低级别版本。例如 V1 使用级别 1，存在破坏性兼容变更的 V2 使用级别 2，则 V1 可以升级 V2，但 V2 不允许回退 V1。

通用托管平台也可以返回版本目录，顶层使用同样的 `schemaVersion`、`product` 和 `line`，并在 `versions` 数组中放置多个上述版本清单。目录中的版本可以省略重复的 `product` 和 `line`。

版本推荐使用 `previewN-build.YYYYMMDD.递增数字`。同一天的随机构建后缀无法稳定排序，会被视为同序列切换候选，避免重复升级提示。

## 生成发布资产

先正常构建或发布到一个目录，再运行：

```powershell
 powershell -ExecutionPolicy Bypass -File scripts/New-UpdatePackage.ps1 `
  -InputDirectory TerminalSimulation.Wpf/bin/Release/net8.0-windows/win-x64 `
  -Version preview10-build.20261014.1 `
  -Line main `
  -CompatibilityEpoch 1 `
  -ReleaseNotes "本次更新说明"
```

正式生成 GitHub 资产时，应在构建和打包命令中使用同一个确定版本号，并通过 `-p:BuildVersionOverride=build.YYYYMMDD.N` 固定程序内显示的构建号，避免清单版本与程序版本不一致。

正式发布使用 `Scripts/New-Release.ps1`，自动同时打包安装 EXE、便携 ZIP 和双格式清单。通用托管平台需传入 `-PackageBaseUrl https://downloads.example.com/terminal/`，并将两种包和清单上传到同一位置。

## 安全与故障恢复

- 只安装通过 SHA-256 校验的包。
- 拒绝 ZIP 路径穿越和安装目录之外的删除路径。
- 每个将被覆盖或删除的文件都会先复制到事务备份目录。
- 替换失败会按逆序恢复旧文件；详情写入该次更新临时目录的 `update.log`。
- 更新包不能替换或删除 `.Updater`、`Workspaces`、`Plugins`、`config.json` 和 `update-sources.json`；新内核通过单独校验、暂存、自检和激活路径升级。

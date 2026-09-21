# 自动更新发布规范

主程序负责检查更新与展示更新说明；安装目录中的独立 `update.exe` 负责下载、SHA-256 校验、等待主程序退出、备份替换、失败回滚和重新启动。主程序不在自身进程中覆盖文件。

## 更新源配置

首次启动会在程序目录创建 `update-sources.json`。仓库中的 `update-sources.example.json` 是完整示例。更新源按 `priority` 从高到低检查，并汇总当前版本线内的全部兼容版本供用户选择。自动检查只更新“关于”页状态，不弹出安装窗口。

- `github`：读取指定仓库的 Releases。公开仓库不需要令牌。
- `manifest`：直接读取任意 HTTPS 托管平台、对象存储或 CDN 上的清单。
- `channel`：目前支持自定义字符串；默认使用 `preview`。配置和清单的通道必须一致。
- `line`：独立于版本号和 channel 的版本线标识，默认 `main`。运行中的程序只接受与编译时版本线相同的包，修改配置不能绕过此限制。
- `allowInsecureHttp`：默认必须为 `false`。只有明确受信任的内网镜像才可打开。

## GitHub Release 特别格式

每个可升级的 Release 必须同时上传：

1. 名为 `update-manifest.json` 的清单资产。
2. 清单 `package.fileName` 指定的 ZIP 更新包。

GitHub 源不信任清单中自带的下载地址，而是按文件名匹配同一个 Release 的资产并使用其 `browser_download_url`。草稿 Release 会被忽略；`stable` 通道也会忽略 prerelease。

## 清单格式

```json
{
  "schemaVersion": 2,
  "product": "TerminalSimulation",
  "version": "preview6-build.20260921.1",
  "channel": "preview",
  "line": "main",
  "compatibilityEpoch": 1,
  "publishedAt": "2026-09-21T12:00:00+08:00",
  "releaseNotes": "修复视频与网络稳定性。",
  "minimumUpdaterVersion": "2.0.0",
  "package": {
    "fileName": "TerminalSimulation-win-x64-preview6-build.20260921.1.zip",
    "url": "https://downloads.example.com/TerminalSimulation-win-x64-preview6-build.20260921.1.zip",
    "sha256": "64 位十六进制 SHA-256",
    "size": 12345678
  },
  "delete": []
}
```

GitHub Release 中 `package.url` 可以省略；通用托管源必须提供绝对或相对清单地址的 URL。`sha256` 必填。`delete` 仅用于确实需要移除的旧程序文件，路径必须相对安装目录。

`compatibilityEpoch` 是单调递增的不可逆兼容级别。同一级别内允许任意升级和回退；升级到更高级别后，不再显示或安装低级别版本。例如 V1 使用级别 1，存在破坏性兼容变更的 V2 使用级别 2，则 V1 可以升级 V2，但 V2 不允许回退 V1。

通用托管平台也可以返回版本目录，顶层使用同样的 `schemaVersion`、`product` 和 `line`，并在 `versions` 数组中放置多个上述版本清单。目录中的版本可以省略重复的 `product` 和 `line`。

版本推荐使用 `previewN-build.YYYYMMDD.递增数字`。同一天的随机构建后缀无法稳定排序，会被视为相同版本，避免重复提示。

## 生成发布资产

先正常构建或发布到一个目录，再运行：

```powershell
 powershell -ExecutionPolicy Bypass -File scripts/New-UpdatePackage.ps1 `
  -InputDirectory TerminalSimulation.Wpf/bin/Release/net8.0-windows/win-x64 `
  -Version preview6-build.20260921.1 `
  -Line main `
  -CompatibilityEpoch 1 `
  -ReleaseNotes "本次更新说明"
```

正式生成 GitHub 资产时，应在构建和打包命令中使用同一个确定版本号，并通过 `-p:BuildVersionOverride=build.YYYYMMDD.N` 固定程序内显示的构建号，避免清单版本与程序版本不一致。

若用于通用托管平台，再传入 `-PackageBaseUrl https://downloads.example.com/terminal/`。脚本会生成 ZIP 和 `update-manifest.json`，之后将二者上传到同一位置。

## 安全与故障恢复

- 只安装通过 SHA-256 校验的包。
- 拒绝 ZIP 路径穿越和安装目录之外的删除路径。
- 每个将被覆盖或删除的文件都会先复制到临时备份目录。
- 替换失败会按逆序恢复旧文件；详情写入该次更新临时目录的 `update.log`。
- 更新包只覆盖其包含的程序文件，不主动清理 `Workspaces`、主题、缓存或其他用户数据。

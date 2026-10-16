# 安装 EXE、ZIP 与兼容更新

## 用户行为

Preview11 首发时使用单独安装 EXE。后续发布脚本固定生成 `TerminalSimulation-Setup-win-x64-{version}.exe` 和 `TerminalSimulation-win-x64-{version}.zip`，并配套 `update-manifest.json`、`SHA256SUMS.txt`。GitHub / GitCode 必须上传完全相同的四项发布资产。EXE 用于安装或修复；ZIP 解压后可直接运行，作为免安装便携选项。历史发布资产不改写。

- **手动打开安装 EXE**：显示安装向导，默认安装至 `%ProgramFiles%\CarTerminalSimulation`，支持自选专用目录及桌面 / 开始菜单快捷方式。已登记的安装路径优先显示。选择原程序目录可覆盖安装 / 修复；旧便携目录不会自动搬迁。首次写入 Program Files 时会请求一次管理员授权，仅为应用目录授予当前账户更新权限，主程序和后续更新不以管理员身份运行。
- **更新器调用 EXE**：只有载荷准备，不显示安装向导、不注册安装、不创建快捷方式、不启动应用。更新器统一执行替换、运行时适配、回滚、启动验证，完成后自动关闭。
- **卸载**：Windows“已安装的应用”或程序目录 `uninstall.exe`。独立卸载工作进程运行于临时目录，仅移除安装及更新记录中的程序文件，保留配置、插件、工作区、未登记文件和事务备份。不会对整个安装目录递归删除。其他目录的同名快捷方式不会删除。
- 首次安装 / 修复成功后自动启动并验证主程序，安装向导保留“完成”按钮。正在运行同目录应用时，提示用户先关闭再重试，不强杀用户进程。

## 双格式容器

安装 EXE 是原生 Windows PE 引导程序加一个真正的 ZIP 容器。ZIP 的绝对偏移包含 PE 前缀，.NET `ZipFile.OpenRead` 能直接读取它。不是仅将 ZIP 重命名成 EXE。

ZIP 根目录仍是程序载荷，附加 `setup-package.json`，包含产品、协议、版本线、版本、兼容级别、引擎位置和逐文件 SHA-256。原生引导程序先解出清单指定的私有 .NET 8 运行时及框架依赖安装引擎，检查路径、体积、ZIP CRC32；随后由托管安装引擎校验全部载荷和持久组件哈希。安装引导、安装引擎和卸载链路不需要系统预装 .NET、解压软件或 PowerShell。Windows 自带快捷方式 COM 服务仅用于系统集成。

独立更新引擎使用 ZIP 标准 Deflate 压缩；仅引导解压所需的原生压缩运行库保持 stored。主程序和离线运行时照常压缩。该容器仍是标准 ZIP，旧 ZIP 更新链路和新托管更新器可按原协议读取。当前没有代码签名证书，不能承诺绕过 SmartScreen / 杀软信誉提示；CRC/哈希不等于发行者签名。

## 更新协议

清单保持 `schemaVersion: 2`、`minimumUpdaterVersion: 2.0.0`，不让旧客户端提前拒绝。根级 `package` 保持为 ZIP，延续旧更新器只理解单 ZIP 字段的行为；新增可选 `packages` 数组列出安装 EXE。新版客户端根据目录是否存在 `.Installation/install.json` 自动选择：安装版优先 EXE，便携版优先 ZIP；若首选格式不可用则回退到另一可用格式。若清单只有一个 `package`，新旧客户端仍按既有格式读取。

```json
{
  "package": {
    "format": "zip",
    "fileName": "TerminalSimulation-win-x64-preview12-build.20261017.1.zip",
    "sha256": "实际便携ZIP的SHA256",
    "size": 12345678
  },
  "packages": [
    {
      "format": "cts-setup-v1",
      "fileName": "TerminalSimulation-Setup-win-x64-preview12-build.20261017.1.exe",
      "sha256": "实际安装EXE的SHA256",
      "size": 12345678
    }
  ]
}
```

`zip` 表示标准 ZIP；`cts-setup-v1` 表示带原生 PE 引导的安装 EXE。字段缺失为 `auto`。新引擎按实际内容识别，声明与内容不符、未知安装器、版本/版本线/兼容级别不符时，在替换程序前拒绝。

新更新器校验整个下载包 SHA-256 后执行安装包 `--update-stage --output {空staging目录} --expected-version {version}`。安装 EXE 启动内嵌引擎的无界面协作模式，准备载荷后以进程退出码返回。更新器再次逐文件验证载荷，后续完全走同一套事务和运行时适配，不采用普通安装器静默覆盖来绕过回滚。

原始旧更新器无需识别新字段：它把下载内容当 ZIP 读取，仍能安装该 EXE 中的载荷，从而建立新兼容层。已经通过 Preview11 建立的兼容层在降级到 Preview8/9 后继续保留。未适配旧更新器如果自身因运行时问题无法启动，需要手动运行一次新安装包；不承诺追溯修复从未执行的代码。

`.Installation/`、`uninstall.exe` 与 `.Updater/`、用户数据同样受更新保护。已安装应用的显示版本跟随切换更新，降级仍使用原来的卸载入口。便携目录使用应用内更新时不强制登记为安装版。

## 发布

```powershell
pwsh -File Scripts/New-Release.ps1 -AppVersion preview11 -BuildVersion build.20261015.1
```

这是示例命令，不表示该版本已构建或发布。Release 主程序和更新引擎采用框架依赖部署，并共用安装包中随附的私有 .NET 8 运行时缓存；目标电脑无需预装 .NET，避免将两份运行时重复打入安装包。脚本排除内部插件，输出到 `artifacts/publish/{完整版本}/`。只上传安装 EXE、清单、校验和及更新说明，不上传 `application/` 中间载荷。

`New-Release.ps1` 固定调用 `New-UpdatePackage.ps1 -PackageFormat both`，同时生成两个格式。单独使用 `New-UpdatePackage.ps1` 时仍可显式选择 `zip` 或 `installer` 供兼容测试；正式 Release 应通过 `New-Release.ps1`，避免遗漏任一资产。

## 验证与边界

- 单元测试：双格式载荷一致性、版本/摘要/目录/大小约束、未知格式拒绝、用户数据与内部插件排除、安装身份保护。
- WPF 渲染：安装向导 96 / 144 / 192 DPI；遵循系统减少动画设置。
- 可选真实安装测试：设置 `CTS_INSTALLER_QA_PACKAGE` 指向 QA EXE、`CTS_INSTALLER_QA_OUTPUT` 指向 QA 输出目录，执行 `InstallerIntegrationTests`。测试通过 GUID 隔离的 HKCU 测试键和临时快捷方式目录验证，不覆盖用户正常卸载登记。
- `Test-Preview11RoundTrip.ps1 -ElevenPayload {QA载荷} -ElevenSetup {QA安装EXE} -PrivateRuntimeOnly` 检查安装 EXE 与真实 Preview8/9 ZIP 往返及旧 EXE 独立重新启动。

私有运行路径测试不会卸载机器上的 .NET，不等于完全无运行时的干净 Windows VM 验收。兼容范围仍受版本线、兼容级别、Windows x64 和可用运行时限制，不承诺未知未来安装协议 / 框架。当前为实现与 QA，未自动上传或发布。

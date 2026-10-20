# 仓库目录与版本管理

## 首次跨平台版本

- 项目目录：`TerminalSimulation.Flutter/`。
- 分支：`codex/terminal-simulation-flutter-port`，基于原 CTS `Preview11`（`c20330b`）。
- Flutter 版本标识：`0.1.0+1`，属于首个开发预览快照；功能范围和未完成项以 [实施记录](migration-status.md) 为准。
- 保留原 WPF 工程与 Git 历史。提交包含跨平台源码、原生宿主、必要资源、构建工具、已有测试/对照数据及文档。

## 目录归属

| 内容 | 位置 | Git |
|---|---|---|
| Dart 业务和 Flutter UI | `lib/` | 跟踪 |
| 四平台原生宿主 | `windows/`、`linux/`、`android/`、`ohos/` | 跟踪源码 |
| 图标与腾讯 GL 最小页面 | `assets/brand/`、`assets/map/` | 跟踪 |
| 构建与原 CTS 对照工具 | `tool/` | 跟踪源码 |
| 既有测试和假数据样本 | `test/` | 跟踪；历史结果不代替当前回归 |
| Dart 依赖锁 | `pubspec.lock` | 跟踪 |
| 地图 Key | 仓库根 `docs/KEY.txt` | 忽略，构建者自行准备 |
| 生成的加密 Key 输入 | `assets/credentials/*.bin`，仅构建副本 | 忽略，构建后清理 |
| SDK、本地依赖与构建副本 | 仓库 `bin/Debug/`、`bin/Release/` | 忽略 |
| 交付/发布包 | 仓库 `artifacts/` | 忽略 |
| IDE、Flutter、Gradle、Hvigor、原生依赖缓存 | `.idea/`、`.dart_tool/`、`.gradle/`、`.hvigor/`、`oh_modules/` 等 | 忽略 |
| 签名、证书、私钥、设备 Profile | `.p12`、`.p7b`、`.cer`、`.csr`、`.pem`、`.key`、`.jks`、`.keystore` | 忽略 |

OHOS 的依赖锁包含本机 Flutter HAR 与插件路径，继续由生成工程维护，不提交。公共 `ohos/build-profile.json5` 保持空签名配置；DevEco 签名与本机应用标识只保存在 Git 忽略的构建副本中。

## Git 过滤规则

项目级 `.gitattributes` 统一脚本、Dart/ArkTS、配置与文档为 LF，Windows 批处理为 CRLF，图像与包文件按二进制处理。规则作用域仅为本项目。

当前文件规模不需要 Git LFS；项目没有绑定 LFS 或自定义 clean/smudge 内容过滤器。机器全局安装的 LFS 过滤器不会自动将普通文件转换成 LFS 指针，是否应用以 `git check-attr filter` 为准。

提交前检查实际暂存文件：源码中不得包含本机 Key 明文、签名材料、SDK、应用包、设备数据或运行日志。构建和运行证据保存在被忽略的 `bin/Debug/`，公共文档仅记录结果与路径。

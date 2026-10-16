# Preview10 GitHub 发布上传说明

版本：`__VERSION__`。建议 Tag：`Preview10`，Release 标题：`CTS-Preview10`，勾选 **Pre-release**，目标分支 `main`。此文件是打包模板，构建后会替换版本及包名。

## 上传资产

将下列两个文件上传到同一个 Release，保持文件名不变：

1. `__PACKAGE__`
2. `update-manifest.json`

Release 正文使用 `RELEASE-NOTES.md`；`SHA256SUMS.txt` 可额外上传。不上传 `application/` 文件夹。草稿 Release 不会被应用识别。新建此 Release 时只上传一个清单，不需要沿用旧清单。

清单采用 schema 2，版本线 `main`，通道 `preview`，兼容级别 `1`，最低更新器版本 `2.0.0`。GitHub 根据清单中的文件名匹配同一 Release 的 ZIP，`package.url` 为空是正常情况。如同步发布 GitCode，上传完全相同的 ZIP 和清单，不要分别重新打包。

## 产物与修复说明

Windows x64 自包含目录式发布，包含主程序、.NET 8 桌面运行时、独立更新器、old/new 两套地图与 FFmpeg 原生模块。不含内部迅洁云插件、用户配置、工作空间、缓存、明文 Key 或额外语言目录。

此包已修复更新器临时启动遗漏运行时的问题，并补测完整启动器链路。已经安装有问题的 Preview9 / 旧 Preview10 时，旧更新器不能借助尚未下载的修复包修复自身；应关闭应用后手动解压新包到新目录，或先手动替换完整程序文件。保留用户配置和工作空间，不要只替换 `update.exe`。

程序、ZIP 和清单使用同一个版本号，大小与哈希以清单和 `SHA256SUMS.txt` 为准。同一未发布版本重新打包后，必须上传本次生成的新 ZIP 与新清单，不混用旧清单。

## 原生依赖发布检查

按 `FFmpeg.Native/README.md` 的现有要求，公开分发还需提供 FFmpeg / x264 许可证、对应源码获取方式、准确构建配置与本地修改说明。本包保留原生运行库的 `FFmpeg-GPL-LICENSE.txt`；二进制打包与启动检查不等同于完成全部对应源码分发检查，上传前请确认配套材料。

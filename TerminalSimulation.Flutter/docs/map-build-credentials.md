# 构建者本地地图 Key

## 约定

- 文件名已经用户确认：仓库根目录 **`docs/KEY.txt`**。Git 已忽略此文件；构建脚本同时要求它处于未跟踪、被忽略状态。
- 文件是 UTF-8 单行腾讯地图 Key，允许 BOM 和末尾换行；不包含字段名、引号或注释。同一个固定 Key 提供 JavaScript API GL 和 WebService，构建者自行开通对应权限及来源限制。
- 谁构建应用，包内就使用谁本机文件中的 Key。不会读取他人开发环境、环境变量、应用配置或上次构建的 Key。
- 应用没有输入/覆盖 Key 的界面，不把 Key 写入普通设置、路线文件或历史。修改本地文件后须重建应用。

## 构建路径

Windows、Android、HarmonyOS 使用 `tool/Build.ps1`，Linux 使用 `tool/Build.sh`。默认 Debug，对应仓库 `bin/Debug/Flutter/{平台}/`；Release 对应 `bin/Release/Flutter/{平台}/`。

1. 检查本地文件存在且未被 Git 跟踪，并删除构建副本中的旧生成输入。
2. 解析锁定依赖，在构建副本内运行纯 Dart 生成器 `tool/embed_map_credentials.dart`。
3. 生成器在进程内读取文件，检查格式，用 AES-256-GCM 与随机 nonce 封装，并在写出前检查解封结果与本地值一致。
4. 仅写出构建副本内的 `assets/credentials/map-credential.bin`。不生成带 Key 的 Dart/JS/C++ 源码，不通过 dart-define、环境变量或命令参数传递 Key。
5. Flutter 将加密二进制资源嵌入包。脚本结束后删除构建副本中的生成输入，最终 bundle/安装包保留加密资源。
6. 应用启动时从包内解封为只读 `MapCredentials`，供地图 SDK 与检索/导航请求使用。资源位于地图 HTTP 宿主白名单目录之外，不能经地图资源 URL 直接读取。

缺失、空白、格式无效、被 Git 跟踪或解封校验失败都会停止脚本构建。错误只报告通用状态，不输出 Key。直接调用 `flutter build/run` 不执行注入步骤，因此必须使用上述脚本生成含地图配置的应用。

加密接口使用固定版本 [cryptography 2.9.0](https://pub.dev/packages/cryptography) 的纯 Dart [AES-GCM](https://pub.dev/documentation/cryptography/latest/cryptography/AesGcm-class.html)，不增加平台原生加密插件依赖。

## 源码与产物边界

- 本地原始 `docs/KEY.txt` 不提交、不复制进源码、不打包；构建生成的凭据资源也处于 Git 忽略路径。
- 仓库中只有封装/解封逻辑，没有构建者 Key 的明文字面量。构建日志、普通设置和静态打包 HTML/JS 不携带 Key 明文。
- 加密封装防止明文随源码和资源分发；运行时腾讯客户端 SDK 和服务必须接收到实际 Key。包装算法及解封逻辑也随应用发布，因此不能承诺内嵌 Key 无法被逆向提取，也不能把它当作服务端秘密。
- 原 WPF 项目的构建凭据路径保持原状，跨平台实现不复制 WPF 的生成源码或改变其配置。

## 本轮结果（2026-10-20）

- Windows、Android、HarmonyOS Debug 构建已成功，使用本机构建者文件注入；日志为仓库 `bin/Debug/flutter-embedded-key-{windows|android|ohos}.log`。
- Dart 静态分析通过；本轮没有新增或运行自动化测试。Linux 只做脚本语法检查，没有本机构建/运行证据。
- APK/HAP 各含一份加密凭据资源，均不含原始 `KEY.txt`；152 个公开源码文件、三个构建日志、APK/HAP 全部解压条目及 Windows Flutter bundle 的本机 Key 明文匹配数均为零。检查只输出计数，不输出 Key 或指纹。
- 三个平台的九项运行时源码/配置与最终构建副本逐项 SHA-256 一致。安卓 API 37 可见模拟器 `emulator-5558` 已安装新版，主界面启动正常，轨迹页不再显示 Key 输入框，直接打开地图后出现腾讯 SDK 标识、缩放控件及新的底部面板，说明包内凭据已解封并交给 SDK；未完成真实搜索/道路规划权限或跨设备性能验收。

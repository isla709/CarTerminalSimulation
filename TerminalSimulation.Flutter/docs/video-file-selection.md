# 视频文件选择

更新于 2026-11-02。

- 视频工作台使用“选择视频文件”入口，不再手填路径。Windows/Linux 调用 file_selector 系统文件对话框；Android 调用 ACTION_OPEN_DOCUMENT；HarmonyOS 调用 DocumentViewPicker。
- 移动端将用户选定的文件异步复制到应用私有 video-sources 目录，返回可供 Dart 读取的路径；不把整段视频通过 MethodChannel 传输，不申请全盘访问权限。取消选择保留原来源，重复点击被禁用，失败会清理未完成副本。
- 继续遵循现有推流器的 H.264 Annex-B 裸流及 256 MiB 限制。没有新增 MP4/MOV 解封装、转码或图库视频支持。桌面和鸿蒙选择器过滤 h264/264/avc；Android 文件提供者的 MIME 标注可能不同，最终仍由裸流解析器检查数据。
- API 26 模拟器已确认能够打开系统文件选择器并取消，证据为 bin/Debug/video-system-picker.png。本轮未选择实际视频，未验证文件复制和推流，也未在 Windows/Linux/Android 实际操作选择器。
- Android Debug、HarmonyOS x86_64 与 ARM64 Debug 构建通过。Dart 视频页及文件选择服务静态分析通过。没有新增或运行自动化测试。

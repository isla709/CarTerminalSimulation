# 输入与焦点修复（2026-10-21）

## 问题与处理

### Windows 地图搜索中文无法提交

`webview_cef` 0.6.2 在整个 Flutter HWND 上安装 IME 子类窗口过程，无条件吞掉 `WM_IME_STARTCOMPOSITION`，同时修改 `WM_IME_SETCONTEXT`。即使焦点位于 Flutter 原生搜索框，也会影响框架组词与提交。

`windows/cef_compat.cmake` 在构建目录生成兼容源码：仅当 CEF 中获得焦点的浏览器确实聚焦可编辑 DOM 节点时处理这四类 IME 消息，其余情况完整调用 `DefSubclassProc`。不修改共享 Pub 缓存。依赖源码变化时兼容补丁会使 CMake 明确失败，避免静默失效。

Runner 的 `WM_ACTIVATE` 仅在激活时恢复 Flutter 子窗口焦点，失活时不抢回焦点。

### 移动端键盘弹出后立即消失

`WorkbenchViewport` 与报文分析页曾在可用高度跨过阈值时，将输入区从直接子节点切换为滚动容器内的子节点。软键盘压缩窗口高度时因此销毁、重建编辑状态和输入连接。

- 工具页始终保留同一滚动容器与编辑树，仅调整内容高度。
- 地图画布和操作面板始终位于同一 Stack 的固定位置，不在 Row、Column、Stack 之间迁移。键盘展开时，竖屏操作面板使用剩余高度，临时隐藏底部模拟按钮，为当前编辑区留出空间。
- HTTP 工作区及请求/响应面板在响应式断点变化时维持父子树结构，切换布局不销毁编辑器。

### 全部输入区的共享约束

- 原直接使用 TextField/TextFormField 的输入统一为 `AppTextField`，由 State 管理隐式控制器、焦点和 `keepScrollOffset:false` 的文本滚动控制器；保留调用者显式控制器。`EditField` 原有的同类保护继续保留。
- 文本视口不继承 ExpansionTile 或外层滚动容器的 PageStorage 标识，避免布尔展开状态与文本偏移混用。
- 地图搜索有独立、稳定的 FocusNode 和 Key；组词期间不请求联想，不执行提交。地图速度控件在单/双列之间使用同一 Wrap 树。
- `EditField` 外部同步不打断有焦点或正在组词的内容；多行字段请求多行输入类型。
- 名称对话框的控制器由对话框 State 释放，覆盖完整退场动画，不在 showDialog 返回后的第一帧提前释放。
- HTTP 参数行提供对象身份索引，删除前面的行不会把后面字段的编辑状态错配。切换主页面或 HTTP 页签时主动结束旧编辑焦点，避免继续输入隐藏字段。

这些规则以 Flutter 的 [焦点生命周期](https://docs.flutter.dev/ui/interactivity/focus) 和 [TextEditingController 组词约束](https://api.flutter.dev/flutter/widgets/TextEditingController-class.html) 为依据。

## 自查范围与结果

- 新增 `test/input_lifecycle_test.dart` 的 9 项回归：终端各可编辑字段、驾驶设置、工作台高度变化、报文分析、HTTP 响应式布局、折叠状态存储、模型更新时编辑草稿、地图搜索结果/错误更新、名称对话框退场。
- 与已有 `layout_test.dart`、`native_material_test.dart` 合计 23 项通过。覆盖 320–1440 逻辑宽度、横屏、高 DPI、字号、主题与减少动画切换。
- 组词检查使用 Flutter 测试输入连接，核对 EditableText State、FocusNode、composing 范围及键盘连接。它不代替 Windows 系统输入法候选窗口或鸿蒙真机输入法的端到端验证。
- Dart 静态分析通过。构建与自查日志在 `bin/Debug/input-lifecycle-*.log`；全部普通构建保存在 `bin/Debug/Flutter/`。
- 鸿蒙 HAP 未签名，需要有效签名后安装。当前没有可用 HDC 真机连接，本轮不声称已在手机上复现并确认所有输入法均正常。

真机复查建议：地图中文搜索选词/提交、连接地址和端口、注册身份、标准/RAW 附加项、驾驶设置、HTTP 名称/URL/参数/正文、透传内容、日志筛选、报文分析输入及筛选；分别检查首次键盘、切换字段、横竖屏、折叠面板以及切到后台再返回。

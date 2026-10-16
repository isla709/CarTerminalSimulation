# Preview11：持久更新内核与运行时适配

安装发布新增 3.1.0 内核：支持单安装 EXE 与历史 ZIP 自动适配，安装与卸载入口、协议和发布命令见[安装 EXE 方案](installer-preview11.md)。下方 3.0.0 目录及原验证记录为前一阶段证据，不代表新安装流程已验收。

## 旧版更新器与首次安装的启动桥

Preview11 主程序采用 framework-dependent 部署，私有 .NET 8 运行时位于 `.Updater/runtimes/net8-x64`。Preview10 及更早的更新器不会调用 Preview11 的 `RuntimeAdapter`，因此发布 ZIP/安装载荷必须在打包前将主 EXE 预置为 `.Updater/compat-host.exe`，并附带相对于安装根目录的 `.Updater/app-runtime.txt` 与 `.Updater/app-hostfxr.txt`。这样旧更新器复制完新载荷后可以直接启动目标程序；Preview11 自己更新时仍会预检运行时并写入规范化的运行时路径。

发布脚本通过 `Scripts/Prepare-LegacyRuntimeBridge.ps1` 生成这两个相对路径标记。安装包清单会校验标记均指向载荷内部的私有运行时。不要移除该启动桥，也不要把机器绝对路径写入标记。

## 行为目标

通过新版更新系统切换旧版应用后，继续保留新版更新能力。主程序可以回退，更新兼容层不随主程序回退；仍然禁止跨版本线及向更低兼容级别回退。这里的“任意版本”指同版本线、兼容级别允许、Windows x64 平台可运行的版本，不承诺未知未来框架、旧操作系统或不兼容数据格式。

## 部署布局

```text
应用目录/
  TerminalSimulation.Wpf.exe       自包含原 EXE，或指向私有运行时的原生兼容宿主
  TerminalSimulation.Wpf.dll       目标版本的原托管程序
  update.exe                       原生持久入口，无 .NET 依赖
  update.dll / deps / runtimeconfig 旧主程序的存在性检查兼容文件
  .Updater/
    current.txt / last-good.txt     当前与后备引擎槽
    engines/3.1.0/                 框架依赖 WPF 更新引擎
    compat-host.exe                旧版应用原生启动适配器
    runtimes/net8-x64/              标准 dotnet 布局的私有运行时
      host/fxr/{version}/
      shared/Microsoft.NETCore.App/{version}/
      shared/Microsoft.WindowsDesktop.App/{version}/
    bundle.json                    组件清单与逐文件 SHA-256
    state.json                     版本线、兼容级别高水位、文件所有权和事务状态
    app-runtime.txt / app-hostfxr.txt
    transactions/{id}/backup/       写前日志及可恢复文件
  Workspaces/                      用户工作区，始终保护
```

不要删除或混入旧 ZIP 的 `.Updater`。三个根目录托管 `update.*` 文件仅兼容旧检查，不用来启动新内核。新框架依赖引擎使用随包私有运行时；更新前会把引擎依赖和私有运行时复制到临时工作目录再执行，避免自身文件锁阻碍安装。

## 运行时决策

| 目标类型 | 选择与处理 |
| --- | --- |
| 自包含 | 校验基本运行时文件完整性，恢复包内原 EXE；不借用旧版或系统运行时 |
| 框架依赖，私有缓存兼容 | 优先使用安装包随附的私有标准运行时缓存，并由原主程序路径上的原生宿主持久启动 |
| 框架依赖，私有缓存缺失或预检失败 | 检查 x64 系统运行时并用原生 hostfxr 实际预检；都不可用时在修改前停止 |
| 所需框架不在系统或缓存 | 在替换应用之前停止，说明缺少兼容运行时，不盲目跨主版本滚动 |

私有缓存从干净的官方标准安装布局构建，不从主程序的平铺 DLL 拼装，避免 NuGet JSON 9 覆盖框架 JSON 8 等混合依赖。Preview11 默认携带匹配的 .NET 8 Core/Desktop x64 运行时，覆盖现有 Preview7/8 框架依赖包，也供当前框架依赖主程序与更新器共用，避免重复携带同一运行时。后续包可在独立版本目录补充缓存；缓存按哈希校验，同路径不同内容拒绝覆盖。未知框架不从未验证的网络地址临时安装。

原生兼容宿主在原 EXE 进程内通过 hostfxr 运行旧托管 DLL，保留 `Environment.ProcessPath` 与 `AppContext.BaseDirectory` 的旧版约定；不是只在更新器启动子进程时设置一次环境变量。更新引擎在独立工作进程中使用私有运行时，旧应用则独立启动，不在一个进程内加载两套 CLR。

## 更新事务与恢复

1. 校验 schema 2 计划、目标路径、版本线和持久兼容级别高水位，并取得安装目录锁。
2. 等待主程序退出，恢复上次中断的事务（如果存在）。
3. 下载并校验整个 ZIP 的 SHA-256；安全解压，限制文件数量和总解压体积。
4. 校验组件包与保留的内核，预检目标运行时。新运行时缓存只增补经过验证的文件。
5. 写入事务状态。每个覆盖/删除先备份，再写前日志，再进行原子文件替换。
6. 安装应用、清理旧运行时/已知所有权的废弃文件，必要时安装持久兼容宿主。
7. 启动目标 EXE，验证目标进程没有退出、主窗口具有正确标题且可响应，并短暂观察后提交状态。
8. 失败或取消：先结束本次启动的目标进程，再逆序恢复文件；回滚失败保留待恢复事务及诊断信息，不虚报已恢复。

启动检查是进程/主窗口就绪检查，不等同于协议连接、地图 API、插件或业务功能的全面健康测试。备份和诊断日志保留，当前不会自动删除历史事务目录，必要时应在确认安装正常后人工归档。

## 内核自身升级

新应用包可携带更高版本的内核槽。整个包通过 SHA-256 后，对组件逐文件校验，原子暂存不可变引擎目录，并用 `--health-check` 验证引擎自身可运行；应用启动成功后才切换当前指针，保留 last-good 槽。原生入口等待工作进程就绪确认，当前槽缺失或未就绪时尝试后备槽，成功后修复当前指针。

应用降级包里的旧 `update.*` 和 `.Updater` 不进入普通覆盖/删除流程。原生入口协议保持稳定；根入口不被历史包覆盖，但更高内核包可以通过专门的哈希校验与事务路径前向更新根入口。框架依赖应用的兼容宿主同样通过专门事务路径更新。

发布清单仍为 `schemaVersion: 2`，`minimumUpdaterVersion: 2.0.0` 指旧协议能力，而非内部引擎的 3.0.0。不能将它直接改成 3，否则 Preview9 等客户端会在看到版本时就拒绝。新包可包含旧客户端忽略的 `updater` 可选元数据。

安全基础仍为受信任 HTTPS 更新源与包 SHA-256；这里没有新增发行者数字签名体系。不得因为新内核存在就降低来源信任要求。

## 现代化 UI

更新窗口使用独立轻量 WPF 样式，不依赖主程序 MaterialDesign 资源或运行库：渐变图标、圆角卡片、五阶段状态、平滑单向进度、阶段文字淡入/位移动效和完成反馈。遵循 Windows 客户区动画/UI 效果开关；关闭动画时直接展示最终状态。文件 IO 与校验不在 UI 线程运行。

取消按钮与窗口关闭请求统一走取消令牌，等安全恢复后才能关闭；成功时主程序已通过启动验证，更新窗口自动关闭。错误长文可滚动，版本号可换行，按钮不覆盖状态内容。

## 构建与验证

普通 Debug/Release 构建自动运行 `Scripts/Build-UpdaterBundle.ps1`，把原生入口和隔离引擎加入相应 `bin/Debug` 或 `bin/Release`。需要 MinGW x64 编译器（与本项目原生 FFmpeg 桥相同环境）以及干净匹配的 .NET 8 Core/Desktop x64 标准布局来生成离线缓存。

QA 测试用载荷示例（不是正式发布包）：

```powershell
dotnet publish TerminalSimulation.Wpf/TerminalSimulation.Wpf.csproj -c Debug -r win-x64 --self-contained true -p:IncludeXunjieCloudPlugin=false -p:BuildVersionOverride=build.20261015.qa -o artifacts/.qa/preview11-payload
pwsh -File Scripts/Test-Preview11RoundTrip.ps1 -PrivateRuntimeOnly
pwsh -File Scripts/Test-Preview11RoundTrip.ps1 -FaultScenarios
dotnet test TerminalSimulation.Tests/TerminalSimulation.Tests.csproj -c Debug -p:IncludeXunjieCloudPlugin=false
```

往返测试使用现有真实 Preview9、Preview8.2 ZIP，检查保留内核、工作区哨兵、目标主窗口以及关闭后独立重新运行 EXE。`PrivateRuntimeOnly` 只对测试进程禁用系统候选选择；不会卸载本机 .NET，因此它验证私有运行路径，不代替干净 Windows 虚拟机验收。

首次从本身已无法启动更新器的原始 Preview9 建立兼容层，仍需手动解压一次完整 Preview11 包。保障范围是此后通过新版更新系统的切换，不包括人为整目录覆盖、删除兼容层、篡改状态文件或强行跨兼容级别安装。

### 2026-10-15 验证记录

- 最终 Debug 构建：`preview11-build.20261015.9380`；无内部插件的 184 项测试全部通过。
- 下载、长错误和成功状态分别覆盖 96/144/192 DPI 渲染，状态区与按钮没有相互遮挡。
- 真实包往返：Preview11 → Preview9.1 → Preview11 → Preview8.2 → Preview9.1 → Preview11；系统候选运行时及强制私有运行时两条路径均运行通过。
- 私有路径下关闭 Preview8.2 后独立重新启动 EXE，通过主窗口检查和 host trace 确认使用私有运行时。
- SHA-256 错误、目标 EXE 退出码 42、候选内核自检失败、当前内核槽缺失的故障场景通过；工作区哨兵保留。
- 发现并修复 Windows 暂存引擎目录原子重命名的瞬时拒绝，采用有界重试，仍不暴露半复制的活动槽。
- 最终私有路径/故障用例证据：`artifacts/.qa/preview11-roundtrip-5a78c8b749d04c8d865885ac551825d7/`；系统路径用例：`artifacts/.qa/preview11-roundtrip-d093a6d9c1e9490d83407466e6666475/`；渲染：`artifacts/.qa/preview11-ui/`。
- 尚未完成：完全无 .NET 的干净 Windows 虚拟机验收，以及未来正式更高版本内核的真实发行包前向升级验证。已验证的候选内核场景为故障拒绝激活与后备恢复，不将其表述为未来版本实测。

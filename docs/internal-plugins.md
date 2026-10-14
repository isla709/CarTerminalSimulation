# 内部插件与公开仓库

`TerminalSimulation.Plugins.XunjieCloud/` 是内部插件，只保留在本地，不属于公开源码。该目录从 Git 索引移除，并通过根目录 `.gitignore` 阻止后续普通 `git add` 重新加入。不要强制添加，也不要将该目录复制到其他被跟踪的位置。

公开解决方案不直接列入内部插件项目。`Directory.Build.props` 检测本地插件项目是否存在：存在时主程序和内部测试可照常构建；不存在时跳过相关引用、内部测试及发布文件，不要求获取内部源码。

即使本地有插件，也可明确排除它：

```powershell
dotnet build TerminalSimulation.slnx -c Debug -p:IncludeXunjieCloudPlugin=false
dotnet test TerminalSimulation.Tests/TerminalSimulation.Tests.csproj -c Debug -p:IncludeXunjieCloudPlugin=false
```

本地存在插件且未显式排除时，发布流程仍可能打包内部插件二进制。对外打包应明确指定 `-p:IncludeXunjieCloudPlugin=false`，并使用新的输出目录，避免旧输出中残留的插件文件混入结果物。插件源码和本地配置不要放入发布包。

本次变更通过正常提交移除当前版本中的插件，不改写 Git 历史。以前的提交仍包含曾经提交的内容；彻底清理历史需要单独进行历史重写与远端协调。

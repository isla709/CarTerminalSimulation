# CarTerminalSimulation 项目约定

## 构建与输出目录

本项目统一遵循以下输出规则：

1. 未明确指定构建类型时，构建到 `bin/Debug`。
2. 明确要求构建 Release 时，构建到 `bin/Release`。
3. 用户要求交付、输出或打包时，将结果物放到 `artifacts/{结果特征或版本名}/`；例如 `artifacts/preview6/`、`artifacts/win-x64-portable/`。
4. 用户要求发布时，将结果物放到 `artifacts/publish/`。

这些规则仅适用于 `CarTerminalSimulation` 项目。除非用户另有明确要求，不要把普通 Debug/Release 构建结果放入 `artifacts`，也不要把发布产物放入普通 `bin` 目录。

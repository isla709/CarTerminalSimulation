# Preview10 地图操作增强

`config.json` 中 `MapProvider: "new"` 和 `MapProvider: "old"` 现均支持下列操作增强。两者仍采用独立的地图引擎、前端资源、检索服务及记录文件。

## 交互

- 三种模式：导航路线、自由绘制、单点选择。单点选择不规划路线，每次点击替换上一点，可直接应用到位置上报。
- 搜索联想：输入停顿 400ms 后请求，中文输入法组词期间不请求，前端最多缓存 30 组结果。旧查询的延迟响应不覆盖新查询。支持上下方向键、Enter 和 Escape。新版使用腾讯关键词输入提示；旧版使用支持联想的 Photon，服务层限制每秒最多一次检索并缓存 10 分钟（最多 40 组）。
- 搜索框仅保留外层的圆角焦点高亮，不再叠加内部矩形轮廓。路线标签 `NARROW`、`LEAST_LIGHT` 等转为中文，去重，并隐藏未知的英文枚举。
- 点击搜索框可查看最近搜索；支持单条删除和全部清空。管理入口与轨迹记录共用独立的 Web 卡片弹窗。
- 轨迹库：开始模拟时自动保存当前模拟路线；也可手动“记入轨迹库”。支持重命名、恢复、删除和清空。该记录保存的是模拟路线快照，不是 GPS 实测采集日志。
- 恢复轨迹时读取保存的选点模式、原始坐标值、坐标系和模拟速度。不把所有路线统一转换为某一种坐标系。
- 单点文件仍使用 `.ctsroute` 专用格式。导航与绘制路线至少需要两个点，单点模式只允许一个点。
- 顶部定位、设置、收起按钮改为统一的描边 SVG 图标；应用面板标题改为“地图与轨迹”。SDK 自带版权标识不作移除。
- 按钮反馈、模式滑动指示、面板收起展开、内容入场、联想列表和弹窗加入过渡，尊重系统减少动态效果偏好。

## 拖拽

地图区域禁用浏览器原生图像/文本拖放和文本选择，避免触发禁止放置的光标。指针移动超过 5px 视为拖图，结束后短时间忽略选点事件。模拟期间手动拖图会暂停自动跟随车辆，点击定位按钮可恢复跟随。

## 本地数据

两套地图还支持独立保存的真实驾驶车辆设置、车辆预设和驾驶状态面板，详见 [真实驾驶轨迹模拟](vehicle-simulation.md)。

搜索和轨迹记录独立保存在主程序目录：

```text
Workspaces/Map/new/history.json
Workspaces/Map/old/history.json
```

最多保留最近 50 条搜索、30 条轨迹。历史写入串行化并使用项目现有的原子 JSON 保存机制，写入失败会显示错误，不将失败操作当作已保存。恢复轨迹时再次校验内容与坐标范围。导出的 `.ctsroute` 文件不受删除记录操作影响。

轨迹记录按选点模式和有序轨迹点去重，不按名称、速度或车辆预设区分。同一路线再次保存时更新参数和时间并移到最前，自动记录保留用户重命名，手动保存可更新名称。不同路线、反向路线或不同选点模式不会仅因同名被合并。坐标系转换后位置相同的轨迹也可识别为重复（逐点比较容差 0.5 米）；此换算仅用于比较，不更改保存内容，保留最新记录的原始坐标系和坐标值。直接切换坐标解释导致位置改变的路线不会被合并。

首次读取时自动合并已有重复项，保留时间最新的一条，并在相同目录保留去重前的 `history.json.dedupe-*.bak` 完整备份。此专用备份不会被后续自动保存覆盖。两套地图各自去重，不跨 `old/new` 合并。

## 旧地图方案

独立前端位于 `MapAppOld/`，保留 Leaflet、高德瓦片、OSRM 导航、五种地图风格、四种路线样式和可拖动的起终点。Leaflet 及箭头库固定版本随程序输出，不再依赖外部 CDN 加载。旧版仍为二维地图，不显示不支持的 3D 控件。

搜索与联想使用 [Photon](https://github.com/komoot/photon)，无需腾讯 Key。公共服务没有可用性保证；大量部署建议改接自有服务。原 Nominatim 公共服务的[使用政策](https://operations.osmfoundation.org/policies/nominatim/)禁止客户端自动联想，因此不将输入联想请求发送到该服务。Photon 与 OSRM 返回 WGS-84，进入当前逻辑坐标系后才用于路线、保存与上报；服务层换算不会强制切换用户坐标系。OSRM 不提供红绿灯数量，因此旧方案不显示虚构的红绿灯统计。

## 回归检查

```powershell
dotnet test TerminalSimulation.Tests/TerminalSimulation.Tests.csproj -c Debug --filter "FullyQualifiedName~OldMapServiceTests|FullyQualifiedName~MapHistoryStoreTests|FullyQualifiedName~TencentMapServiceTests|FullyQualifiedName~GeoCoordinateConverterTests"
node Scripts/Test-MapUi.cjs new
node Scripts/Test-MapUi.cjs old
```

Web 测试的 `new` 使用模拟 SDK 与假 Key，`old` 使用随项目提供的真实 Leaflet 渲染库。服务回调与底图请求模拟处理，不占用线上 API 配额。截图输出到 `artifacts/.qa/map-ui-*`。Node 测试使用 Codex bundled Playwright，也可通过 `CTS_NODE_MODULES` 指定相应 node_modules 路径。

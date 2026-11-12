# 移动端后台连接与设备系统定位（2026-10-21）

## 用户入口

- Android / HarmonyOS 的终端页增加“后台保持连接”（默认开启，可保存偏好）；用户连接平台时申请后台任务，断开后释放，无连接且没有 GNSS 工作时不常驻后台。
- 位置汇报提供“使用设备系统定位”，启用时请求位置权限，支持用户仅授予大致位置。默认关闭，重启应用后不自动启用、不持久化本次定位；启用定位本身不自动打开周期上报。
- 地图切换为设备实时位置，隐藏路线、选点及编辑面板；可拖动浏览、恢复跟随或停止设备定位。停止定位后恢复此前手填位置/路线编辑上下文，不自动恢复轨迹播放。
- Android 通知显示后台任务，提供返回应用及“停止任务”；系统后台设置入口由用户自行管理省电限制。

## 平台适配

### Android

- `CtsApplication` 持有进程内缓存 FlutterEngine，`MainActivity` 使用外部引擎且不在宿主销毁时释放；连接和 GNSS 通道绑定引擎，不绑定页面。文件选择器通道随 Activity 重新绑定，销毁时解除引用。
- `CtsConnectionService` 是用户发起的前台服务，终端会话采用 `specialUse`，声明明确的交互式 JT808 联调子类型；真实定位期间增加 `location`。不将无限交互式终端会话伪装成有后台时长配额的批量 `dataSync`。
- 持有仅用于当前活动任务的局部 CPU 唤醒锁；断开且无定位、通知停止、服务销毁/超时均释放。`START_NOT_STICKY`，不使用启动广播、不在进程被强制停止后静默恢复连接或定位。
- 使用系统 LocationManager 的 GPS、网络和可用的融合 Provider，每秒请求位置。精确权限允许 GPS；大致权限仍可使用系统允许的网络或融合定位。各 Provider 独立尝试订阅，不依赖 Google Play Services；设备没有网络定位 Provider 时，界面不会声称已启用 Wi-Fi/LBS。
- Provider 返回值映射为 GNSS、网络、融合或未知来源。不通过连接 Wi-Fi、精度较好、卫星可见等现象猜测具体来源；Provider 开关变化会更新服务状态。
- `specialUse` 如用于商店发布，需按实际用途完成相应声明/审核；本轮不代表商店审核结果。

### HarmonyOS

- `CtsRuntimePlugin` 与引擎绑定，使用 KEEP_BACKGROUND_RUNNING；Ability 声明 dataTransfer / location。
- 连接和定位按实际用途组合申请长时任务，模式变化使用 updateBackgroundRunning。通知 WantAgent 打开当前应用，后台任务取消事件会停定位并通知 Dart 停止连接/重试。
- 使用系统 geoLocationManager 的精度优先 LocationRequest，场景为 UNSET、间隔 1 秒、无最大精度过滤；由系统选择可用信号，移除原先仅接受 GNSS 的过滤。请求 APPROXIMATELY_LOCATION / LOCATION 使用期间权限，允许仅大致位置，并配合定位长时任务。
- 按 SDK 的 sourceType 区分 GNSS、RTK、NETWORK、INDOOR，字段缺失或未知值显示“来源未提供”。本机 API 26 的枚举没有融合项，不将未知来源伪装成融合；网络来源显示“可能使用 Wi-Fi、基站 LBS 等，系统未细分本次来源”。定位错误、权限失效、定位开关关闭分别显示对应原因。
- FlutterAbility 正常进入后台不销毁业务状态；宿主被用户结束/引擎销毁时清理任务及定位回调。没有承诺宿主、进程被强制销毁后还能保有原 TCP 套接字。
- 模拟定位标志为 API 26 可选字段，较低系统不具备相同的模拟来源识别保证。长时任务、卫星能力等仍受设备和系统策略约束。

## 连接恢复与停止语义

- 后台连接保护开启时，对异常断线使用 1、2、4、8、16、30 秒上限的退避重试；重试保持最初连接的地址、端口、手机号和协议版本快照，不跟随用户正在编辑的草稿跳到其他平台。
- 上次已成功鉴权的会话才尝试用本次内存中的鉴权码恢复；原周期汇报等待鉴权成功后恢复。新的手动连接不继承上次的自动鉴权/周期任务意图，鉴权码不落盘。
- 鉴权成功后，后台连接保护会启动周期心跳；已鉴权且启用心跳时，无入站响应超过 `max(90 秒, 3 × 心跳间隔)` 会断开旧传输并重试。未鉴权的手工协议调试不会被该响应超时规则误断开。
- 手动断开、取消连接/重连、系统停止通知任务会取消重试；已有发送世代检查继续阻止旧会话排队报文进入新连接。
- 定位数据持续无效时，重复的周期汇报错误只记录一次，数据恢复后清除错误去重状态；不积压过期位置或在恢复后突发补发旧轨迹。

## 定位数据边界

- 系统位置按 WGS-84 处理，协议位置保持 WGS-84；仅在腾讯地图渲染边界转换为 GCJ-02。系统定位模式期间禁止手填位置、轨迹播放和地图选点，历史路线不被真实位置覆盖或自动记录。
- 采用系统时间戳，经纬度及有限数校验，单调时钟定位龄不超过 30 秒、墙上时间差不超过 60 秒；支持识别的模拟定位被拒绝。移除 200 m 的硬性精度上限：50 m 内显示“精度较好”、200 m 内“一般”、超过 200 m 显示“粗略位置”，0 表示精度未提供。系统报告精度不是保证误差范围。
- 并行 Provider 通常拒绝旧时间戳；允许 5 秒内显著改善精度的位置接替稍新的粗略位置，避免 GNSS 回调略晚于网络回调而被持续压制。5 秒内优先保留明显更准确的位置，超过 5 秒允许粗略的新位置接替。数据过期、关闭定位或等待首次定位时禁止发送 0x0200/0x0201，不用先前的模拟位置、IP 定位或零坐标顶替。
- 速度由 m/s 转 km/h，范围限于协议 0–6553.5；方向归一化至 0–359；协议高程为无符号 WORD，因此高程限于 0–65535。系统未提供的运动字段显示“未提供”，协议固定字段填 0，不继承模拟速度或高程；定位有效时汇报状态自动置定位位。
- 终端页与地图面板显示来源、来源披露边界、水平精度、系统时间、更新龄及 GNSS 切换情况。位置过期时保留“上次位置”供查看，地图实时标记及汇报均停止；不会把上次来源描述为仍在实时定位。
- 卫星状态独立采样并显示可见数与参与解算数，两者不混用，15 秒后无新状态则隐藏。HarmonyOS 从 satelliteAdditionalInfo 的 USED_IN_FIX 位取得参与解算数；字段不可用时不猜测。0x31 仅在 GNSS/RTK 或融合来源且有新鲜参与解算数量时附带，网络/室内/未知来源不附带，不用默认 15 或随机波动伪装卫星数。自定义 RAW 字节仍由用户明确控制。

## 系统限制与官方依据

- 后台任务提升运行优先级，不保证对系统强制停止、权限撤销、设备关机、厂商省电或断网永久免疫。Android 用户可在“系统后台设置”检查省电限制；HarmonyOS 任务取消后会明确停止并允许回前台重新启用。
- Android [前台服务类型](https://developer.android.com/develop/background-work/services/fgs/service-types) 规定类型与权限；[Android 15 行为变化](https://developer.android.com/about/versions/15/behavior-changes-15) 说明 dataSync 的后台时长限制。
- Android [LocationManager](https://developer.android.com/reference/android/location/LocationManager) 的 Provider 接口；HarmonyOS 定位请求、来源枚举、卫星标志及定位错误签名以本机 API 26 的 `sdk/default/openharmony/ets/api/@ohos.geoLocationManager.d.ts` 为依据。Wi-Fi/LBS 的细分信息没有稳定的跨设备接口，不自行扫描无线网络来推断来源。
- HarmonyOS [后台任务管理 API](https://developer.huawei.com/consumer/cn/doc/harmonyos-references-V14/js-apis-resourceschedule-backgroundtaskmanager-V14)、[后台任务使用规范](https://developer.huawei.com/consumer/cn/doc/doccenter-app-quality/bpta-use-of-background-tasks)。原生接口签名同时按本机 API 26 SDK 的声明核对，兼容基线仍为 API 17。

## 本轮证据与待验收

- 本轮进行 Dart 静态分析及 Android APK、HarmonyOS 未签名 HAP、Windows Debug 构建；不新增或运行自动化测试。
- 不在设备上代为授予定位权限、启用定位或发送真实位置；没有实际真机、锁屏长时、断网/省电切换的成功记录。构建通过不等于后台永久在线或真实 GNSS 精度验收。
- 真机后续检查：首次/拒绝/精确位置授权、关闭定位、室内无星、通知停止、锁屏与长时间静止、后台限制开启/解除、断网/换网重连、服务端重新鉴权、Activity 重建与用户结束应用、定位模式进出及坐标一致性。
- 构建日志统一位于 `bin/Debug/mobile-runtime-{android|ohos|windows}-build.log`。鸿蒙产物未签名，真机安装仍需有效且包含目标设备的调试签名。
- 系统多源定位修订构建日志使用 `bin/Debug/system-location-{android|ohos|windows}-build.log`。需要在室内、室外、开关网络及 GNSS 遮挡环境下继续真机确认来源和切换行为，编译不代表室内定位精度已验收。
- 最终结果：Dart 静态分析无问题；Android、HarmonyOS、Windows Debug 三端构建成功。共享运行源码及原生适配与三个构建副本共 44 项 SHA-256 一致。APK 为 `bin/Debug/Flutter/android/source/build/app/outputs/flutter-apk/app-debug.apk`；HAP 为 `bin/Debug/Flutter/ohos/source/build/ohos/hap/entry-default-unsigned.hap`。

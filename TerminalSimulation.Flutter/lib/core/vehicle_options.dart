double checkedNumber(Map<String, dynamic> data, String key, double fallback) {
  final value = data[key];
  if (value == null) return fallback;
  if (value is! num || !value.toDouble().isFinite) {
    throw FormatException('$key 不是有效数值');
  }
  return value.toDouble();
}

void checkRange(double value, double min, double max, String label) {
  if (!value.isFinite || value < min || value > max) {
    throw FormatException('$label 必须为 $min–$max');
  }
}

bool checkedBool(Map<String, dynamic> data, String key, bool fallback) {
  if (!data.containsKey(key)) return fallback;
  if (data[key] is! bool) throw FormatException('$key 不是有效开关');
  return data[key] as bool;
}

Map<String, dynamic> checkedObject(dynamic value) {
  if (value is! Map) throw const FormatException('设置必须为 JSON 对象');
  return Map<String, dynamic>.from(value);
}

class TrafficRule {
  const TrafficRule({
    this.enabled = true,
    this.probabilityPercent = 60,
    this.intervalSeconds = 180,
    this.minDurationSeconds = 8,
    this.maxDurationSeconds = 25,
    this.minCycles = 1,
    this.maxCycles = 1,
    this.slowSpeedKph = 12,
  });
  final bool enabled;
  final double probabilityPercent,
      intervalSeconds,
      minDurationSeconds,
      maxDurationSeconds,
      slowSpeedKph;
  final int minCycles, maxCycles;
  factory TrafficRule.fromJson(Map<String, dynamic> data) {
    final minCycles = checkedNumber(data, 'minCycles', 1),
        maxCycles = checkedNumber(data, 'maxCycles', 1);
    if (minCycles != minCycles.roundToDouble() ||
        maxCycles != maxCycles.roundToDouble()) {
      throw const FormatException('事件轮数必须为整数');
    }
    return TrafficRule(
      enabled: checkedBool(data, 'enabled', true),
      probabilityPercent: checkedNumber(data, 'probabilityPercent', 60),
      intervalSeconds: checkedNumber(data, 'intervalSeconds', 180),
      minDurationSeconds: checkedNumber(data, 'minDurationSeconds', 8),
      maxDurationSeconds: checkedNumber(data, 'maxDurationSeconds', 25),
      minCycles: minCycles.toInt(),
      maxCycles: maxCycles.toInt(),
      slowSpeedKph: checkedNumber(data, 'slowSpeedKph', 12),
    ).validate();
  }
  TrafficRule validate() {
    checkRange(probabilityPercent, 0, 100, '触发概率');
    checkRange(intervalSeconds, 5, 3600, '机会间隔');
    checkRange(minDurationSeconds, 1, 180, '最短阶段时间');
    checkRange(maxDurationSeconds, minDurationSeconds, 180, '最长阶段时间');
    checkRange(minCycles.toDouble(), 1, 6, '最少轮数');
    checkRange(maxCycles.toDouble(), minCycles.toDouble(), 6, '最多轮数');
    checkRange(slowSpeedKph, 2, 60, '缓行速度');
    return this;
  }

  Map<String, dynamic> toJson() => {
    'enabled': enabled,
    'probabilityPercent': probabilityPercent,
    'intervalSeconds': intervalSeconds,
    'minDurationSeconds': minDurationSeconds,
    'maxDurationSeconds': maxDurationSeconds,
    'minCycles': minCycles,
    'maxCycles': maxCycles,
    'slowSpeedKph': slowSpeedKph,
  };
}

class TrafficDefinition {
  const TrafficDefinition._(
    this.id,
    this.name,
    this.group,
    this.description,
    this.hasCycles,
    this.hasSlowSpeed,
    this.defaults,
  );
  final String id, name, group, description;
  final bool hasCycles, hasSlowSpeed;
  final TrafficRule defaults;
  Map<String, dynamic> toJson() => {
    'id': id,
    'name': name,
    'group': group,
    'description': description,
    'hasCycles': hasCycles,
    'hasSlowSpeed': hasSlowSpeed,
    'defaults': defaults.toJson(),
  };
}

TrafficDefinition _define(
  String id,
  String name,
  String group,
  String description,
  double interval,
  double probability,
  double min,
  double max, {
  int cyclesMin = 1,
  int cyclesMax = 1,
  double? slow,
  bool enabled = true,
}) => TrafficDefinition._(
  id,
  name,
  group,
  description,
  cyclesMax > 1,
  slow != null,
  TrafficRule(
    enabled: enabled,
    intervalSeconds: interval,
    probabilityPercent: probability,
    minDurationSeconds: min,
    maxDurationSeconds: max,
    minCycles: cyclesMin,
    maxCycles: cyclesMax,
    slowSpeedKph: slow ?? 12,
  ),
);
final List<TrafficDefinition> trafficCatalog = List.unmodifiable([
  _define(
    'signalSingle',
    '路口红灯 · 一轮放行',
    '路口通行',
    '提前减速停车，等待红灯，绿灯后带起步反应时间平顺通行。',
    150,
    70,
    15,
    55,
  ),
  _define(
    'signalQueue',
    '路口红灯 · 多轮排队',
    '路口通行',
    '红灯等待、绿灯队列前移、再次停车，经过多轮信号周期后通过。',
    300,
    45,
    20,
    65,
    cyclesMin: 2,
    cyclesMax: 4,
    slow: 9,
  ),
  _define(
    'leftTurnYield',
    '无保护左转让行',
    '路口通行',
    '仅在左转前，停车等待对向车流间隙，再缓慢穿过路口。',
    180,
    55,
    4,
    18,
    slow: 12,
  ),
  _define(
    'crosswalkYield',
    '人行横道礼让',
    '路口通行',
    '在虚拟过街点前停车，等待行人通过，再低速起步。',
    240,
    50,
    5,
    20,
    slow: 10,
  ),
  _define(
    'stopGo',
    '拥堵车流 · 走走停停',
    '车流变化',
    '跟随队列蠕行、停车、反应起步，反复推进后逐步恢复。',
    300,
    55,
    4,
    14,
    cyclesMin: 3,
    cyclesMax: 6,
    slow: 10,
  ),
  _define(
    'phantomJam',
    '幽灵堵车 · 连续减速波',
    '车流变化',
    '没有固定障碍的车流压缩波，减速、短暂恢复、再次减速，最终消散。',
    360,
    45,
    8,
    22,
    cyclesMin: 2,
    cyclesMax: 4,
    slow: 14,
  ),
  _define(
    'slowLeader',
    '慢车跟行',
    '车流变化',
    '在一段道路上受慢车影响，目标速度连续小幅变化，随后恢复巡航。',
    240,
    60,
    20,
    65,
    slow: 24,
  ),
  _define(
    'cutIn',
    '邻车切入 · 拉开车距',
    '车流变化',
    '邻车切入后先收油减速，保持安全间隔，再平顺恢复；不改变轨迹。',
    180,
    45,
    3,
    8,
    slow: 20,
  ),
  _define(
    'mergeAlternating',
    '汇入口交替通行',
    '车流变化',
    '接近合流队尾减速，停车让行、低速前移，再跟随车流汇入。',
    300,
    45,
    3,
    10,
    cyclesMin: 1,
    cyclesMax: 3,
    slow: 12,
  ),
  _define(
    'busObstruction',
    '公交停靠 / 临时占道',
    '道路干扰',
    '前方车道临时受阻，停车等待障碍解除，再缓慢通过。',
    360,
    35,
    8,
    30,
    slow: 12,
  ),
  _define(
    'roadworks',
    '施工窄道 · 交替放行',
    '道路干扰',
    '在施工路段入口停车等候，获准后低速通过受限路段，再恢复。',
    600,
    35,
    12,
    45,
    slow: 15,
  ),
  _define(
    'emergencyYield',
    '应急车辆避让',
    '道路干扰',
    '平顺减速至停车，让应急车辆通过，稍作观察后继续行驶。',
    900,
    20,
    5,
    18,
    slow: 10,
  ),
  _define(
    'speedBump',
    '减速带 / 破损路面',
    '道路干扰',
    '提前收油减速，低速通过短距离颠簸路段，离开后恢复速度。',
    300,
    45,
    3,
    7,
    slow: 10,
  ),
  _define(
    'railCrossing',
    '铁路道口等待',
    '道路干扰',
    '在虚拟道口前停车，等待栏杆开启后低速通过；默认关闭。',
    1200,
    20,
    35,
    100,
    slow: 12,
    enabled: false,
  ),
]);

class TrafficOptions {
  TrafficOptions({
    this.minimumGapSeconds = 30,
    this.allowVirtualSignalsOnStraight = true,
    this.virtualSignalSpacingMeters = 450,
    Map<String, TrafficRule>? events,
  }) : events = Map.unmodifiable({
         for (final item in trafficCatalog) item.id: item.defaults,
         ...?events,
       }) {
    checkRange(minimumGapSeconds, 5, 600, '事件最小间隔');
    checkRange(virtualSignalSpacingMeters, 100, 5000, '虚拟路口间距');
    final ids = trafficCatalog.map((e) => e.id).toSet();
    for (final entry in this.events.entries) {
      if (!ids.contains(entry.key)) {
        throw FormatException('未知交通事件：${entry.key}');
      }
      entry.value.validate();
    }
  }
  final double minimumGapSeconds, virtualSignalSpacingMeters;
  final bool allowVirtualSignalsOnStraight;
  final Map<String, TrafficRule> events;
  factory TrafficOptions.fromJson(Map<String, dynamic> data) => TrafficOptions(
    minimumGapSeconds: checkedNumber(data, 'minimumGapSeconds', 30),
    virtualSignalSpacingMeters: checkedNumber(
      data,
      'virtualSignalSpacingMeters',
      450,
    ),
    allowVirtualSignalsOnStraight: checkedBool(
      data,
      'allowVirtualSignalsOnStraight',
      true,
    ),
    events: data.containsKey('events')
        ? checkedObject(data['events']).map(
            (key, value) =>
                MapEntry(key, TrafficRule.fromJson(checkedObject(value))),
          )
        : null,
  );
  Map<String, dynamic> toJson() => {
    'minimumGapSeconds': minimumGapSeconds,
    'allowVirtualSignalsOnStraight': allowVirtualSignalsOnStraight,
    'virtualSignalSpacingMeters': virtualSignalSpacingMeters,
    'events': events.map((key, value) => MapEntry(key, value.toJson())),
  };
}

class VehicleOptions {
  VehicleOptions({
    this.enabled = false,
    this.randomEventsEnabled = false,
    this.preset = 'small-auto',
    this.maxSpeedKph = 100,
    this.averageSpeedKph = 50,
    this.drivingStyle = 'normal',
    this.maxAcceleration = 2.6,
    this.brakeDeceleration = 6,
    this.transmission = 'automatic',
    this.powertrain = 'petrol',
    List<double>? gearRatios,
    this.finalDrive = 4.1,
    this.wheelRadius = .3,
    this.idleRpm = 800,
    this.redlineRpm = 6500,
    this.upshiftRpm = 3200,
    this.downshiftRpm = 1200,
    this.shiftDuration = .45,
    TrafficOptions? trafficEvents,
  }) : gearRatios = List<double>.unmodifiable(
         ['cvt', 'singleSpeed'].contains(transmission)
             ? <double>[1.0]
             : gearRatios ?? <double>[3.4, 2.1, 1.4, 1, .8, .64],
       ),
       trafficEvents = trafficEvents ?? TrafficOptions() {
    validate();
  }
  final bool enabled, randomEventsEnabled;
  final String preset, drivingStyle, transmission, powertrain;
  final double maxSpeedKph,
      averageSpeedKph,
      maxAcceleration,
      brakeDeceleration,
      finalDrive,
      wheelRadius,
      idleRpm,
      redlineRpm,
      upshiftRpm,
      downshiftRpm,
      shiftDuration;
  final List<double> gearRatios;
  final TrafficOptions trafficEvents;
  void validate() {
    checkRange(maxSpeedKph, 5, 300, '最大速度');
    checkRange(averageSpeedKph, 1, maxSpeedKph, '平均速度');
    checkRange(maxAcceleration, .1, 12, '最大加速度');
    checkRange(brakeDeceleration, .5, 12, '最大刹车减速度');
    checkRange(finalDrive, 1, 15, '主减速比');
    checkRange(wheelRadius, .2, 1, '车轮半径');
    checkRange(idleRpm, 400, 2000, '怠速转速');
    checkRange(redlineRpm, 1500, 16000, '红线转速');
    checkRange(downshiftRpm, idleRpm, redlineRpm - 300, '降挡转速');
    checkRange(upshiftRpm, downshiftRpm + 200, redlineRpm, '升挡转速');
    checkRange(shiftDuration, .05, 3, '换挡时间');
    if (!['calm', 'normal', 'sport'].contains(drivingStyle)) {
      throw const FormatException('驾驶风格无效');
    }
    if (![
      'manual',
      'automatic',
      'cvt',
      'dct',
      'singleSpeed',
    ].contains(transmission)) {
      throw const FormatException('变速箱类型无效');
    }
    if (!['petrol', 'diesel', 'electric', 'hybrid'].contains(powertrain)) {
      throw const FormatException('动力类型无效');
    }
    if (gearRatios.isEmpty || gearRatios.length > 12) {
      throw const FormatException('挡位数量必须为 1–12');
    }
    for (var i = 0; i < gearRatios.length; i++) {
      checkRange(gearRatios[i], .1, 20, '${i + 1} 挡齿比');
      if (i > 0 && gearRatios[i] >= gearRatios[i - 1]) {
        throw const FormatException('挡位齿比必须逐挡递减');
      }
    }
  }

  factory VehicleOptions.fromJson(Map<String, dynamic> data) {
    List<double>? ratios;
    if (data.containsKey('gearRatios')) {
      if (data['gearRatios'] is! List) throw const FormatException('挡位齿比无效');
      ratios = (data['gearRatios'] as List).map((v) {
        if (v is! num || !v.toDouble().isFinite) {
          throw const FormatException('挡位齿比无效');
        }
        return v.toDouble();
      }).toList();
    }
    return VehicleOptions(
      enabled: checkedBool(data, 'enabled', false),
      randomEventsEnabled: checkedBool(data, 'randomEventsEnabled', false),
      preset: data['preset'] as String? ?? 'small-auto',
      drivingStyle: data['drivingStyle'] as String? ?? 'normal',
      transmission: data['transmission'] as String? ?? 'automatic',
      powertrain: data['powertrain'] as String? ?? 'petrol',
      gearRatios: ratios,
      maxSpeedKph: checkedNumber(data, 'maxSpeedKph', 100),
      averageSpeedKph: checkedNumber(data, 'averageSpeedKph', 50),
      maxAcceleration: checkedNumber(data, 'maxAcceleration', 2.6),
      brakeDeceleration: checkedNumber(data, 'brakeDeceleration', 6),
      finalDrive: checkedNumber(data, 'finalDrive', 4.1),
      wheelRadius: checkedNumber(data, 'wheelRadius', .3),
      idleRpm: checkedNumber(data, 'idleRpm', 800),
      redlineRpm: checkedNumber(data, 'redlineRpm', 6500),
      upshiftRpm: checkedNumber(data, 'upshiftRpm', 3200),
      downshiftRpm: checkedNumber(data, 'downshiftRpm', 1200),
      shiftDuration: checkedNumber(data, 'shiftDuration', .45),
      trafficEvents: data.containsKey('trafficEvents')
          ? TrafficOptions.fromJson(checkedObject(data['trafficEvents']))
          : null,
    );
  }
  Map<String, dynamic> toJson() => {
    'enabled': enabled,
    'randomEventsEnabled': randomEventsEnabled,
    'preset': preset,
    'maxSpeedKph': maxSpeedKph,
    'averageSpeedKph': averageSpeedKph,
    'drivingStyle': drivingStyle,
    'maxAcceleration': maxAcceleration,
    'brakeDeceleration': brakeDeceleration,
    'transmission': transmission,
    'powertrain': powertrain,
    'gearRatios': gearRatios.toList(),
    'finalDrive': finalDrive,
    'wheelRadius': wheelRadius,
    'idleRpm': idleRpm,
    'redlineRpm': redlineRpm,
    'upshiftRpm': upshiftRpm,
    'downshiftRpm': downshiftRpm,
    'shiftDuration': shiftDuration,
    'trafficEvents': trafficEvents.toJson(),
  };
  static final Map<String, VehicleOptions> presets = Map.unmodifiable({
    'small-auto': VehicleOptions(),
    'small-manual': VehicleOptions(
      preset: 'small-manual',
      transmission: 'manual',
      maxAcceleration: 2.4,
      gearRatios: [3.6, 2.1, 1.36, 1, .82],
      upshiftRpm: 3000,
      downshiftRpm: 1100,
      shiftDuration: .65,
    ),
    'electric': VehicleOptions(
      preset: 'electric',
      transmission: 'singleSpeed',
      powertrain: 'electric',
      maxSpeedKph: 140,
      averageSpeedKph: 60,
      maxAcceleration: 4.2,
      gearRatios: [1],
      finalDrive: 9,
      wheelRadius: .31,
      redlineRpm: 14000,
      upshiftRpm: 12000,
      downshiftRpm: 1800,
      shiftDuration: .05,
    ),
    'manual-truck': VehicleOptions(
      preset: 'manual-truck',
      transmission: 'manual',
      powertrain: 'diesel',
      maxSpeedKph: 65,
      averageSpeedKph: 32,
      drivingStyle: 'calm',
      maxAcceleration: .7,
      brakeDeceleration: 4.2,
      gearRatios: [12.2, 9.3, 7.2, 5.4, 4.1, 3.1, 2.3, 1.75, 1.3, 1],
      finalDrive: 4.8,
      wheelRadius: .5,
      idleRpm: 650,
      redlineRpm: 2800,
      upshiftRpm: 2100,
      downshiftRpm: 900,
      shiftDuration: .85,
    ),
  });
}

import 'dart:convert';
import 'package:flutter/material.dart';
import '../core/vehicle_options.dart';
import 'components.dart';

Future<VehicleOptions?> editMapVehicle(
  BuildContext context,
  VehicleOptions value,
) => showDialog<VehicleOptions>(
  context: context,
  builder: (_) => _VehicleSettings(value: value),
);

class _VehicleSettings extends StatefulWidget {
  const _VehicleSettings({required this.value});
  final VehicleOptions value;
  @override
  State<_VehicleSettings> createState() => _VehicleSettingsState();
}

class _VehicleSettingsState extends State<_VehicleSettings> {
  final form = GlobalKey<FormState>();
  final fields = <String, TextEditingController>{};
  late Map<String, dynamic> data;
  String? error;
  int revision = 0;
  static const presetNames = {
    'small-auto': '小型自动挡',
    'small-manual': '小型手动挡',
    'electric': '电动车',
    'manual-truck': '手动货车',
  };
  @override
  void initState() {
    super.initState();
    data = jsonDecode(jsonEncode(widget.value.toJson()));
  }

  @override
  void dispose() {
    for (final c in fields.values) {
      c.dispose();
    }
    super.dispose();
  }

  Widget number(
    Map<String, dynamic> target,
    String key,
    String label, {
    String prefix = '',
    bool integer = false,
  }) {
    final id = '$prefix$key';
    final c = fields.putIfAbsent(
      id,
      () => TextEditingController(text: '${target[key]}'),
    );
    return AppTextField(
      controller: c,
      keyboardType: const TextInputType.numberWithOptions(decimal: true),
      decoration: InputDecoration(labelText: label),
      validator: (v) {
        final n = double.tryParse(v ?? '');
        return n == null || !n.isFinite || (integer && n != n.roundToDouble())
            ? '请输入有效${integer ? '整数' : '数值'}'
            : null;
      },
      onChanged: (v) {
        final n = num.tryParse(v);
        target[key] = integer && n != null && n == n.roundToDouble()
            ? n.toInt()
            : n ?? v;
      },
    );
  }

  Widget choice(String key, String label, Map<String, String> options) =>
      DropdownButtonFormField<String>(
        key: ValueKey('$key-$revision-${data[key]}'),
        initialValue: options.containsKey(data[key])
            ? data[key]
            : options.keys.first,
        isExpanded: true,
        decoration: InputDecoration(labelText: label),
        items: options.entries
            .map(
              (e) => DropdownMenuItem(
                value: e.key,
                child: Text(e.value, overflow: TextOverflow.ellipsis),
              ),
            )
            .toList(),
        onChanged: (v) {
          if (v != null) setState(() => data[key] = v);
        },
      );
  void preset(String value) {
    final previous = data;
    setState(() {
      data = {
        ...VehicleOptions.presets[value]!.toJson(),
        'enabled': previous['enabled'],
        'randomEventsEnabled': previous['randomEventsEnabled'],
        'trafficEvents': previous['trafficEvents'],
      };
      for (final entry in fields.entries) {
        if (!entry.key.contains('.') && data.containsKey(entry.key)) {
          entry.value.text = entry.key == 'gearRatios'
              ? (data[entry.key] as List).join(', ')
              : '${data[entry.key]}';
        }
      }
      ++revision;
      error = null;
    });
  }

  void save() {
    if (!form.currentState!.validate()) return;
    try {
      final ratios =
          fields['gearRatios']?.text ?? (data['gearRatios'] as List).join(',');
      data['gearRatios'] = ratios
          .split(RegExp(r'[,，\s]+'))
          .where((v) => v.isNotEmpty)
          .map(double.parse)
          .toList();
      final options = VehicleOptions.fromJson(data);
      Navigator.pop(context, options);
    } catch (e) {
      setState(
        () => error = e is FormatException ? e.message : '车辆参数无效，请检查挡位和数值范围。',
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final traffic = data['trafficEvents'] as Map<String, dynamic>;
    final events = traffic['events'] as Map<String, dynamic>;
    return Dialog(
      insetPadding: const EdgeInsets.all(12),
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 760),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 12, 8, 8),
              child: Row(
                children: [
                  Expanded(
                    child: Text(
                      '驾驶设置',
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                  ),
                  IconButton(
                    tooltip: '关闭',
                    onPressed: () => Navigator.pop(context),
                    icon: const Icon(Icons.close),
                  ),
                ],
              ),
            ),
            const Divider(height: 1),
            Flexible(
              child: SingleChildScrollView(
                padding: const EdgeInsets.all(20),
                child: Form(
                  key: form,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      SwitchListTile.adaptive(
                        contentPadding: EdgeInsets.zero,
                        title: const Text('真实驾驶模拟'),
                        subtitle: const Text('使用车辆动力、弯道限速与终点停车'),
                        value: data['enabled'] as bool,
                        onChanged: (v) => setState(() => data['enabled'] = v),
                      ),
                      DropdownButtonFormField<String>(
                        key: ValueKey('preset-$revision'),
                        initialValue:
                            VehicleOptions.presets.containsKey(data['preset'])
                            ? data['preset']
                            : 'small-auto',
                        isExpanded: true,
                        decoration: const InputDecoration(labelText: '车辆预设'),
                        items: presetNames.entries
                            .map(
                              (e) => DropdownMenuItem(
                                value: e.key,
                                child: Text(e.value),
                              ),
                            )
                            .toList(),
                        onChanged: (v) {
                          if (v != null) preset(v);
                        },
                      ),
                      const SizedBox(height: 20),
                      FieldGrid(
                        minWidth: 220,
                        children: [
                          number(data, 'maxSpeedKph', '最高速度 (km/h)'),
                          number(data, 'averageSpeedKph', '期望巡航速度 (km/h)'),
                          choice('drivingStyle', '驾驶风格', {
                            'calm': '平稳',
                            'normal': '标准',
                            'sport': '积极',
                          }),
                          choice('transmission', '变速箱', {
                            'manual': '手动挡',
                            'automatic': '自动挡',
                            'cvt': 'CVT',
                            'dct': '双离合',
                            'singleSpeed': '单速',
                          }),
                          choice('powertrain', '动力', {
                            'petrol': '汽油',
                            'diesel': '柴油',
                            'electric': '电动',
                            'hybrid': '混动',
                          }),
                          number(data, 'maxAcceleration', '最大加速度 (m/s²)'),
                          number(data, 'brakeDeceleration', '制动减速度 (m/s²)'),
                        ],
                      ),
                      ExpansionTile(
                        tilePadding: EdgeInsets.zero,
                        title: const Text('传动与换挡参数'),
                        children: [
                          FieldGrid(
                            minWidth: 220,
                            children: [
                              for (final e in {
                                'finalDrive': '主减速比',
                                'wheelRadius': '车轮半径 (m)',
                                'idleRpm': '怠速 (rpm)',
                                'redlineRpm': '红线 (rpm)',
                                'upshiftRpm': '升挡转速',
                                'downshiftRpm': '降挡转速',
                                'shiftDuration': '换挡时间 (s)',
                              }.entries)
                                number(data, e.key, e.value),
                            ],
                          ),
                          const SizedBox(height: 16),
                          AppTextField(
                            controller: fields.putIfAbsent(
                              'gearRatios',
                              () => TextEditingController(
                                text: (data['gearRatios'] as List).join(', '),
                              ),
                            ),
                            decoration: const InputDecoration(
                              labelText: '各挡齿比',
                              helperText: '用逗号分隔，按挡位递减；最多 12 挡',
                            ),
                            maxLines: 2,
                          ),
                          const SizedBox(height: 12),
                        ],
                      ),
                      SwitchListTile.adaptive(
                        contentPadding: EdgeInsets.zero,
                        title: const Text('虚拟交通情景'),
                        subtitle: const Text('仅自动驾驶操作时触发，不代表实时路况'),
                        value: data['randomEventsEnabled'] as bool,
                        onChanged: (v) =>
                            setState(() => data['randomEventsEnabled'] = v),
                      ),
                      ExpansionTile(
                        tilePadding: EdgeInsets.zero,
                        title: const Text('交通情景参数 · 14 类'),
                        children: [
                          FieldGrid(
                            minWidth: 220,
                            children: [
                              number(
                                traffic,
                                'minimumGapSeconds',
                                '事件最小间隔 (s)',
                                prefix: 'traffic.',
                              ),
                              number(
                                traffic,
                                'virtualSignalSpacingMeters',
                                '虚拟路口间距 (m)',
                                prefix: 'traffic.',
                              ),
                            ],
                          ),
                          SwitchListTile.adaptive(
                            contentPadding: EdgeInsets.zero,
                            title: const Text('直路允许虚拟信号灯'),
                            value:
                                traffic['allowVirtualSignalsOnStraight']
                                    as bool,
                            onChanged: (v) => setState(
                              () =>
                                  traffic['allowVirtualSignalsOnStraight'] = v,
                            ),
                          ),
                          Wrap(
                            spacing: 8,
                            children: [
                              TextButton(
                                onPressed: () => setState(() {
                                  for (final rule in events.values) {
                                    (rule as Map)['enabled'] = true;
                                  }
                                }),
                                child: const Text('全部启用'),
                              ),
                              TextButton(
                                onPressed: () => setState(() {
                                  for (final rule in events.values) {
                                    (rule as Map)['enabled'] = false;
                                  }
                                }),
                                child: const Text('全部关闭'),
                              ),
                            ],
                          ),
                          for (final definition in trafficCatalog)
                            Builder(
                              builder: (context) {
                                final rule =
                                    events[definition.id]
                                        as Map<String, dynamic>;
                                return ExpansionTile(
                                  title: Text(definition.name),
                                  subtitle: Text(definition.group),
                                  leading: Checkbox(
                                    value: rule['enabled'] as bool,
                                    onChanged: (v) => setState(
                                      () => rule['enabled'] = v ?? false,
                                    ),
                                  ),
                                  children: [
                                    Padding(
                                      padding: const EdgeInsets.only(
                                        bottom: 12,
                                      ),
                                      child: Text(definition.description),
                                    ),
                                    FieldGrid(
                                      minWidth: 210,
                                      children: [
                                        number(
                                          rule,
                                          'probabilityPercent',
                                          '机会概率 (%)',
                                          prefix: '${definition.id}.',
                                        ),
                                        number(
                                          rule,
                                          'intervalSeconds',
                                          '平均机会间隔 (s)',
                                          prefix: '${definition.id}.',
                                        ),
                                        number(
                                          rule,
                                          'minDurationSeconds',
                                          '最短阶段时间 (s)',
                                          prefix: '${definition.id}.',
                                        ),
                                        number(
                                          rule,
                                          'maxDurationSeconds',
                                          '最长阶段时间 (s)',
                                          prefix: '${definition.id}.',
                                        ),
                                        if (definition.hasCycles) ...[
                                          number(
                                            rule,
                                            'minCycles',
                                            '最少轮数',
                                            prefix: '${definition.id}.',
                                            integer: true,
                                          ),
                                          number(
                                            rule,
                                            'maxCycles',
                                            '最多轮数',
                                            prefix: '${definition.id}.',
                                            integer: true,
                                          ),
                                        ],
                                        if (definition.hasSlowSpeed)
                                          number(
                                            rule,
                                            'slowSpeedKph',
                                            '缓行速度 (km/h)',
                                            prefix: '${definition.id}.',
                                          ),
                                      ],
                                    ),
                                    const SizedBox(height: 16),
                                  ],
                                );
                              },
                            ),
                        ],
                      ),
                      if (error != null)
                        Padding(
                          padding: const EdgeInsets.only(top: 12),
                          child: Text(
                            error!,
                            style: TextStyle(
                              color: Theme.of(context).colorScheme.error,
                            ),
                          ),
                        ),
                    ],
                  ),
                ),
              ),
            ),
            const Divider(height: 1),
            Padding(
              padding: const EdgeInsets.all(12),
              child: OverflowBar(
                spacing: 8,
                children: [
                  TextButton(
                    onPressed: () => Navigator.pop(context),
                    child: const Text('取消'),
                  ),
                  FilledButton(onPressed: save, child: const Text('保存设置')),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

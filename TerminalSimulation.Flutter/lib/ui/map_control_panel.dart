import 'dart:async';
import 'package:flutter/material.dart';
import '../core/geo.dart';
import '../core/route.dart';
import '../core/vehicle_driver.dart';
import '../core/vehicle_options.dart';
import '../services/map_workspace_controller.dart';
import 'components.dart';
import 'map_vehicle_settings.dart';

class MapControlPanel extends StatefulWidget {
  const MapControlPanel({
    super.key,
    required this.controller,
    required this.openRoute,
    required this.saveRoute,
    required this.onToggle,
    this.onDrag,
    this.onDragEnd,
    this.collapsed = false,
  });
  final MapWorkspaceController controller;
  final Future<RouteDocument?> Function() openRoute;
  final Future<bool> Function(RouteDocument) saveRoute;
  final VoidCallback onToggle;
  final ValueChanged<double>? onDrag;
  final VoidCallback? onDragEnd;
  final bool collapsed;
  @override
  State<MapControlPanel> createState() => _MapControlPanelState();
}

class _MapControlPanelState extends State<MapControlPanel> {
  final search = TextEditingController(), speed = TextEditingController();
  final speedFocus = FocusNode();
  Timer? debounce;
  String _lastQuery = '';
  bool _composing = false;
  void queryValueChanged() {
    final value = search.value;
    final composing = value.composing.isValid && !value.composing.isCollapsed;
    if (composing) {
      _composing = true;
      debounce?.cancel();
      return;
    }
    if (value.text != _lastQuery || _composing) {
      _lastQuery = value.text;
      _composing = false;
      queryChanged(value.text);
    }
  }

  bool fileBusy = false, automatic = true;
  int gear = 1;
  double throttle = 0, brake = 0;
  MapWorkspaceController get c => widget.controller;
  @override
  void initState() {
    super.initState();
    speed.text = '${c.speed}';
    search.addListener(queryValueChanged);
  }

  @override
  void dispose() {
    debounce?.cancel();
    search.removeListener(queryValueChanged);
    search.dispose();
    speed.dispose();
    speedFocus.dispose();
    super.dispose();
  }

  Future<void> run(Future<void> Function() operation) async {
    try {
      await operation();
    } catch (e) {
      if (mounted) c.report(e is FormatException ? e.message : '$e');
    }
  }

  Future<void> files(bool save) async {
    if (fileBusy) return;
    setState(() => fileBusy = true);
    await run(() async {
      if (save) {
        final ok = await widget.saveRoute(c.document());
        if (ok && mounted) {
          c.message = '路线已保存';
          c.changed(geometry: false);
        }
      } else {
        final route = await widget.openRoute();
        if (route != null && mounted) await c.load(route);
      }
    });
    if (mounted) setState(() => fileBusy = false);
  }

  void queryChanged(String text) {
    debounce?.cancel();
    if (search.value.composing.isValid && !search.value.composing.isCollapsed) {
      return;
    }
    debounce = Timer(const Duration(milliseconds: 400), () {
      if (mounted) unawaited(c.search(text, suggest: true));
    });
  }

  void submitSearch(String value) {
    debounce?.cancel();
    FocusScope.of(context).unfocus();
    unawaited(c.search(value));
  }

  Future<void> changeSystem(CoordinateSystem value) async {
    if (value == c.system) return;
    if (c.points.isEmpty && c.start == null && c.end == null) {
      c.setSystem(value, convert: false);
      return;
    }
    final convert = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('切换坐标系'),
        content: const Text('已有点位可转换坐标以保持地图位置，也可以保留数值、直接改变坐标系解释。'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('取消'),
          ),
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: const Text('保留数值'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('转换并保持位置'),
          ),
        ],
      ),
    );
    if (convert != null && mounted) c.setSystem(value, convert: convert);
  }

  Future<String?> askName(String title, String value) async {
    final field = TextEditingController(text: value);
    try {
      return await showDialog<String>(
        context: context,
        builder: (ctx) => AlertDialog(
          title: Text(title),
          content: TextField(
            controller: field,
            autofocus: true,
            maxLength: 80,
            decoration: const InputDecoration(labelText: '名称'),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(ctx),
              child: const Text('取消'),
            ),
            FilledButton(
              onPressed: () {
                final text = field.text.trim();
                if (text.isNotEmpty) Navigator.pop(ctx, text);
              },
              child: const Text('保存'),
            ),
          ],
        ),
      );
    } finally {
      WidgetsBinding.instance.addPostFrameCallback((_) => field.dispose());
    }
  }

  Future<void> history({bool routes = true}) async {
    await c.refreshHistory();
    if (!mounted) return;
    await showDialog<void>(
      context: context,
      builder: (ctx) => Dialog(
        insetPadding: const EdgeInsets.all(12),
        child: SizedBox(
          width: 600,
          child: AnimatedBuilder(
            animation: c,
            builder: (context, _) => Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Padding(
                  padding: const EdgeInsets.fromLTRB(20, 10, 8, 8),
                  child: Row(
                    children: [
                      Expanded(
                        child: Text(
                          routes ? '轨迹记录' : '搜索记录',
                          style: Theme.of(ctx).textTheme.titleLarge,
                        ),
                      ),
                      IconButton(
                        tooltip: '关闭',
                        onPressed: () => Navigator.pop(ctx),
                        icon: const Icon(Icons.close),
                      ),
                    ],
                  ),
                ),
                const Divider(height: 1),
                Flexible(
                  child: (routes ? c.records : c.searches).isEmpty
                      ? const Padding(
                          padding: EdgeInsets.all(32),
                          child: Text('还没有记录'),
                        )
                      : ListView(
                          shrinkWrap: true,
                          children: [
                            for (final item in routes ? c.records : c.searches)
                              ListTile(
                                title: Text(
                                  '${item[routes ? 'name' : 'query']}',
                                  maxLines: 2,
                                  overflow: TextOverflow.ellipsis,
                                ),
                                subtitle: routes
                                    ? Text(
                                        '${(item['route']['points'] as List).length} 个点 · ${coordinateLabel('${item['route']['coordinateSystem']}')}',
                                      )
                                    : null,
                                onTap: routes && !c.editable
                                    ? null
                                    : () {
                                        Navigator.pop(ctx);
                                        if (routes) {
                                          unawaited(
                                            run(
                                              () => c.load(
                                                RouteDocument.fromJson(
                                                  Map<String, dynamic>.from(
                                                    item['route'],
                                                  ),
                                                ),
                                              ),
                                            ),
                                          );
                                        } else {
                                          search.text = '${item['query']}';
                                          submitSearch(search.text);
                                        }
                                      },
                                trailing: PopupMenuButton<String>(
                                  tooltip: '记录操作',
                                  onSelected: (action) async {
                                    await run(() async {
                                      if (action == 'rename') {
                                        final name = await askName(
                                          '重命名轨迹',
                                          '${item['name']}',
                                        );
                                        if (name != null) {
                                          await c.history!.rename(
                                            '${item['id']}',
                                            name,
                                          );
                                        }
                                      } else {
                                        if (!mounted) return;
                                        if (await confirmAction(
                                          this.context,
                                          title: '删除记录',
                                          message: '删除这条本机记录？',
                                        )) {
                                          await c.history!.delete(
                                            routes ? 'routes' : 'searches',
                                            '${item['id']}',
                                          );
                                        }
                                      }
                                      await c.refreshHistory();
                                    });
                                  },
                                  itemBuilder: (_) => [
                                    if (routes)
                                      const PopupMenuItem(
                                        value: 'rename',
                                        child: Text('重命名'),
                                      ),
                                    const PopupMenuItem(
                                      value: 'delete',
                                      child: Text('删除'),
                                    ),
                                  ],
                                ),
                              ),
                          ],
                        ),
                ),
                const Divider(height: 1),
                Padding(
                  padding: const EdgeInsets.all(12),
                  child: OverflowBar(
                    spacing: 8,
                    children: [
                      TextButton(
                        onPressed: (routes ? c.records : c.searches).isEmpty
                            ? null
                            : () async {
                                if (await confirmAction(
                                  ctx,
                                  title: '清空记录',
                                  message: '清空这一类本机记录？',
                                )) {
                                  await run(() async {
                                    await c.history!.clear(
                                      routes ? 'routes' : 'searches',
                                    );
                                    await c.refreshHistory();
                                  });
                                }
                              },
                        child: const Text('清空记录'),
                      ),
                      FilledButton.tonal(
                        onPressed: () => Navigator.pop(ctx),
                        child: const Text('完成'),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Future<void> mapSettings() async {
    var city = c.defaultLocation, style = c.mapStyle, path = c.pathStyle;
    await showDialog<void>(
      context: context,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, update) => AlertDialog(
          title: const Text('地图设置'),
          content: SizedBox(
            width: 420,
            child: SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  DropdownButtonFormField<String>(
                    isExpanded: true,
                    initialValue:
                        [
                          'auto',
                          ...MapWorkspaceController.cities.keys,
                        ].contains(city)
                        ? city
                        : 'auto',
                    decoration: const InputDecoration(labelText: '默认位置'),
                    items: [
                      const DropdownMenuItem(
                        value: 'auto',
                        child: Text('自动 · 网络近似位置'),
                      ),
                      for (final entry in MapWorkspaceController.cities.entries)
                        DropdownMenuItem(
                          value: entry.key,
                          child: Text(entry.value.$1),
                        ),
                    ],
                    onChanged: (v) => update(() => city = v!),
                  ),
                  const SizedBox(height: 20),
                  DropdownButtonFormField<String>(
                    isExpanded: true,
                    initialValue: style,
                    decoration: const InputDecoration(labelText: '地图样式'),
                    items: const [
                      DropdownMenuItem(value: 'default', child: Text('默认')),
                      DropdownMenuItem(value: 'style1', child: Text('墨渊')),
                    ],
                    onChanged: (v) => update(() => style = v!),
                  ),
                  const SizedBox(height: 20),
                  DropdownButtonFormField<String>(
                    isExpanded: true,
                    initialValue:
                        const [
                          'theme-neon-blue',
                          'theme-neon-purple',
                          'theme-solid-red',
                          'theme-solid-orange',
                        ].contains(path)
                        ? path
                        : 'theme-neon-blue',
                    decoration: const InputDecoration(labelText: '路线颜色'),
                    items: const [
                      DropdownMenuItem(
                        value: 'theme-neon-blue',
                        child: Text('导航蓝'),
                      ),
                      DropdownMenuItem(
                        value: 'theme-neon-purple',
                        child: Text('霓虹紫'),
                      ),
                      DropdownMenuItem(
                        value: 'theme-solid-red',
                        child: Text('警示红'),
                      ),
                      DropdownMenuItem(
                        value: 'theme-solid-orange',
                        child: Text('活力橙'),
                      ),
                    ],
                    onChanged: (v) => update(() => path = v!),
                  ),
                ],
              ),
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(ctx),
              child: const Text('取消'),
            ),
            FilledButton(
              onPressed: () {
                Navigator.pop(ctx);
                unawaited(
                  run(() => c.settings(city: city, style: style, path: path)),
                );
              },
              child: const Text('保存并应用'),
            ),
          ],
        ),
      ),
    );
  }

  void controls() {
    c.model.setVehicleControls(
      VehicleControls(
        automatic: automatic,
        gear: gear,
        throttle: throttle,
        brake: brake,
      ),
    );
    setState(() {});
  }

  static String coordinateLabel(String value) => switch (value) {
    'gcj02' => 'GCJ-02',
    'wgs84' => 'WGS-84',
    _ => 'BD-09',
  };
  String pointLabel(GeoPoint? point, String name) => point == null
      ? '点击地图或搜索地点'
      : name.isNotEmpty
      ? name
      : '${point.lat.toStringAsFixed(5)}, ${point.lng.toStringAsFixed(5)}';
  Widget endpoint(bool first) => Row(
    children: [
      Icon(
        Icons.circle,
        size: 10,
        color: first ? Colors.teal : Colors.deepOrange,
      ),
      const SizedBox(width: 12),
      Expanded(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              first ? '起点' : '终点',
              style: Theme.of(context).textTheme.labelSmall,
            ),
            Text(
              pointLabel(
                first ? c.start : c.end,
                first ? c.startName : c.endName,
              ),
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
            ),
          ],
        ),
      ),
      IconButton(
        tooltip: first ? '清除起点' : '清除终点',
        onPressed: c.editable ? () => c.removeEndpoint(first) : null,
        icon: const Icon(Icons.close, size: 18),
      ),
    ],
  );
  Widget fields() {
    final effectiveSpeed = c.vehicle.enabled
        ? c.vehicle.averageSpeedKph
        : c.speed;
    if (!speedFocus.hasFocus && double.tryParse(speed.text) != effectiveSpeed) {
      speed.text = effectiveSpeed.toStringAsFixed(
        effectiveSpeed % 1 == 0 ? 0 : 1,
      );
    }
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Expanded(
              child: Text(
                '模拟参数',
                style: Theme.of(context).textTheme.titleSmall,
              ),
            ),
            TextButton.icon(
              onPressed: c.editable
                  ? () async {
                      final value = await editMapVehicle(context, c.vehicle);
                      if (value != null && mounted) {
                        await run(() => c.saveVehicle(value));
                      }
                    }
                  : null,
              icon: const Icon(Icons.tune, size: 18),
              label: const Text('驾驶设置'),
            ),
          ],
        ),
        const SizedBox(height: 8),
        LayoutBuilder(
          builder: (context, box) {
            final scaled = MediaQuery.textScalerOf(context).scale(14) / 14;
            final two = box.maxWidth >= (160 * scaled) * 2 + 12;
            final children = <Widget>[
              TextFormField(
                controller: speed,
                focusNode: speedFocus,
                readOnly: c.vehicle.enabled,
                keyboardType: const TextInputType.numberWithOptions(
                  decimal: true,
                ),
                autovalidateMode: AutovalidateMode.onUserInteraction,
                decoration: InputDecoration(
                  labelText: c.vehicle.enabled ? '巡航速度' : '模拟速度',
                  suffixText: 'km/h',
                  isDense: true,
                  contentPadding: const EdgeInsets.symmetric(
                    horizontal: 16,
                    vertical: 18,
                  ),
                ),
                validator: (v) {
                  if (c.vehicle.enabled) return null;
                  final n = double.tryParse(v ?? '');
                  return n == null || !n.isFinite || n < 1 || n > 300
                      ? '范围 1–300'
                      : null;
                },
                onChanged: (v) {
                  final n = double.tryParse(v);
                  if (n != null && n.isFinite && n >= 1 && n <= 300) {
                    c.setSpeed(n);
                  }
                },
              ),
              DropdownButtonFormField<CoordinateSystem>(
                key: ValueKey(c.system),
                initialValue: c.system,
                isExpanded: true,
                decoration: const InputDecoration(
                  labelText: '坐标系',
                  isDense: true,
                  contentPadding: EdgeInsets.symmetric(
                    horizontal: 16,
                    vertical: 18,
                  ),
                ),
                items: CoordinateSystem.values
                    .map(
                      (v) => DropdownMenuItem(
                        value: v,
                        child: Text(coordinateLabel(v.name)),
                      ),
                    )
                    .toList(),
                onChanged: c.editable
                    ? (v) {
                        if (v != null) unawaited(changeSystem(v));
                      }
                    : null,
              ),
            ];
            return two
                ? Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Expanded(child: children[0]),
                      const SizedBox(width: 12),
                      Expanded(child: children[1]),
                    ],
                  )
                : Column(
                    children: [
                      children[0],
                      const SizedBox(height: 16),
                      children[1],
                    ],
                  );
          },
        ),
        const SizedBox(height: 16),
        Wrap(
          spacing: 8,
          runSpacing: 4,
          children: [
            TextButton.icon(
              onPressed: c.editable && !fileBusy ? () => files(false) : null,
              icon: const Icon(Icons.folder_open),
              label: const Text('打开路线'),
            ),
            TextButton.icon(
              onPressed: c.canSave && !fileBusy ? () => files(true) : null,
              icon: const Icon(Icons.save_outlined),
              label: const Text('保存路线'),
            ),
            TextButton.icon(
              onPressed: c.canSave
                  ? () async {
                      final name = await askName('记入轨迹库', '我的轨迹');
                      if (name != null && mounted) {
                        await run(() => c.record(name));
                      }
                    }
                  : null,
              icon: const Icon(Icons.bookmark_add_outlined),
              label: const Text('记入轨迹库'),
            ),
            TextButton.icon(
              onPressed: () => history(),
              icon: const Icon(Icons.history),
              label: Text('轨迹记录 ${c.records.length}'),
            ),
          ],
        ),
      ],
    );
  }

  Widget driving() {
    final t = c.model.vehicleTelemetry;
    if (t == null) return const SizedBox.shrink();
    final values = t.values;
    String n(String key, {int decimals = 0}) =>
        (values[key] as num? ?? 0).toStringAsFixed(decimals);
    final event = trafficCatalog
        .where((e) => e.id == t.drivingEvent)
        .firstOrNull;
    return ExpansionTile(
      key: const PageStorageKey('map-driving'),
      tilePadding: EdgeInsets.zero,
      initiallyExpanded: c.running,
      title: Text(
        '${t.speedKph.toStringAsFixed(1)} km/h · ${t.gear == 0 ? 'N' : '${t.gear} 挡'}',
      ),
      subtitle: Text(c.model.simulating ? '驾驶状态 · 运行中' : '驾驶状态 · 已停止'),
      children: [
        Wrap(
          spacing: 16,
          runSpacing: 8,
          children: [
            Text('目标 ${n('targetSpeedKph', decimals: 1)} km/h'),
            Text('转速 ${n('rpm')} rpm'),
            Text('油门 ${((values['throttle'] as num? ?? 0) * 100).round()}%'),
            Text('刹车 ${((values['brake'] as num? ?? 0) * 100).round()}%'),
            Text('已行驶 ${n('traveledMeters', decimals: 1)} m'),
            Text('剩余 ${n('remainingMeters', decimals: 1)} m'),
          ],
        ),
        if (event != null)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 12),
            child: Text(
              '${event.name} · ${values['eventStage'] ?? ''} · ${n('eventRemainingSeconds')} s',
            ),
          ),
        if (c.running &&
            c.vehicle.enabled &&
            c.vehicle.transmission == 'manual') ...[
          SwitchListTile.adaptive(
            contentPadding: EdgeInsets.zero,
            title: const Text('自动操作'),
            value: automatic,
            onChanged: (v) {
              automatic = v;
              controls();
            },
          ),
          DropdownButtonFormField<int>(
            initialValue: gear,
            isExpanded: true,
            decoration: const InputDecoration(labelText: '手动挡位'),
            items: [
              for (var i = 0; i <= c.vehicle.gearRatios.length; i++)
                DropdownMenuItem(
                  value: i,
                  child: Text(i == 0 ? 'N · 空挡' : '$i 挡'),
                ),
            ],
            onChanged: automatic
                ? null
                : (v) {
                    gear = v!;
                    controls();
                  },
          ),
          Text('油门 ${(throttle * 100).round()}%'),
          Slider(
            value: throttle,
            onChanged: automatic
                ? null
                : (v) {
                    throttle = v;
                    controls();
                  },
          ),
          Text('刹车 ${(brake * 100).round()}%'),
          Slider(
            value: brake,
            onChanged: automatic
                ? null
                : (v) {
                    brake = v;
                    controls();
                  },
          ),
        ],
        const SizedBox(height: 12),
      ],
    );
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Material(
      color: scheme.surface,
      child: Column(
        children: [
          GestureDetector(
            behavior: HitTestBehavior.opaque,
            onVerticalDragUpdate: widget.onDrag == null
                ? null
                : (d) => widget.onDrag!(d.delta.dy),
            onVerticalDragEnd: widget.onDragEnd == null
                ? null
                : (_) => widget.onDragEnd!(),
            child: Padding(
              padding: const EdgeInsets.fromLTRB(16, 6, 4, 4),
              child: Column(
                children: [
                  if (widget.onDrag != null)
                    Container(
                      width: 36,
                      height: 4,
                      margin: const EdgeInsets.only(bottom: 4),
                      decoration: BoxDecoration(
                        color: scheme.outlineVariant,
                        borderRadius: BorderRadius.circular(4),
                      ),
                    ),
                  Row(
                    children: [
                      const Icon(Icons.route_outlined, size: 22),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          MediaQuery.textScalerOf(context).scale(14) > 20
                              ? '轨迹'
                              : '轨迹模拟',
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                      ),
                      IconButton(
                        tooltip: '地图设置',
                        onPressed: mapSettings,
                        icon: const Icon(Icons.tune_outlined),
                      ),
                      IconButton(
                        tooltip: widget.collapsed ? '展开面板' : '收起面板',
                        onPressed: widget.onToggle,
                        icon: Icon(
                          widget.collapsed
                              ? Icons.expand_less
                              : Icons.expand_more,
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ),
          if (!widget.collapsed)
            Expanded(
              child: SingleChildScrollView(
                key: const PageStorageKey('native-map-panel-scroll'),
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    if (c.error != null)
                      Container(
                        margin: const EdgeInsets.only(bottom: 12),
                        padding: const EdgeInsets.fromLTRB(12, 8, 4, 8),
                        decoration: BoxDecoration(
                          color: scheme.errorContainer,
                          borderRadius: BorderRadius.circular(12),
                        ),
                        child: Row(
                          children: [
                            Expanded(
                              child: Text(
                                c.error!,
                                style: TextStyle(
                                  color: scheme.onErrorContainer,
                                ),
                              ),
                            ),
                            IconButton(
                              tooltip: '关闭提示',
                              onPressed: c.dismissError,
                              icon: const Icon(Icons.close),
                            ),
                          ],
                        ),
                      ),
                    TextField(
                      controller: search,
                      onSubmitted: submitSearch,
                      textInputAction: TextInputAction.search,
                      decoration: InputDecoration(
                        labelText: '搜索地点',
                        hintText: '道路、园区或商场',
                        prefixIcon: const Icon(Icons.search),
                        suffixIcon: IconButton(
                          tooltip: '搜索',
                          onPressed: () => submitSearch(search.text),
                          icon: c.searching
                              ? const SizedBox.square(
                                  dimension: 18,
                                  child: CircularProgressIndicator(
                                    strokeWidth: 2,
                                  ),
                                )
                              : const Icon(Icons.arrow_forward),
                        ),
                      ),
                    ),
                    Align(
                      alignment: Alignment.centerRight,
                      child: TextButton.icon(
                        onPressed: () => history(routes: false),
                        icon: const Icon(Icons.history, size: 16),
                        label: const Text('搜索记录'),
                      ),
                    ),
                    if (c.results.isNotEmpty) ...[
                      Row(
                        children: [
                          const Expanded(child: Text('搜索结果')),
                          IconButton(
                            tooltip: '收起搜索结果',
                            onPressed: c.dismissResults,
                            icon: const Icon(Icons.close),
                          ),
                        ],
                      ),
                      ConstrainedBox(
                        constraints: const BoxConstraints(maxHeight: 260),
                        child: ListView(
                          shrinkWrap: true,
                          children: [
                            for (final result in c.results)
                              ListTile(
                                contentPadding: EdgeInsets.zero,
                                title: Text('${result['title']}', maxLines: 2),
                                subtitle: Text(
                                  '${result['address']}',
                                  maxLines: 2,
                                  overflow: TextOverflow.ellipsis,
                                ),
                                onTap: () =>
                                    c.fly(MapWorkspaceController.place(result)),
                                trailing: PopupMenuButton<String>(
                                  tooltip: '使用地点',
                                  onSelected: (role) {
                                    if (role == 'point') c.setMode('single');
                                    c.select(
                                      MapWorkspaceController.place(result),
                                      name: '${result['title']}',
                                      role: role == 'point' ? null : role,
                                    );
                                    FocusScope.of(context).unfocus();
                                  },
                                  itemBuilder: (_) => const [
                                    PopupMenuItem(
                                      value: 'start',
                                      child: Text('设为起点'),
                                    ),
                                    PopupMenuItem(
                                      value: 'end',
                                      child: Text('设为终点'),
                                    ),
                                    PopupMenuItem(
                                      value: 'point',
                                      child: Text('单点选择'),
                                    ),
                                  ],
                                ),
                              ),
                          ],
                        ),
                      ),
                      const Divider(),
                    ],
                    LayoutBuilder(
                      builder: (context, box) => SegmentedButton<String>(
                        showSelectedIcon: false,
                        style: const ButtonStyle(
                          visualDensity: VisualDensity.compact,
                        ),
                        segments: [
                          ButtonSegment(
                            value: 'navigation',
                            label: Text(
                              box.maxWidth < 300 ||
                                      MediaQuery.textScalerOf(
                                            context,
                                          ).scale(14) >
                                          20
                                  ? '导航'
                                  : '导航路线',
                            ),
                          ),
                          ButtonSegment(
                            value: 'draw',
                            label: Text(
                              box.maxWidth < 300 ||
                                      MediaQuery.textScalerOf(
                                            context,
                                          ).scale(14) >
                                          20
                                  ? '绘制'
                                  : '自由绘制',
                            ),
                          ),
                          ButtonSegment(
                            value: 'single',
                            label: Text(
                              box.maxWidth < 300 ||
                                      MediaQuery.textScalerOf(
                                            context,
                                          ).scale(14) >
                                          20
                                  ? '单点'
                                  : '单点选择',
                            ),
                          ),
                        ],
                        selected: {c.mode},
                        onSelectionChanged: c.editable
                            ? (v) => c.setMode(v.single)
                            : null,
                      ),
                    ),
                    const SizedBox(height: 16),
                    if (c.mode == 'navigation') ...[
                      endpoint(true),
                      endpoint(false),
                      const SizedBox(height: 8),
                      Wrap(
                        spacing: 8,
                        runSpacing: 8,
                        children: [
                          OutlinedButton.icon(
                            onPressed: c.editable ? c.swap : null,
                            icon: const Icon(Icons.swap_vert),
                            label: const Text('交换'),
                          ),
                          FilledButton.tonalIcon(
                            onPressed:
                                c.editable &&
                                    c.start != null &&
                                    c.end != null &&
                                    !c.planning
                                ? () => run(c.plan)
                                : null,
                            icon: c.planning
                                ? const SizedBox.square(
                                    dimension: 16,
                                    child: CircularProgressIndicator(
                                      strokeWidth: 2,
                                    ),
                                  )
                                : const Icon(Icons.alt_route),
                            label: Text(c.planning ? '规划中' : '规划路线'),
                          ),
                        ],
                      ),
                      for (var i = 0; i < c.alternatives.length; i++)
                        ListTile(
                          contentPadding: EdgeInsets.zero,
                          selected: i == c.activeRoute,
                          leading: Icon(
                            i == c.activeRoute
                                ? Icons.radio_button_checked
                                : Icons.radio_button_unchecked,
                          ),
                          onTap: c.editable ? () => c.chooseRoute(i) : null,
                          title: Text(
                            '路线 ${i + 1} · ${((c.alternatives[i]['distance'] as num) / 1000).toStringAsFixed(1)} km',
                          ),
                          subtitle: Text(
                            '${c.alternatives[i]['durationMinutes']} 分钟 · ${c.alternatives[i]['trafficLightCount']} 个红绿灯',
                          ),
                        ),
                    ] else ...[
                      Text(
                        c.mode == 'draw'
                            ? '点击地图依次绘制 · ${c.points.length} 个点'
                            : c.points.isEmpty
                            ? '点击地图选择一个位置'
                            : pointLabel(c.points.first, ''),
                      ),
                      if (c.mode == 'draw')
                        Align(
                          alignment: Alignment.centerLeft,
                          child: TextButton.icon(
                            onPressed: c.editable && c.points.isNotEmpty
                                ? c.undo
                                : null,
                            icon: const Icon(Icons.undo),
                            label: const Text('撤销上一个点'),
                          ),
                        ),
                    ],
                    const Divider(height: 28),
                    fields(),
                    if (c.message.isNotEmpty)
                      Padding(
                        padding: const EdgeInsets.only(top: 8),
                        child: Text(
                          c.message,
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                      ),
                    driving(),
                  ],
                ),
              ),
            ),
          const Divider(height: 1),
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 10, 16, 12),
            child: Row(
              children: [
                if (MediaQuery.textScalerOf(context).scale(14) > 20)
                  IconButton.filledTonal(
                    tooltip: '清除路线',
                    onPressed: c.editable ? c.clear : null,
                    icon: const Icon(Icons.delete_outline),
                  )
                else
                  Expanded(
                    child: OutlinedButton(
                      onPressed: c.editable ? c.clear : null,
                      child: const Text('清除路线'),
                    ),
                  ),
                const SizedBox(width: 12),
                Expanded(
                  flex: 2,
                  child: FilledButton.icon(
                    onPressed: c.running
                        ? c.stop
                        : c.canSave
                        ? () => run(() async {
                            FocusScope.of(context).unfocus();
                            if (!c.vehicle.enabled) {
                              final value = double.tryParse(speed.text);
                              if (value == null) {
                                throw const FormatException('请输入有效速度');
                              }
                              c.setSpeed(value);
                            }
                            automatic = true;
                            gear = 1;
                            throttle = brake = 0;
                            await c.startSimulation();
                          })
                        : null,
                    style: c.running
                        ? FilledButton.styleFrom(
                            backgroundColor: scheme.error,
                            foregroundColor: scheme.onError,
                          )
                        : null,
                    icon: Icon(
                      c.running
                          ? Icons.stop
                          : c.mode == 'single'
                          ? Icons.check
                          : Icons.play_arrow,
                    ),
                    label: Text(
                      c.model.simulationPreparing
                          ? '取消准备'
                          : c.running
                          ? '停止模拟'
                          : c.mode == 'single'
                          ? '应用位置'
                          : '开始模拟',
                    ),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

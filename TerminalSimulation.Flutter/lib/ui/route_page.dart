import 'dart:io';
import 'dart:math' as math;
import 'package:flutter/material.dart';
import '../core/route.dart';
import '../services/app_model.dart';
import '../services/route_files.dart';
import '../core/vehicle_options.dart';
import 'components.dart';
import 'map_workspace.dart';
import 'device_position_controls.dart';

class RoutePage extends StatefulWidget {
  const RoutePage({super.key, required this.model});
  final AppModel model;
  @override
  State<RoutePage> createState() => _RoutePageState();
}

class _RoutePageState extends State<RoutePage> {
  final editor = TextEditingController(),
      path = TextEditingController(),
      name = TextEditingController(text: '我的路线');
  String error = '';
  @override
  void dispose() {
    editor.dispose();
    path.dispose();
    name.dispose();
    super.dispose();
  }

  Future<void> act(Future<void> Function() work) async {
    try {
      await work();
      if (mounted) setState(() => error = '');
    } catch (e) {
      if (mounted) setState(() => error = '$e');
    }
  }

  @override
  Widget build(BuildContext context) {
    final m = widget.model, route = m.route;
    return PageBody(
      title: '轨迹工作台',
      description: '地图选点、搜索和规划路线，或载入 CTS 路线进行模拟。',
      children: [
        if (m.realLocation)
          Section('设备实时位置', child: DevicePositionSummary(model: m)),
        if (error.isNotEmpty)
          Text(
            error,
            style: TextStyle(color: Theme.of(context).colorScheme.error),
          ),
        Section(
          '地图工作台',
          subtitle: '搜索地点、规划路线与模拟驾驶。',
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              FilledButton.icon(
                onPressed: () async {
                  if (!m.mapCredentials.available) {
                    ScaffoldMessenger.of(context).showSnackBar(
                      const SnackBar(content: Text('地图配置不可用，请联系应用提供者。')),
                    );
                    return;
                  }
                  await Navigator.of(context).push(
                    MaterialPageRoute<void>(
                      builder: (_) => MapWorkspace(
                        model: m,
                        openRoute: RouteFiles.open,
                        saveRoute: (document) async {
                          final saved = await RouteFiles.save(document);
                          if (saved) m.log('路线', '路线已保存');
                          return saved;
                        },
                      ),
                    ),
                  );
                },
                icon: const Icon(Icons.map_outlined),
                label: const Text('打开地图工作台'),
              ),
            ],
          ),
        ),
        if (!m.realLocation)
          Section(
            '轨迹预览',
            subtitle: route == null
                ? '尚未载入路线'
                : '${route.coordinateSystem.name.toUpperCase()} · ${route.points.length} 个点 · ${route.speed} km/h',
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                SizedBox(
                  height: 280,
                  width: double.infinity,
                  child: CustomPaint(
                    painter: RoutePainter(
                      route,
                      Theme.of(context).colorScheme.primary,
                    ),
                    child: route == null
                        ? const Center(child: Text('导入 .ctsroute 后显示轨迹轮廓'))
                        : null,
                  ),
                ),
                const SizedBox(height: 12),
                Wrap(
                  spacing: 10,
                  runSpacing: 10,
                  children: [
                    FilledButton.icon(
                      onPressed:
                          route == null ||
                              m.simulationPreparing ||
                              m.realLocation
                          ? null
                          : () => act(() async {
                              if (m.simulating) {
                                m.stopSimulation();
                                m.changed();
                              } else {
                                await m.startSimulation();
                              }
                            }),
                      icon: Icon(m.simulating ? Icons.stop : Icons.play_arrow),
                      label: Text(
                        m.simulationPreparing
                            ? '准备中…'
                            : m.simulating
                            ? '停止模拟'
                            : '开始模拟',
                      ),
                    ),
                    Text(
                      '当前位置  ${m.latitude.toStringAsFixed(6)}, ${m.longitude.toStringAsFixed(6)}',
                    ),
                  ],
                ),
              ],
            ),
          ),
        if (m.vehicleTelemetry != null)
          Section(
            '驾驶状态',
            subtitle: '查看车速、挡位与行驶进度。',
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Wrap(
                  spacing: 20,
                  runSpacing: 12,
                  children: [
                    Text(
                      '实际速度  ${m.vehicleTelemetry!.speedKph.toStringAsFixed(1)} km/h',
                    ),
                    Text(
                      '目标速度  ${(m.vehicleTelemetry!.values['targetSpeedKph'] as num).toStringAsFixed(1)} km/h',
                    ),
                    Text(
                      '挡位  ${m.vehicleTelemetry!.gear == 0 ? 'N' : m.vehicleTelemetry!.gear}',
                    ),
                    Text(
                      '转速  ${(m.vehicleTelemetry!.values['rpm'] as num).round()} rpm',
                    ),
                    Text(
                      '油门  ${((m.vehicleTelemetry!.values['throttle'] as num) * 100).round()}%',
                    ),
                    Text(
                      '刹车  ${((m.vehicleTelemetry!.values['brake'] as num) * 100).round()}%',
                    ),
                    Text(
                      '已行驶  ${(m.vehicleTelemetry!.values['traveledMeters'] as num).toStringAsFixed(1)} m',
                    ),
                    Text(
                      '剩余  ${(m.vehicleTelemetry!.values['remainingMeters'] as num).toStringAsFixed(1)} m',
                    ),
                  ],
                ),
                if (m.vehicleTelemetry!.drivingEvent.isNotEmpty)
                  Padding(
                    padding: const EdgeInsets.only(top: 12),
                    child: Text(
                      '模拟情景 · ${trafficCatalog.where((e) => e.id == m.vehicleTelemetry!.drivingEvent).firstOrNull?.name ?? ''}',
                    ),
                  ),
              ],
            ),
          ),
        if (!m.realLocation)
          Section(
            '路线文件',
            subtitle: '打开或保存 .ctsroute 路线文件。',
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                AppTextField(
                  controller: editor,
                  minLines: 7,
                  maxLines: 16,
                  decoration: const InputDecoration(
                    labelText: '粘贴 .ctsroute 文件内容',
                  ),
                ),
                const SizedBox(height: 12),
                Wrap(
                  spacing: 10,
                  runSpacing: 10,
                  children: [
                    FilledButton.icon(
                      onPressed: () => act(() async {
                        final document = await RouteFiles.open();
                        if (document != null) {
                          m.applyRoute(document);
                          editor.text = document.serialize();
                        }
                      }),
                      icon: const Icon(Icons.folder_open),
                      label: const Text('选择路线文件'),
                    ),
                    OutlinedButton.icon(
                      onPressed: route == null
                          ? null
                          : () => act(() async {
                              if (await RouteFiles.save(route)) {
                                m.log('路线', '路线已保存');
                              }
                            }),
                      icon: const Icon(Icons.save_alt),
                      label: const Text('保存路线文件'),
                    ),
                    FilledButton(
                      onPressed: () => act(() async {
                        m.applyRoute(RouteDocument.parse(editor.text));
                      }),
                      child: const Text('载入路线'),
                    ),
                    OutlinedButton(
                      onPressed: route == null
                          ? null
                          : () {
                              editor.text = route.serialize();
                            },
                      child: const Text('填入当前路线'),
                    ),
                  ],
                ),
                const SizedBox(height: 16),
                AppTextField(
                  controller: path,
                  decoration: const InputDecoration(
                    labelText: '本地文件路径（应用可访问目录）',
                  ),
                ),
                const SizedBox(height: 12),
                Wrap(
                  spacing: 10,
                  runSpacing: 10,
                  children: [
                    OutlinedButton(
                      onPressed: () => act(() async {
                        final file = File(path.text.trim());
                        if (await file.length() > 25 * 1024 * 1024) {
                          throw const FormatException('路线超过 25 MiB');
                        }
                        final content = await file.readAsString();
                        final document = RouteDocument.parse(content);
                        editor.text = content;
                        m.applyRoute(document);
                      }),
                      child: const Text('打开文件'),
                    ),
                    OutlinedButton(
                      onPressed: route == null
                          ? null
                          : () => act(() async {
                              final target = File(path.text.trim());
                              if (await target.exists()) {
                                throw const FileSystemException(
                                  '目标已存在，请使用新文件名',
                                );
                              }
                              final temporary = File('${target.path}.tmp');
                              await temporary.writeAsString(
                                route.serialize(),
                                flush: true,
                              );
                              await temporary.rename(target.path);
                              m.log('路线', '路线已保存');
                            }),
                      child: const Text('另存为新文件'),
                    ),
                  ],
                ),
              ],
            ),
          ),
      ],
    );
  }
}

class RoutePainter extends CustomPainter {
  RoutePainter(this.route, this.color);
  final RouteDocument? route;
  final Color color;
  @override
  void paint(Canvas canvas, Size size) {
    final grid = Paint()
      ..color = color.withValues(alpha: .07)
      ..strokeWidth = 1;
    for (double x = 0; x < size.width; x += 28) {
      canvas.drawLine(Offset(x, 0), Offset(x, size.height), grid);
    }
    for (double y = 0; y < size.height; y += 28) {
      canvas.drawLine(Offset(0, y), Offset(size.width, y), grid);
    }
    final points = route?.points;
    if (points == null) return;
    final minLat = points.map((p) => p.lat).reduce(math.min),
        maxLat = points.map((p) => p.lat).reduce(math.max),
        minLng = points.map((p) => p.lng).reduce(math.min),
        maxLng = points.map((p) => p.lng).reduce(math.max);
    final scale = math.min(
      (size.width - 40) / math.max(.00001, maxLng - minLng),
      (size.height - 40) / math.max(.00001, maxLat - minLat),
    );
    Offset project(int i) => Offset(
      size.width / 2 + (points[i].lng - (minLng + maxLng) / 2) * scale,
      size.height / 2 - (points[i].lat - (minLat + maxLat) / 2) * scale,
    );
    final path = Path()..moveTo(project(0).dx, project(0).dy);
    for (var i = 1; i < points.length; i++) {
      final p = project(i);
      path.lineTo(p.dx, p.dy);
    }
    canvas.drawPath(
      path,
      Paint()
        ..color = color
        ..strokeWidth = 3
        ..style = PaintingStyle.stroke
        ..strokeCap = StrokeCap.round
        ..strokeJoin = StrokeJoin.round,
    );
    canvas.drawCircle(project(0), 6, Paint()..color = color);
    canvas.drawCircle(
      project(points.length - 1),
      6,
      Paint()..color = Colors.orange,
    );
  }

  @override
  bool shouldRepaint(covariant RoutePainter old) =>
      old.route != route || old.color != color;
}

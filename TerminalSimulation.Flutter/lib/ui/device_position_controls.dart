import 'package:flutter/material.dart';
import '../services/app_model.dart';
import '../services/mobile_runtime.dart';

class DevicePositionControls extends StatelessWidget {
  const DevicePositionControls({super.key, required this.model});
  final AppModel model;
  @override
  Widget build(BuildContext context) {
    if (!MobileRuntime.supported) return const SizedBox.shrink();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SwitchListTile.adaptive(
          contentPadding: EdgeInsets.zero,
          title: const Text('使用设备系统定位'),
          subtitle: const Text('由系统选择可用定位方式，用于位置汇报与地图实时显示。'),
          value: model.realLocation,
          onChanged: model.locationChanging
              ? null
              : (value) async {
                  try {
                    await model.setRealLocation(value);
                  } catch (e) {
                    if (context.mounted) {
                      ScaffoldMessenger.of(
                        context,
                      ).showSnackBar(SnackBar(content: Text('$e')));
                    }
                  }
                },
        ),
        if (model.locationChanging) const LinearProgressIndicator(),
        if (model.realLocation) DevicePositionSummary(model: model),
        const SizedBox(height: 16),
      ],
    );
  }
}

class DevicePositionSummary extends StatelessWidget {
  const DevicePositionSummary({super.key, required this.model});
  final AppModel model;
  @override
  Widget build(BuildContext context) {
    final fix = model.deviceFix;
    final colors = Theme.of(context).colorScheme;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Icon(
              model.hasDeviceFix
                  ? Icons.location_on_outlined
                  : Icons.location_searching,
              color: model.hasDeviceFix
                  ? colors.primary
                  : colors.onSurfaceVariant,
            ),
            const SizedBox(width: 8),
            Expanded(
              child: Text(
                model.locationStatus,
                style: Theme.of(context).textTheme.titleSmall,
              ),
            ),
          ],
        ),
        if (fix != null) ...[
          const SizedBox(height: 8),
          Text(fix.source.detail),
          const SizedBox(height: 8),
          Text(model.gnssStatus),
          const SizedBox(height: 8),
          SelectableText(
            '${model.hasDeviceFix ? '' : '上次位置：'}${fix.latitude.toStringAsFixed(6)}, ${fix.longitude.toStringAsFixed(6)} · WGS-84',
          ),
          const SizedBox(height: 8),
          Wrap(
            spacing: 16,
            runSpacing: 8,
            children: [
              Text(
                fix.accuracy > 0
                    ? '水平精度约 ${fix.accuracy.toStringAsFixed(1)} m'
                    : '水平精度未提供',
              ),
              Text(
                fix.speedMps == null
                    ? '速度未提供'
                    : '速度 ${(fix.speedMps! * 3.6).toStringAsFixed(1)} km/h',
              ),
              Text(
                fix.altitude == null
                    ? '高程未提供'
                    : '高程 ${fix.altitude!.toStringAsFixed(1)} m',
              ),
              Text('位置已更新 ${fix.ageMilliseconds ~/ 1000} 秒'),
              Text('定位时间 ${fix.time.toLocal().toString().split('.').first}'),
            ],
          ),
        ],
        if (model.satellitesVisible != null ||
            model.satellitesUsed != null) ...[
          const SizedBox(height: 8),
          Wrap(
            spacing: 16,
            runSpacing: 8,
            children: [
              if (model.satellitesVisible != null)
                Text('可见卫星 ${model.satellitesVisible}'),
              if (model.satellitesUsed != null)
                Text('参与解算卫星 ${model.satellitesUsed}'),
            ],
          ),
        ],
        if (fix != null && model.deviceLocationMessage.isNotEmpty) ...[
          const SizedBox(height: 8),
          Text(
            model.deviceLocationMessage,
            style: TextStyle(color: colors.onSurfaceVariant),
          ),
        ],
      ],
    );
  }
}

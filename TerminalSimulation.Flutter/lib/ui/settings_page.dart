import 'package:flutter/material.dart';
import '../services/app_model.dart';
import 'components.dart';

class SettingsPage extends StatelessWidget {
  const SettingsPage({super.key, required this.model});
  final AppModel model;
  @override
  Widget build(BuildContext context) => PageBody(
    title: '外观与偏好',
    description: '按设备尺寸和系统设置调整显示体验。',
    children: [
      Section(
        '显示',
        child: Column(
          children: [
            DropdownButtonFormField<String>(
              isExpanded: true,
              initialValue: model.theme,
              key: ValueKey(model.theme),
              decoration: const InputDecoration(labelText: '主题'),
              items: const [
                DropdownMenuItem(value: 'system', child: Text('跟随系统')),
                DropdownMenuItem(value: 'light', child: Text('浅色')),
                DropdownMenuItem(value: 'dark', child: Text('深色')),
              ],
              onChanged: (v) {
                model.theme = v!;
                model.changed();
              },
            ),
            const SizedBox(height: 16),
            SwitchListTile(
              contentPadding: EdgeInsets.zero,
              title: const Text('局部玻璃质感'),
              subtitle: const Text('用于导航与浮层，关闭后使用清晰实色材质'),
              value: model.glass,
              onChanged: (v) {
                model.glass = v;
                model.changed();
              },
            ),
            SwitchListTile(
              contentPadding: EdgeInsets.zero,
              title: const Text('减少动态效果'),
              subtitle: const Text('同时遵循系统无障碍设置'),
              value: model.reduceMotion,
              onChanged: (v) {
                model.reduceMotion = v;
                model.changed();
              },
            ),
            const SizedBox(height: 12),
            FilledButton.icon(
              onPressed: () => model.guard(model.saveSettings),
              icon: const Icon(Icons.save_outlined),
              label: const Text('保存偏好'),
            ),
            TextButton(
              onPressed: () async {
                final confirmed = await confirmAction(
                  context,
                  title: '恢复默认外观',
                  message: '主题将跟随系统，开启局部玻璃质感，并恢复标准过渡动效。',
                  nativeMaterial: model.glass,
                );
                if (confirmed) {
                  model.theme = 'system';
                  model.glass = true;
                  model.reduceMotion = false;
                  model.changed();
                }
              },
              child: const Text('恢复默认外观'),
            ),
          ],
        ),
      ),
      Section(
        '关于 CTS',
        child: const Text('CTS Terminal Studio · 开发版本 0.1.0\n车载终端模拟与协议联调工作台。'),
      ),
    ],
  );
}

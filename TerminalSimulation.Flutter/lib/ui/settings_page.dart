import 'package:flutter/material.dart';
import '../services/app_model.dart';
import 'components.dart';

class SettingsPage extends StatelessWidget {
  const SettingsPage({super.key, required this.model, required this.themeMenu});
  final AppModel model;
  final MenuController themeMenu;
  @override
  Widget build(BuildContext context) => PageBody(
    title: '外观与偏好',
    description: '设置主题、玻璃质感和动态效果。',
    children: [
      Section(
        '显示',
        child: Column(
          children: [
            DropdownMenu<String>(
              expandedInsets: EdgeInsets.zero,
              initialSelection: model.theme,
              menuController: themeMenu,
              requestFocusOnTap: false,
              enableSearch: false,
              key: ValueKey(model.theme),
              label: const Text('主题'),
              dropdownMenuEntries: const [
                DropdownMenuEntry(value: 'system', label: '跟随系统'),
                DropdownMenuEntry(value: 'light', label: '浅色'),
                DropdownMenuEntry(value: 'dark', label: '深色'),
              ],
              onSelected: (v) {
                if (v == null) return;
                model.theme = v;
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
        child: const Text('CTS Terminal Studio · 0.1.0\n车载终端模拟与协议联调工作台。'),
      ),
    ],
  );
}

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:terminal_simulation/services/app_model.dart';
import 'package:terminal_simulation/services/http_workspace.dart';
import 'package:terminal_simulation/services/map_workspace_controller.dart';
import 'package:terminal_simulation/ui/components.dart';
import 'package:terminal_simulation/ui/http_workbench_page.dart';
import 'package:terminal_simulation/ui/map_control_panel.dart';
import 'package:terminal_simulation/ui/message_analyzer_page.dart';
import 'package:terminal_simulation/ui/workbench.dart';
import 'package:terminal_simulation/ui/app.dart';
import 'package:terminal_simulation/ui/map_vehicle_settings.dart';
import 'package:terminal_simulation/core/vehicle_options.dart';

const composing = TextEditingValue(
  text: 'chong',
  selection: TextSelection.collapsed(offset: 5),
  composing: TextRange(start: 0, end: 5),
);

void viewport(WidgetTester tester, Size size) {
  tester.view.devicePixelRatio = 1;
  tester.view.physicalSize = size;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);
  addTearDown(tester.view.resetViewInsets);
}

Future<EditableTextState> compose(WidgetTester tester, Finder input) async {
  await tester.ensureVisible(input);
  await tester.showKeyboard(input);
  tester.testTextInput.updateEditingValue(composing);
  await tester.pump();
  return tester.state<EditableTextState>(
    find.descendant(of: input, matching: find.byType(EditableText)),
  );
}

void retained(WidgetTester tester, EditableTextState state) {
  expect(state.mounted, isTrue);
  expect(state.widget.focusNode.hasFocus, isTrue);
  expect(state.widget.controller.value, composing);
  expect(tester.testTextInput.isVisible, isTrue);
  expect(tester.takeException(), isNull);
}

void main() {
  testWidgets(
    'terminal identity and position editors retain drafts on telemetry rebuild',
    (tester) async {
      viewport(tester, const Size(1200, 900));
      final model = AppModel()..reduceMotion = true;
      await tester.pumpWidget(TerminalApp(model: model));
      await tester.pumpAndSettle();
      for (final label in ['连接配置', '注册身份', '标准附加信息（0x0200）']) {
        final title = find.text(label);
        await tester.ensureVisible(title);
        await tester.tap(title);
        await tester.pumpAndSettle();
      }
      final fields = find.byType(EditableText);
      final count = fields.evaluate().length;
      expect(count, greaterThan(20));
      for (var i = 0; i < count; i++) {
        final finder = fields.at(i);
        final widget = tester.widget<EditableText>(finder);
        if (widget.readOnly) continue;
        await tester.ensureVisible(finder);
        await tester.showKeyboard(finder);
        tester.testTextInput.updateEditingValue(composing);
        await tester.pump();
        final state = tester.state<EditableTextState>(finder);
        model.changed();
        await tester.pump();
        retained(tester, state);
      }
      await tester.pumpWidget(const SizedBox());
      model.dispose();
      await tester.pump();
    },
  );

  testWidgets('vehicle numeric editors keep keyboard across dialog resize', (
    tester,
  ) async {
    viewport(tester, const Size(800, 1000));
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: Builder(
            builder: (context) => TextButton(
              onPressed: () => editMapVehicle(context, VehicleOptions()),
              child: const Text('车辆设置'),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('车辆设置'));
    await tester.pumpAndSettle();
    final field = find.byType(AppTextField).first;
    final state = await compose(tester, field);
    tester.view.viewInsets = const FakeViewPadding(bottom: 450);
    tester.view.physicalSize = const Size(390, 1000);
    await tester.pumpAndSettle();
    retained(tester, state);
    await tester.pumpWidget(const SizedBox());
  });

  testWidgets(
    'workbench keeps IME across keyboard height and resize thresholds',
    (tester) async {
      viewport(tester, const Size(800, 900));
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: WorkbenchViewport(
              builder: (_, _) => const Align(
                alignment: Alignment.topCenter,
                child: AppTextField(
                  decoration: InputDecoration(labelText: '编辑'),
                ),
              ),
            ),
          ),
        ),
      );
      final field = find.byType(AppTextField);
      final state = await compose(tester, field);
      tester.view.viewInsets = const FakeViewPadding(bottom: 500);
      await tester.pumpAndSettle();
      retained(tester, state);
      tester.view.physicalSize = const Size(600, 1100);
      await tester.pumpAndSettle();
      retained(tester, state);
      tester.testTextInput.updateEditingValue(
        const TextEditingValue(
          text: '重庆',
          selection: TextSelection.collapsed(offset: 2),
        ),
      );
      await tester.pump();
      expect(state.widget.controller.text, '重庆');
      await tester.pumpWidget(const SizedBox());
    },
  );

  testWidgets('analyzer keeps composition when keyboard causes short layout', (
    tester,
  ) async {
    viewport(tester, const Size(800, 900));
    await tester.pumpWidget(const MaterialApp(home: MessageAnalyzerPage()));
    final state = await compose(tester, find.byType(AppTextField).first);
    tester.view.viewInsets = const FakeViewPadding(bottom: 480);
    await tester.pumpAndSettle();
    retained(tester, state);
    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('HTTP editor keeps IME across desktop columns and keyboard', (
    tester,
  ) async {
    viewport(tester, const Size(1500, 1000));
    await tester.pumpWidget(
      MaterialApp(home: HttpWorkbenchPage(workspace: HttpWorkspace(null))),
    );
    await tester.pumpAndSettle();
    final state = await compose(
      tester,
      find.widgetWithText(AppTextField, '请求名称'),
    );
    for (final size in [
      const Size(1200, 1000),
      const Size(800, 1000),
      const Size(390, 1000),
    ]) {
      tester.view.physicalSize = size;
      await tester.pumpAndSettle();
      retained(tester, state);
    }
    tester.view.viewInsets = const FakeViewPadding(bottom: 450);
    await tester.pumpAndSettle();
    retained(tester, state);
    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('keyed expansion never shares bool storage with text offsets', (
    tester,
  ) async {
    viewport(tester, const Size(600, 900));
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: SingleChildScrollView(
            child: ExpansionTile(
              key: PageStorageKey('section'),
              initiallyExpanded: true,
              title: Text('展开'),
              children: [
                AppTextField(decoration: InputDecoration(labelText: '内容')),
              ],
            ),
          ),
        ),
      ),
    );
    final state = await compose(tester, find.byType(AppTextField));
    retained(tester, state);
    await tester.tap(find.text('展开'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('展开'));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox());
  });

  testWidgets(
    'model rebuild cannot reset an active numeric draft or composition',
    (tester) async {
      viewport(tester, const Size(600, 900));
      var value = '60';
      late StateSetter update;
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: StatefulBuilder(
              builder: (context, setState) {
                update = setState;
                return EditField(
                  '速度',
                  value: value,
                  numeric: true,
                  onChanged: (_) {},
                );
              },
            ),
          ),
        ),
      );
      final state = await compose(tester, find.byType(EditField));
      update(() => value = '80');
      await tester.pump();
      retained(tester, state);
      expect(state.widget.scrollController!.keepScrollOffset, isFalse);
      await tester.pumpWidget(const SizedBox());
    },
  );

  testWidgets(
    'map search retains composition through errors, results and panel resize',
    (tester) async {
      viewport(tester, const Size(600, 900));
      final model = AppModel();
      final controller = MapWorkspaceController(
        model: model,
        token: 'test',
        execute: (_) async {},
      );
      Widget panel() => MaterialApp(
        home: Scaffold(
          body: AnimatedBuilder(
            animation: controller,
            builder: (_, _) => MapControlPanel(
              controller: controller,
              openRoute: () async => null,
              saveRoute: (_) async => false,
              onToggle: () {},
            ),
          ),
        ),
      );
      await tester.pumpWidget(panel());
      final state = await compose(
        tester,
        find.byKey(const ValueKey('map-search')),
      );
      await tester.pump(const Duration(milliseconds: 500));
      expect(controller.searching, isFalse);
      controller.report('临时搜索错误');
      await tester.pump();
      retained(tester, state);
      controller.dismissError();
      controller.results = [
        {'title': '测试地点', 'address': '测试地址', 'lat': 29.5, 'lng': 106.5},
      ];
      controller.changed(geometry: false);
      await tester.pump();
      retained(tester, state);
      tester.view.viewInsets = const FakeViewPadding(bottom: 450);
      tester.view.physicalSize = const Size(390, 900);
      await tester.pumpAndSettle();
      retained(tester, state);
      await tester.pumpWidget(const SizedBox());
      controller.dispose();
      model.dispose();
      await tester.pump();
    },
  );

  testWidgets(
    'text prompt owns its controller until the reverse route completes',
    (tester) async {
      viewport(tester, const Size(600, 900));
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: Builder(
              builder: (context) => TextButton(
                onPressed: () => promptText(context, '名称', '路线'),
                child: const Text('打开'),
              ),
            ),
          ),
        ),
      );
      await tester.tap(find.text('打开'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('保存'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 100));
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
    },
  );
}

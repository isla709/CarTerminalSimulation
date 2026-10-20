import 'dart:io';
import 'dart:ui' as ui;
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:terminal_simulation/services/app_model.dart';
import 'package:terminal_simulation/ui/app.dart';
import 'package:terminal_simulation/core/vehicle_driver.dart';

void main() {
  testWidgets(
    'model changes rebuild route content and preserve its editor state',
    (tester) async {
      tester.view.devicePixelRatio = 1;
      tester.view.physicalSize = const Size(390, 900);
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final model = AppModel()..reduceMotion = true;
      await tester.pumpWidget(TerminalApp(model: model));
      await tester.pumpAndSettle();
      await tester.tap(
        find.descendant(
          of: find.byType(NavigationBar),
          matching: find.text('轨迹'),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('打开地图工作台'), findsOneWidget);
      final editor = find.widgetWithText(TextField, '粘贴 .ctsroute 文件内容');
      await tester.enterText(editor, '保留未保存的路线文本');
      model.vehicleTelemetry = VehicleTelemetry.fixed(
        36,
      ).withValues({'traveledMeters': 75.0});
      model.changed();
      await tester.pumpAndSettle();
      expect(find.text('打开地图工作台'), findsOneWidget);
      expect(find.text('驾驶状态'), findsOneWidget);
      expect(find.text('实际速度  36.0 km/h'), findsOneWidget);
      expect(tester.widget<TextField>(editor).controller!.text, '保留未保存的路线文本');
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      model.dispose();
      await tester.pump();
    },
  );
  for (final viewport in [
    (const Size(900, 320), 1.0),
    (const Size(3840, 2160), 3.0),
  ]) {
    testWidgets('landscape and high DPI ${viewport.$1}', (tester) async {
      tester.view.physicalSize = viewport.$1;
      tester.view.devicePixelRatio = viewport.$2;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final model = AppModel()..reduceMotion = true;
      await tester.pumpWidget(TerminalApp(model: model));
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      model.dispose();
      await tester.pump();
    });
  }
  for (final width in [320.0, 599.0, 600.0, 1023.0, 1024.0, 1440.0]) {
    testWidgets('responsive layout $width, all pages and text scale', (
      tester,
    ) async {
      tester.view.devicePixelRatio = 1;
      tester.view.physicalSize = Size(width, 900);
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final model = AppModel()..reduceMotion = true;
      await tester.pumpWidget(TerminalApp(model: model));
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      expect(
        find.byType(width < 600 ? NavigationBar : NavigationRail),
        findsOneWidget,
      );
      for (final label in ['轨迹', '工具', '日志', '设置', '终端']) {
        final nav = find.descendant(
          of: find.byType(width < 600 ? NavigationBar : NavigationRail),
          matching: find.text(label),
        );
        await tester.tap(nav);
        await tester.pumpAndSettle();
        expect(
          tester.takeException(),
          isNull,
          reason: 'width=$width page=$label',
        );
      }
      await tester.pumpWidget(const SizedBox());
      model.dispose();
      await tester.pump();
    });
  }
  testWidgets('live resize, theme, reduced motion and text scaling', (
    tester,
  ) async {
    final model = AppModel()..reduceMotion = true;
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(1440, 900);
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    tester.platformDispatcher.textScaleFactorTestValue = 1.5;
    addTearDown(tester.platformDispatcher.clearTextScaleFactorTestValue);
    await tester.pumpWidget(TerminalApp(model: model));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    model.theme = 'dark';
    model.changed();
    tester.view.physicalSize = const Size(320, 800);
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox());
    model.dispose();
    await tester.pump();
  });
  testWidgets('render desktop and compact screenshots', (tester) async {
    await tester.runAsync(() async {
      if (Platform.isWindows &&
          await File('C:/Windows/Fonts/msyh.ttc').exists()) {
        final font = FontLoader('Microsoft YaHei')
          ..addFont(
            File(
              'C:/Windows/Fonts/msyh.ttc',
            ).readAsBytes().then((b) => b.buffer.asByteData()),
          );
        await font.load();
      }
      final iconFont = FontLoader('MaterialIcons')
        ..addFont(rootBundle.load('fonts/MaterialIcons-Regular.otf'));
      await iconFont.load();
    });
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final model = AppModel()..reduceMotion = true;
    const key = ValueKey('capture');
    for (final size in [const Size(1440, 1000), const Size(390, 844)]) {
      tester.view.physicalSize = size;
      await tester.pumpWidget(
        RepaintBoundary(
          key: key,
          child: TerminalApp(model: model),
        ),
      );
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      final boundary = tester.renderObject<RenderRepaintBoundary>(
        find.byKey(key),
      );
      await tester.runAsync(() async {
        final image = await boundary.toImage();
        final bytes = await image.toByteData(format: ui.ImageByteFormat.png);
        final file = File('../bin/Debug/flutter-ui/${size.width.toInt()}.png');
        await file.parent.create(recursive: true);
        await file.writeAsBytes(bytes!.buffer.asUint8List());
        image.dispose();
      });
    }
    await tester.pumpWidget(const SizedBox());
    model.dispose();
    await tester.pump();
  });
}

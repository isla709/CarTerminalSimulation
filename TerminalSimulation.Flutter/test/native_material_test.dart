import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:terminal_simulation/services/native_material.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  const channel = MethodChannel('cts/platform');
  final messenger =
      TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger;
  tearDown(() => messenger.setMockMethodCallHandler(channel, null));
  test('unsupported platform falls back without crashing', () async {
    expect(await NativeMaterial.capabilities(), isEmpty);
    expect(await NativeMaterial.showConfirmation('title', 'message'), isNull);
  });
  test('older HarmonyOS never calls material dialog API', () async {
    var dialogCalls = 0;
    messenger.setMockMethodCallHandler(channel, (call) async {
      if (call.method == 'materialCapabilities') {
        return {
          'platform': 'harmonyos',
          'apiVersion': 17,
          'nativeDialogs': false,
        };
      }
      dialogCalls++;
      return true;
    });
    expect(await NativeMaterial.showConfirmation('title', 'message'), isNull);
    expect(dialogCalls, 0);
  });
  test('native HarmonyOS result passes through channel', () async {
    messenger.setMockMethodCallHandler(channel, (call) async {
      if (call.method == 'materialCapabilities') {
        return {
          'platform': 'harmonyos',
          'apiVersion': 26,
          'nativeDialogs': true,
        };
      }
      expect(call.method, 'showMaterialDialog');
      expect(call.arguments, {'title': 'title', 'message': 'message'});
      return false;
    });
    expect(await NativeMaterial.showConfirmation('title', 'message'), isFalse);
  });
}

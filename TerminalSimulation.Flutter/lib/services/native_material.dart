import 'package:flutter/services.dart';

class NativeMaterial {
  static const _channel = MethodChannel('cts/platform');
  static Future<Map<String, dynamic>> capabilities() async {
    try {
      return Map<String, dynamic>.from(
        await _channel.invokeMapMethod<String, dynamic>(
              'materialCapabilities',
            ) ??
            {},
      );
    } on MissingPluginException {
      return {};
    } on PlatformException {
      return {};
    }
  }

  static Future<bool?> showConfirmation(String title, String message) async {
    final info = await capabilities();
    if (info['nativeDialogs'] != true) return null;
    try {
      return await _channel.invokeMethod<bool>('showMaterialDialog', {
        'title': title,
        'message': message,
      });
    } on PlatformException {
      return null;
    } on MissingPluginException {
      return null;
    }
  }
}

import 'package:flutter/material.dart';
import 'services/app_model.dart';
import 'services/frame_diagnostics.dart';
import 'services/storage.dart';
import 'services/map_credentials.dart';
import 'ui/app.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  AppStorage? storage;
  String? storageError;
  try {
    storage = await AppStorage.open();
  } catch (e) {
    storageError = '$e';
  }
  MapCredentials credentials = const MapCredentials.unavailable();
  String? credentialError;
  try {
    credentials = await MapCredentials.load();
  } catch (_) {
    credentialError = '地图配置不可用，请联系应用提供者。';
  }
  final model = AppModel(storage: storage, mapCredentials: credentials);
  if (credentialError != null) model.log('地图', credentialError);
  try {
    await model.loadSettings();
  } catch (e) {
    model.log('错误', '读取设置失败：$e');
  }
  if (storageError != null) model.log('错误', '应用数据目录不可用：$storageError');
  FrameDiagnostics.start();
  runApp(TerminalApp(model: model));
}

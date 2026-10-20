import 'package:flutter/material.dart';
import 'services/app_model.dart';
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
    credentialError = '内置地图凭据不可用，请使用构建脚本重新构建应用。';
  }
  final model = AppModel(storage: storage, mapCredentials: credentials);
  if (credentialError != null) model.log('地图', credentialError);
  try {
    await model.loadSettings();
  } catch (e) {
    model.log('错误', '读取设置失败：$e');
  }
  if (storageError != null) model.log('错误', '应用数据目录不可用：$storageError');
  runApp(TerminalApp(model: model));
}

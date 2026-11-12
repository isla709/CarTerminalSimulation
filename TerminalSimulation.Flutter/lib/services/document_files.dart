import 'dart:io';
import 'package:file_selector/file_selector.dart';
import 'package:flutter/services.dart';

class DocumentFiles {
  static const int limit = 25 * 1024 * 1024;
  static Future<Uint8List?> open({
    List<String> extensions = const ['json', 'txt'],
  }) async {
    if (!Platform.isWindows && !Platform.isLinux) {
      final bytes = await const MethodChannel(
        'cts/platform',
      ).invokeMethod<Uint8List>('openDocument');
      if (bytes != null && bytes.length > limit) {
        throw const FormatException('文件超过 25 MiB');
      }
      return bytes;
    }
    final file = await openFile(
      acceptedTypeGroups: [XTypeGroup(label: '工作文档', extensions: extensions)],
    );
    if (file == null) return null;
    if (await file.length() > limit) throw const FormatException('文件超过 25 MiB');
    return file.readAsBytes();
  }

  static Future<bool> save(Uint8List bytes, String name) async {
    if (bytes.length > limit) throw const FormatException('文件超过 25 MiB');
    if (!Platform.isWindows && !Platform.isLinux) {
      return await const MethodChannel('cts/platform').invokeMethod<bool>(
            'saveDocument',
            {'bytes': bytes, 'name': name},
          ) ??
          false;
    }
    final location = await getSaveLocation(suggestedName: name);
    if (location == null) return false;
    final temp = File(
      '${location.path}.tmp-${DateTime.now().microsecondsSinceEpoch}',
    );
    try {
      await temp.writeAsBytes(bytes, flush: true);
      await temp.rename(location.path);
    } finally {
      if (await temp.exists()) await temp.delete();
    }
    return true;
  }
}

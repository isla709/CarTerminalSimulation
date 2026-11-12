import 'dart:io';
import 'package:file_selector/file_selector.dart';
import 'package:flutter/services.dart';

class VideoFiles {
  static Future<String?> pick() async {
    String? path;
    if (Platform.isWindows || Platform.isLinux) {
      final file = await openFile(
        acceptedTypeGroups: [
          const XTypeGroup(
            label: 'H.264 裸流',
            extensions: ['h264', '264', 'avc'],
          ),
        ],
      );
      path = file?.path;
    } else {
      path = await const MethodChannel(
        'cts/platform',
      ).invokeMethod<String>('openVideo');
    }
    if (path == null) return null;
    if (await File(path).length() > 256 * 1024 * 1024) {
      throw const FormatException('视频文件超过 256 MiB');
    }
    return path;
  }
}

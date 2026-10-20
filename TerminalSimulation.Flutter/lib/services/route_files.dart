import 'dart:convert';
import 'dart:io';
import 'package:file_selector/file_selector.dart';
import 'package:flutter/services.dart';
import 'package:flutter/foundation.dart';
import '../core/route.dart';

class RouteFiles {
  static const _type = XTypeGroup(
    label: 'CTS 路线',
    extensions: ['ctsroute'],
    mimeTypes: ['application/octet-stream', 'text/plain', 'application/json'],
  );
  static Future<RouteDocument?> open() async {
    if (!Platform.isWindows && !Platform.isLinux) {
      final bytes = await const MethodChannel(
        'cts/platform',
      ).invokeMethod<Uint8List>('openDocument');
      return bytes == null ? null : compute(_parseBytes, bytes);
    }
    final file = await openFile(acceptedTypeGroups: [_type]);
    if (file == null) return null;
    if (await file.length() > 25 * 1024 * 1024) {
      throw const FormatException('路线超过 25 MiB');
    }
    return compute(_parseBytes, await file.readAsBytes());
  }

  static Future<bool> save(RouteDocument document) async {
    final bytes = await compute(_serialize, document);
    if (bytes.length > 25 * 1024 * 1024) {
      throw const FormatException('路线超过 25 MiB');
    }
    final name = 'route-${DateTime.now().millisecondsSinceEpoch}.ctsroute';
    if (!Platform.isWindows && !Platform.isLinux) {
      return await const MethodChannel('cts/platform').invokeMethod<bool>(
            'saveDocument',
            {'bytes': bytes, 'name': name},
          ) ??
          false;
    }
    final destination = await getSaveLocation(
      suggestedName: name,
      acceptedTypeGroups: [_type],
    );
    if (destination == null) return false;
    final temporary = File(
      '${destination.path}.tmp-${DateTime.now().microsecondsSinceEpoch}',
    );
    try {
      await temporary.writeAsBytes(bytes, flush: true);
      await temporary.rename(destination.path);
    } finally {
      if (await temporary.exists()) await temporary.delete();
    }
    return true;
  }

  static RouteDocument _parseBytes(Uint8List bytes) =>
      RouteDocument.parse(utf8.decode(bytes));
  static Uint8List _serialize(RouteDocument route) =>
      Uint8List.fromList(utf8.encode(route.serialize()));
}

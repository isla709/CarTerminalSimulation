import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'package:flutter/services.dart';
import '../core/route.dart';

class AppStorage {
  AppStorage(this.directory);
  final Directory directory;
  Future<void> _tail = Future.value();
  static Future<AppStorage> open() async {
    String? root;
    if (Platform.isWindows) {
      root = Platform.environment['LOCALAPPDATA'];
    } else if (Platform.isLinux && Platform.environment['HOME'] != null) {
      root =
          Platform.environment['XDG_DATA_HOME'] ??
          '${Platform.environment['HOME']}/.local/share';
    } else {
      root = await const MethodChannel(
        'cts/platform',
      ).invokeMethod<String>('dataDirectory');
    }
    if (root == null || root.isEmpty) {
      throw const FileSystemException('无法确定应用数据目录');
    }
    final dir = Directory('$root/TerminalSimulation.Flutter');
    await dir.create(recursive: true);
    return AppStorage(dir);
  }

  Future<Map<String, dynamic>?> read(String name) async {
    await _tail;
    final file = _file(name);
    if (!await file.exists()) {
      final backup = File('${file.path}.bak');
      if (!await backup.exists()) return null;
      if (await backup.length() > 25 * 1024 * 1024) {
        throw const FormatException('备份文件过大');
      }
      return jsonDecode(await backup.readAsString()) as Map<String, dynamic>;
    }
    if (await file.length() > 25 * 1024 * 1024) {
      throw const FormatException('数据文件过大');
    }
    return jsonDecode(await file.readAsString()) as Map<String, dynamic>;
  }

  File _file(String name) {
    if (!RegExp(r'^[a-zA-Z0-9_.-]+$').hasMatch(name)) {
      throw const FormatException('无效数据文件名');
    }
    return File('${directory.path}/$name');
  }

  Future<void> write(String name, Map<String, dynamic> data) {
    final snapshot = const JsonEncoder.withIndent('  ').convert(data);
    final result = _tail.then((_) async {
      final file = _file(name), temp = File('${_file(name).path}.tmp');
      try {
        await temp.writeAsString(snapshot, flush: true);
        // POSIX rename replaces atomically. On Windows retain a backup across replacement.
        if (Platform.isWindows && await file.exists()) {
          await file.copy('${file.path}.bak');
        }
        await temp.rename(file.path);
      } catch (_) {
        if (await temp.exists()) await temp.delete();
        rethrow;
      }
    });
    _tail = result.then<void>((_) {}, onError: (Object _, StackTrace _) {});
    return result;
  }
}

class MapHistory {
  MapHistory(this.storage);
  final AppStorage storage;
  Future<void> _tail = Future.value();
  static const _name = 'map-history.json';
  Future<Map<String, dynamic>> read() async {
    await _tail;
    return await storage.read(_name) ??
        {'searches': <dynamic>[], 'routes': <dynamic>[]};
  }

  Future<void> _mutate(void Function(Map<String, dynamic>) change) {
    final result = _tail.then((_) async {
      final data =
          await storage.read(_name) ??
          {'searches': <dynamic>[], 'routes': <dynamic>[]};
      change(data);
      await storage.write(_name, data);
    });
    _tail = result.then<void>((_) {}, onError: (Object _, StackTrace _) {});
    return result;
  }

  Future<void> addSearch(String query) => _mutate((data) {
    query = query.trim();
    if (query.length < 2 || query.length > 120) {
      throw const FormatException('搜索词需为 2–120 字符');
    }
    final list = List<dynamic>.from(data['searches']);
    list.removeWhere(
      (e) => e['query'].toString().toLowerCase() == query.toLowerCase(),
    );
    list.insert(0, {
      'id': DateTime.now().microsecondsSinceEpoch.toString(),
      'query': query,
      'createdAtUtc': DateTime.now().toUtc().toIso8601String(),
    });
    data['searches'] = list.take(50).toList();
  });
  Future<void> addRoute(
    String name,
    RouteDocument route, {
    bool automatic = false,
  }) => _mutate((data) {
    name = name.trim();
    if (name.isEmpty || name.length > 120) {
      throw const FormatException('路线名称需为 1–120 字符');
    }
    final list = List<dynamic>.from(data['routes']);
    final matches = list
        .where(
          (e) => route.sameShape(
            RouteDocument.fromJson(Map<String, dynamic>.from(e['route'])),
          ),
        )
        .toList();
    final existing = matches.firstOrNull;
    list.removeWhere(matches.contains);
    list.insert(0, {
      'id': existing?['id'] ?? DateTime.now().microsecondsSinceEpoch.toString(),
      'name': automatic && existing != null ? existing['name'] : name,
      'createdAtUtc': DateTime.now().toUtc().toIso8601String(),
      'route': route.toJson(),
    });
    data['routes'] = list.take(30).toList();
  });
  Future<void> rename(String id, String name) => _mutate((data) {
    name = name.trim();
    if (name.isEmpty || name.length > 120) throw const FormatException('名称无效');
    final entry = (data['routes'] as List)
        .where((e) => e['id'] == id)
        .firstOrNull;
    if (entry == null) throw StateError('路线已不存在');
    entry['name'] = name;
  });
  Future<void> delete(String kind, String id) => _mutate((data) {
    if (!['searches', 'routes'].contains(kind)) throw ArgumentError('记录类型无效');
    (data[kind] as List).removeWhere((e) => e['id'] == id);
  });
  Future<void> clear(String kind) => _mutate((data) {
    if (!['searches', 'routes'].contains(kind)) throw ArgumentError('记录类型无效');
    data[kind] = <dynamic>[];
  });
}

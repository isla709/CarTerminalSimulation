import 'dart:async';
import 'dart:convert';
import '../core/geo.dart';
import '../core/route.dart';
import '../core/vehicle_driver.dart';
import '../core/vehicle_options.dart';
import 'app_model.dart';
import 'map_service.dart';
import 'storage.dart';

/// Tencent GL host bridge. A closed/replaced document cannot receive replies.
class MapSession {
  MapSession({
    required this.model,
    required this.token,
    required this.execute,
    required this.openRoute,
    required this.saveRoute,
  }) {
    model.addListener(_positionChanged);
  }
  final AppModel model;
  final String token;
  final Future<void> Function(String) execute;
  final Future<RouteDocument?> Function() openRoute;
  final Future<bool> Function(RouteDocument) saveRoute;
  final MapService service = MapService();
  final Map<String, int> _requests = {};
  bool _closed = false, ready = false;
  bool _wasSimulating = false;
  String _lastPosition = '';
  VehicleTelemetry? _lastTelemetry;
  late final history = model.storage == null
      ? null
      : MapHistory(model.storage!);
  Future<void> _mutations = Future.value();
  void documentLoading() {
    ready = false;
    _lastPosition = '';
    _lastTelemetry = null;
    _requests.updateAll((_, value) => value + 1);
    for (final lane in ['search', 'suggest', 'route', 'locate']) {
      service.cancel(lane);
    }
  }

  Future<void> call(String function, dynamic payload) async {
    if (_closed) return;
    // Function names are internal constants, never supplied by a document.
    await execute('$function?.(${jsonEncode(payload)});');
  }

  Future<void> _reply(String function, Map<String, dynamic> payload) =>
      call('window.ctsMap.$function', payload);
  void _positionChanged() {
    if (_closed || !ready) return;
    final telemetry = model.vehicleTelemetry;
    if (telemetry != null && !identical(telemetry, _lastTelemetry)) {
      _lastTelemetry = telemetry;
      unawaited(
        call('window.ctsVehicleUi.render', {
          'telemetry': telemetry.toJson(),
          'coordinateSystem': model.coordinateSystem.name,
        }).catchError((Object _) {}),
      );
    }
    final current = '${model.latitude},${model.longitude},${model.direction}';
    if (_lastPosition != current) {
      _lastPosition = current;
      final system = model.coordinateSystem;
      unawaited(
        execute(
          'window.updateCarLocationLogical?.(${model.latitude},${model.longitude},${model.direction},${jsonEncode(system.name)});',
        ).catchError((Object _) {}),
      );
    }
    if (_wasSimulating && !model.simulating) {
      unawaited(
        execute('window.simulationFinished?.();').catchError((Object _) {}),
      );
    }
    _wasSimulating = model.simulating;
  }

  Future<void> appearance({
    required bool glass,
    required bool reduceMotion,
    required bool dark,
  }) => call('window.ctsHostAppearance', {
    'glass': glass,
    'reduceMotion': reduceMotion,
    'dark': dark,
  });

  Future<void> receive(String raw) async {
    if (_closed || raw.length > 26 * 1024 * 1024) return;
    try {
      final envelope = jsonDecode(raw);
      if (envelope is! Map ||
          envelope['token'] != token ||
          envelope['message'] is! String) {
        return;
      }
      final data = Map<String, dynamic>.from(jsonDecode(envelope['message']));
      final type = data['type'];
      if (type is! String) return;
      if ([
        'tencent_search',
        'tencent_suggest',
        'tencent_route',
        'request_network_location',
      ].contains(type)) {
        await _serviceRequest(type, data);
        return;
      }
      // File dialogs, history and simulation commands retain document order.
      final next = _mutations.then((_) => _command(type, data));
      _mutations = next.then<void>(
        (_) {},
        onError: (Object _, StackTrace _) {},
      );
      await next;
    } catch (_) {
      if (!_closed) model.log('地图', '地图请求无效或操作失败');
    }
  }

  Future<void> _command(String type, Map<String, dynamic> data) async {
    if (_closed) return;
    switch (type) {
      case 'request_map_settings':
        ready = true;
        final settings = await model.storage?.read('map-settings.json') ?? {};
        await call('window.initializeMapSettings', {
          'defaultLocation': settings['defaultLocation'] ?? 'auto',
          'mapStyle': settings['mapStyle'] ?? 'default',
          'pathStyle': settings['pathStyle'] ?? 'theme-neon-blue',
        });
        if (model.route != null) {
          await call('window.loadRouteFile', model.route!.toJson());
        }
        await call('window.ctsRestoreSimulation', {
          'running': model.simulating,
          'coordinateSystem': model.coordinateSystem.name,
        });
        _positionChanged();
      case 'save_map_settings':
        final location = '${data['defaultLocation'] ?? 'auto'}';
        final style = '${data['mapStyle'] ?? ''}',
            path = '${data['pathStyle'] ?? ''}';
        if (!RegExp(r'^[a-z]{2,20}$').hasMatch(location) ||
            !['default', 'style1'].contains(style) ||
            ![
              'theme-neon-blue',
              'theme-neon-purple',
              'theme-solid-red',
              'theme-solid-orange',
            ].contains(path)) {
          throw const FormatException('地图设置无效');
        }
        await model.storage?.write('map-settings.json', {
          'defaultLocation': location,
          'mapStyle': style,
          'pathStyle': path,
        });
      case 'map_history':
        await _history(data);
      case 'apply':
        final point = GeoPoint.fromJson(data);
        model.coordinateSystem = CoordinateSystem.values.firstWhere(
          (v) => v.name == data['coordinateSystem'],
        );
        model.stopSimulation();
        model.latitude = point.lat;
        model.longitude = point.lng;
        model.changed();
      case 'simulate_path':
        try {
          final route = RouteDocument.fromJson({
            'formatVersion': 1,
            'mode': 'draw',
            'coordinateSystem': data['coordinateSystem'],
            'speed': data['speed'],
            'points': data['path'],
            'vehicle': data['vehicle'],
          });
          model.applyRoute(route);
          final accepted = await model.startSimulation();
          await _reply('simulationAccepted', {
            'accepted': accepted,
            if (!accepted) 'message': '模拟启动已取消',
          });
        } catch (e) {
          await _reply('simulationAccepted', {
            'accepted': false,
            'message': '$e',
          });
        }
      case 'simulate_speed':
        final value = (data['speed'] as num).toDouble();
        if (!value.isFinite || value < 1 || value > 300) {
          throw const FormatException('速度无效');
        }
        model.speed = value;
        model.changed();
      case 'stop_simulation':
        model.stopSimulation();
        model.changed();
      case 'save_map_route':
        try {
          final route = RouteDocument.fromJson(
            Map<String, dynamic>.from(data['route']),
          );
          if (await saveRoute(route)) {
            await execute(
              'window.routeFileOperationResult?.("save",true,"路线已保存");',
            );
          }
        } catch (_) {
          await execute(
            'window.routeFileOperationResult?.("save",false,"保存失败，请检查文件权限和路线内容");',
          );
        }
      case 'open_map_route':
        try {
          final route = await openRoute();
          if (route != null && !_closed) {
            model.applyRoute(route);
            await call('window.loadRouteFile', route.toJson());
          }
        } catch (_) {
          await execute(
            'window.routeFileOperationResult?.("open",false,"打开失败，请检查路线文件格式");',
          );
        }
      case 'vehicle_settings':
        try {
          final action = data['action'] ?? 'get';
          if (!['get', 'save'].contains(action)) {
            throw const FormatException('未知车辆设置操作');
          }
          final options = action == 'save'
              ? VehicleOptions.fromJson(checkedObject(data['options']))
              : VehicleOptions.fromJson(
                  await model.storage?.read('map-vehicle.json') ?? {},
                );
          if (action == 'save') {
            if (model.storage == null) throw StateError('应用存储不可用');
            await model.storage!.write('map-vehicle.json', options.toJson());
          }
          await _reply('receiveVehicleSettings', {
            'succeeded': true,
            'action': action,
            'options': options.toJson(),
            'presets': VehicleOptions.presets.map(
              (key, value) => MapEntry(key, value.toJson()),
            ),
            'trafficEventCatalog': trafficCatalog
                .map((e) => e.toJson())
                .toList(),
          });
        } catch (e) {
          await _reply('receiveVehicleSettings', {
            'succeeded': false,
            'action': data['action'],
            'message': '$e',
          });
        }
      case 'vehicle_control':
        model.setVehicleControls(
          VehicleControls.fromJson(checkedObject(data['controls'])),
        );
      case 'map_log':
        // SDK errors may include script URLs; omit details to protect entered keys.
        model.log('地图', '地图页面状态已更新');
    }
  }

  Future<void> _serviceRequest(String type, Map<String, dynamic> data) async {
    final locate = type == 'request_network_location';
    final operation = locate ? 'locate' : type.split('_').last;
    final callback = switch (operation) {
      'route' => 'receiveRoutes',
      'suggest' => 'receiveSuggestions',
      _ => 'receiveSearch',
    };
    final id = (_requests[operation] ?? 0) + 1;
    _requests[operation] = id;
    bool current() => !_closed && _requests[operation] == id;
    try {
      final result = await service.request(
        operation,
        data,
        key: model.mapCredentials.serviceKey,
      );
      if (!current()) return;
      if (locate) {
        await call('window.applyNetworkLocation', result);
      } else {
        await _reply(callback, {
          'requestId': data['requestId'],
          'query': data['query'],
          'succeeded': true,
          ...result,
        });
      }
    } catch (e) {
      if (!current()) return;
      if (locate) {
        await execute('window.networkLocationFailed?.();');
      } else {
        await _reply(callback, {
          'requestId': data['requestId'],
          'query': data['query'],
          'succeeded': false,
          'message': '$e',
          'results': [],
          'routes': [],
        });
      }
    }
  }

  Future<void> _history(Map<String, dynamic> data) async {
    final store = history, action = data['action'], id = '${data['id'] ?? ''}';
    try {
      if (store == null) throw StateError('应用存储不可用');
      final kind = switch (data['kind']) {
        'route' => 'routes',
        'search' => 'searches',
        _ => '',
      };
      switch (action) {
        case 'search':
          await store.addSearch('${data['query'] ?? ''}');
        case 'record':
          await store.addRoute(
            '${data['name'] ?? '轨迹'}',
            RouteDocument.fromJson(Map<String, dynamic>.from(data['route'])),
            automatic: data['automatic'] == true,
          );
        case 'rename':
          await store.rename(id, '${data['name'] ?? ''}');
        case 'delete':
          await store.delete(kind, id);
        case 'clear':
          await store.clear(kind);
        case 'load':
          final entries = (await store.read())['routes'] as List;
          final entry = entries.where((e) => e['id'] == id).firstOrNull;
          if (entry == null) throw StateError('路线记录已不存在');
          final route = RouteDocument.fromJson(
            Map<String, dynamic>.from(entry['route']),
          );
          model.applyRoute(route);
          await call('window.loadRouteFile', route.toJson());
        case 'list':
          break;
        default:
          throw const FormatException('未知历史操作');
      }
      final snapshot = await store.read();
      final routes = (snapshot['routes'] as List).map((e) {
        final r = RouteDocument.fromJson(Map<String, dynamic>.from(e['route']));
        return {
          'id': e['id'],
          'name': e['name'],
          'createdAtUtc': e['createdAtUtc'],
          'coordinateSystem': r.coordinateSystem.name,
          'mode': r.mode,
          'pointCount': r.points.length,
          'speed': '${r.speed}',
          'speedDescription': r.vehicle?['enabled'] == true
              ? '真实驾驶配置'
              : '固定 ${r.speed} km/h',
        };
      }).toList();
      await _reply('receiveHistory', {
        'succeeded': true,
        'action': action,
        'automatic': data['automatic'] == true,
        'searches': snapshot['searches'],
        'routes': routes,
      });
    } catch (_) {
      await _reply('receiveHistory', {
        'succeeded': false,
        'action': action,
        'message': '记录操作失败，请检查数据和目录权限',
      });
    }
  }

  void close() {
    _closed = true;
    model.removeListener(_positionChanged);
    service.close();
  }
}

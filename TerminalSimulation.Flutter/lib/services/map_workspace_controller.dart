import 'dart:async';
import 'dart:convert';
import 'package:flutter/foundation.dart';
import '../core/geo.dart';
import '../core/route.dart';
import '../core/vehicle_options.dart';
import 'app_model.dart';
import 'map_service.dart';
import 'storage.dart';

/// Flutter owns editing state. The document only renders GCJ-02 geometry.
class MapWorkspaceController extends ChangeNotifier {
  MapWorkspaceController({
    required this.model,
    required this.token,
    required this.execute,
  }) {
    model.addListener(_modelChanged);
    final route = model.route;
    if (route != null) _restore(route);
  }
  final AppModel model;
  final String token;
  final Future<void> Function(String) execute;
  final service = MapService();
  late final history = model.storage == null
      ? null
      : MapHistory(model.storage!);
  bool _closed = false, ready = false, locating = false, planning = false;
  bool follow = true, use3d = false, searching = false;
  String mode = 'navigation',
      message = '',
      mapStyle = 'default',
      pathStyle = 'theme-neon-blue',
      defaultLocation = 'auto';
  String? error, renderError;
  CoordinateSystem system = CoordinateSystem.gcj02;
  double speed = 60;
  VehicleOptions vehicle = VehicleOptions();
  List<GeoPoint> points = [];
  GeoPoint? start, end;
  String startName = '', endName = '';
  List<Map<String, dynamic>> results = [],
      alternatives = [],
      searches = [],
      records = [];
  int activeRoute = 0,
      _document = 0,
      _search = 0,
      _planning = 0,
      _locating = 0,
      _revision = 0,
      _sentRevision = -1;
  bool _dark = false, _reduceMotion = false;
  String? _lastPosition;
  bool _wasPreparing = false;
  bool _wasRunning = false, _dirty = false, _sending = false;
  bool _wasRealLocation = false;
  Future<void> _writes = Future.value();
  bool get running => model.simulating || model.simulationPreparing;
  bool get canSave =>
      !model.realLocation &&
      (mode == 'single' ? points.length == 1 : points.length >= 2);
  bool get editable => !running && !model.realLocation;
  GeoPoint center = GeoPoint(39.9042, 116.4074);
  static const cities = <String, (String, double, double)>{
    'beijing': ('北京', 39.9042, 116.4074),
    'shanghai': ('上海', 31.2304, 121.4737),
    'guangzhou': ('广州', 23.1291, 113.2644),
    'shenzhen': ('深圳', 22.5431, 114.0579),
    'chengdu': ('成都', 30.5728, 104.0668),
    'chongqing': ('重庆', 29.563, 106.5516),
    'hangzhou': ('杭州', 30.2741, 120.1551),
    'wuhan': ('武汉', 30.5928, 114.3055),
    'xian': ('西安', 34.3416, 108.9398),
    'nanjing': ('南京', 32.0603, 118.7969),
  };
  Future<void> initialize() async {
    try {
      final settings = await model.storage?.read('map-settings.json') ?? {};
      if (_closed) return;
      mapStyle = ['default', 'style1'].contains(settings['mapStyle'])
          ? settings['mapStyle']
          : 'default';
      pathStyle = '${settings['pathStyle'] ?? 'theme-neon-blue'}';
      defaultLocation = '${settings['defaultLocation'] ?? 'auto'}';
      final saved = await model.storage?.read('map-vehicle.json') ?? {};
      if (_closed) return;
      if (model.route == null) vehicle = VehicleOptions.fromJson(saved);
      await refreshHistory();
      changed();
      if (ready) await _initialCamera();
    } catch (_) {
      report('读取地图设置失败，已使用默认设置。');
    }
  }

  void report(String text) {
    if (_closed) return;
    error = text;
    notifyListeners();
  }

  void dismissError() {
    if (_closed) return;
    error = null;
    notifyListeners();
  }

  void changed({bool geometry = true}) {
    if (_closed) return;
    if (geometry) {
      ++_revision;
    }
    _queueRender();
    notifyListeners();
  }

  Future<void> _call(String command, Object? data) async {
    if (_closed || !ready) return;
    await execute('window.ctsCanvas.$command?.(${jsonEncode(data)});');
  }

  void _queueRender() {
    _dirty = true;
    if (!_sending && ready) unawaited(_flush());
  }

  Future<void> _flush() async {
    _sending = true;
    try {
      while (_dirty && ready && !_closed) {
        _dirty = false;
        final generation = _document, revision = _revision;
        await _call('render', _snapshot());
        if (generation == _document) _sentRevision = revision;
      }
    } catch (_) {
      if (!_closed) report('地图更新失败，可重新载入。');
    } finally {
      _sending = false;
    }
  }

  Map<String, dynamic> _snapshot() {
    Map<String, dynamic> display(GeoPoint p) =>
        Geo.convert(p, system, CoordinateSystem.gcj02).toJson();
    return {
      'revision': _revision,
      if (_sentRevision != _revision) ...{
        'mode': model.realLocation ? 'live' : mode,
        'points': model.realLocation ? [] : points.map(display).toList(),
        'alternatives': model.realLocation
            ? []
            : alternatives.map((r) => r['points']).toList(),
        'active': activeRoute,
        'start': model.realLocation || start == null ? null : display(start!),
        'end': model.realLocation || end == null ? null : display(end!),
      },
      'car': Geo.convert(
        GeoPoint(model.latitude, model.longitude),
        model.coordinateSystem,
        CoordinateSystem.gcj02,
      ).toJson(),
      'bearing': model.direction,
      'showCar': !model.realLocation || model.hasDeviceFix,
      'follow': follow && (model.simulating || model.hasDeviceFix),
      'mapStyle': mapStyle,
      'pathStyle': pathStyle,
      'use3d': use3d,
    };
  }

  void documentLoading() {
    ready = false;
    if (error == renderError) error = null;
    renderError = null;
    ++_document;
    ++_search;
    ++_planning;
    ++_locating;
    planning = locating = false;
    searching = false;
    for (final lane in ['search', 'suggest', 'route', 'locate']) {
      service.cancel(lane);
    }
    _lastPosition = null;
    _sentRevision = -1;
    if (!_closed) notifyListeners();
  }

  Future<void> receive(String raw) async {
    if (_closed || raw.length > 8192) return;
    try {
      final envelope = jsonDecode(raw);
      if (envelope is! Map ||
          envelope['token'] != token ||
          envelope['message'] is! String) {
        return;
      }
      final data = jsonDecode(envelope['message']);
      if (data is! Map || data['version'] != 1) return;
      switch (data['type']) {
        case 'ready':
          ready = true;
          renderError = null;
          error = null;
          _queueRender();
          notifyListeners();
          await appearance(dark: _dark, reduceMotion: _reduceMotion);
          await _initialCamera();
        case 'point':
          if (!editable) return;
          select(GeoPoint.fromJson(Map<String, dynamic>.from(data)));
        case 'view':
          center = GeoPoint.fromJson(Map<String, dynamic>.from(data));
          if (data['dragged'] == true && follow) {
            follow = false;
            changed(geometry: false);
          }
        case 'error':
          renderError = data['reason'] == 'webgl-unavailable'
              ? '此设备无法启用 WebGL，无法显示地图。请检查系统与显卡驱动，或更换设备后重试。'
              : '腾讯地图加载或渲染失败，请检查网络与地图授权后重试。';
          report(renderError!);
      }
    } catch (_) {
      if (!_closed) model.log('地图', '忽略无效地图渲染事件');
    }
  }

  Future<void> _initialCamera() async {
    if (model.realLocation) {
      if (model.hasDeviceFix) resumeFollow();
      return;
    }
    if (points.isNotEmpty) {
      await fit();
      return;
    }
    if (defaultLocation != 'auto' && cities.containsKey(defaultLocation)) {
      final city = cities[defaultLocation]!;
      await fly(
        Geo.convert(
          GeoPoint(city.$2, city.$3),
          CoordinateSystem.wgs84,
          CoordinateSystem.gcj02,
        ),
      );
    } else {
      await locate(silent: true);
    }
  }

  void _modelChanged() {
    if (_closed) return;
    final position =
        '${model.latitude},${model.longitude},${model.direction},${model.hasDeviceFix}';
    if (_lastPosition != position) {
      _lastPosition = position;
      _queueRender();
    }
    final sourceChanged = _wasRealLocation != model.realLocation;
    if (sourceChanged) {
      ++_revision;
      follow = true;
      _cancelRoute();
      ++_search;
      service.cancel('search');
      service.cancel('suggest');
      service.cancel('locate');
      searching = locating = false;
    }
    final stateChanged =
        sourceChanged ||
        _wasRunning != running ||
        _wasPreparing != model.simulationPreparing;
    _wasRealLocation = model.realLocation;
    if (_wasRunning && !running) message = '模拟已停止';
    _wasRunning = running;
    _wasPreparing = model.simulationPreparing;
    // Position ticks update the canvas; controls only rebuild on state changes.
    if (stateChanged) {
      _queueRender();
      notifyListeners();
    }
  }

  void _cancelRoute() {
    ++_planning;
    planning = false;
    service.cancel('route');
  }

  void setMode(String value) {
    if (!editable ||
        !['navigation', 'draw', 'single'].contains(value) ||
        mode == value) {
      return;
    }
    clear();
    mode = value;
    changed();
  }

  void clear() {
    if (!editable) return;
    _cancelRoute();
    points = [];
    start = end = null;
    startName = endName = '';
    alternatives = [];
    activeRoute = 0;
    message = '';
    changed();
  }

  void select(GeoPoint displayPoint, {String name = '', String? role}) {
    if (!editable) return;
    final converted = Geo.convert(displayPoint, CoordinateSystem.gcj02, system);
    final point = GeoPoint(converted.lat, converted.lng, name: name);
    if (role != null) mode = 'navigation';
    switch (mode) {
      case 'single':
        points = [point];
        message = '单点已选择，点击应用位置';
      case 'draw':
        if (points.length >= 100000) {
          report('轨迹最多包含十万个点');
          return;
        }
        points = [...points, point];
        message = '已绘制 ${points.length} 个点';
      default:
        _cancelRoute();
        alternatives = [];
        points = [];
        if (role == 'start' || role == null && (start == null || end != null)) {
          start = point;
          startName = name;
          if (role == null) {
            end = null;
            endName = '';
          }
        } else {
          end = point;
          endName = name;
        }
        message = end == null ? '选择终点' : '起终点已就绪，可规划路线';
    }
    changed();
  }

  void undo() {
    if (editable && points.isNotEmpty) {
      points = points.sublist(0, points.length - 1);
      changed();
    }
  }

  void removeEndpoint(bool first) {
    if (!editable) return;
    _cancelRoute();
    points = [];
    alternatives = [];
    if (first) {
      start = null;
      startName = '';
    } else {
      end = null;
      endName = '';
    }
    changed();
  }

  void swap() {
    if (!editable) return;
    final p = start, n = startName;
    start = end;
    startName = endName;
    end = p;
    endName = n;
    _cancelRoute();
    alternatives = [];
    points = [];
    changed();
  }

  void setSystem(CoordinateSystem value, {required bool convert}) {
    if (!editable || value == system) return;
    _cancelRoute();
    if (convert) {
      GeoPoint cv(GeoPoint p) {
        final converted = Geo.convert(p, system, value);
        return GeoPoint(converted.lat, converted.lng, name: p.name);
      }

      points = points.map(cv).toList();
      if (start != null) start = cv(start!);
      if (end != null) end = cv(end!);
    }
    system = value;
    alternatives = [];
    changed();
  }

  void setSpeed(double value) {
    if (!value.isFinite || value < 1 || value > 300) {
      throw const FormatException('速度应为 1–300 km/h');
    }
    speed = value;
    if (running && !vehicle.enabled) {
      model.speed = value;
      model.changed();
    }
  }

  Future<void> search(String query, {bool suggest = false}) async {
    if (model.realLocation) return;
    query = query.trim();
    final id = ++_search, generation = _document;
    service.cancel('search');
    service.cancel('suggest');
    searching = false;
    if (query.length < 2) {
      results = [];
      changed(geometry: false);
      return;
    }
    if (query.length > 120) {
      report('搜索词最多 120 个字符');
      return;
    }
    searching = true;
    results = [];
    changed(geometry: false);
    try {
      final response = await service.request(suggest ? 'suggest' : 'search', {
        'query': query,
        ...center.toJson(),
      }, key: model.mapCredentials.serviceKey);
      if (_closed || id != _search || generation != _document) return;
      results = (response['results'] as List)
          .map((v) => Map<String, dynamic>.from(v))
          .toList();
      if (!suggest) {
        if (history != null) {
          await history!.addSearch(query);
          await refreshHistory();
        }
        message = results.isEmpty ? '没有找到地点' : '找到 ${results.length} 个地点';
      }
      changed(geometry: false);
    } catch (e) {
      if (!_closed && id == _search && generation == _document) report('$e');
    } finally {
      if (!_closed && id == _search && generation == _document) {
        searching = false;
        changed(geometry: false);
      }
    }
  }

  void dismissResults() {
    ++_search;
    service.cancel('search');
    service.cancel('suggest');
    results = [];
    searching = false;
    changed(geometry: false);
  }

  static GeoPoint place(Map<String, dynamic> value) => GeoPoint(
    ((value['latitude'] ?? value['lat']) as num).toDouble(),
    ((value['longitude'] ?? value['lng']) as num).toDouble(),
  );
  Future<void> plan() async {
    if (!editable || start == null || end == null) return;
    final id = ++_planning, generation = _document;
    planning = true;
    error = null;
    changed(geometry: false);
    try {
      final response = await service.request('route', {
        'start': Geo.convert(start!, system, CoordinateSystem.gcj02).toJson(),
        'end': Geo.convert(end!, system, CoordinateSystem.gcj02).toJson(),
      }, key: model.mapCredentials.serviceKey);
      if (_closed || id != _planning || generation != _document || !editable) {
        return;
      }
      alternatives = (response['routes'] as List)
          .map((v) => Map<String, dynamic>.from(v))
          .toList();
      chooseRoute(0);
      await fit();
    } catch (e) {
      if (!_closed && id == _planning && generation == _document) report('$e');
    } finally {
      if (!_closed && id == _planning) {
        planning = false;
        changed(geometry: false);
      }
    }
  }

  void chooseRoute(int index) {
    if (!editable || index < 0 || index >= alternatives.length) return;
    activeRoute = index;
    points = (alternatives[index]['points'] as List)
        .map(
          (v) => Geo.convert(
            place(Map<String, dynamic>.from(v)),
            CoordinateSystem.gcj02,
            system,
          ),
        )
        .toList();
    message = '路线已就绪 · ${points.length} 个点';
    changed();
  }

  Future<void> fly(GeoPoint displayPoint, {double zoom = 13}) =>
      _call('camera', {'point': displayPoint.toJson(), 'zoom': zoom});
  Future<void> fit() async {
    if (model.realLocation) {
      if (model.hasDeviceFix) resumeFollow();
      return;
    }
    if (ready && points.isNotEmpty) await _call('fit', null);
  }

  Future<void> locate({bool silent = false}) async {
    if (model.realLocation) {
      if (model.hasDeviceFix) {
        resumeFollow();
      } else if (!silent) {
        report(model.locationStatus);
      }
      return;
    }
    final id = ++_locating, generation = _document;
    locating = true;
    changed(geometry: false);
    try {
      final result = await service.request(
        'locate',
        {},
        key: model.mapCredentials.serviceKey,
      );
      if (_closed || id != _locating || generation != _document) return;
      center = Geo.convert(
        GeoPoint.fromJson(result),
        CoordinateSystem.wgs84,
        CoordinateSystem.gcj02,
      );
      if (!silent || points.isEmpty && start == null && end == null) {
        await fly(center, zoom: 11);
      }
      message = '网络近似位置 · ${result['label'] ?? ''}';
    } catch (_) {
      if (!_closed && id == _locating && !silent) {
        report('网络定位暂不可用，可选择默认城市或手动选点。');
      }
    } finally {
      if (!_closed && id == _locating) {
        locating = false;
        changed(geometry: false);
      }
    }
  }

  void resumeFollow() {
    if (model.realLocation && !model.hasDeviceFix) return;
    follow = true;
    changed();
    unawaited(
      fly(
        Geo.convert(
          GeoPoint(model.latitude, model.longitude),
          model.coordinateSystem,
          CoordinateSystem.gcj02,
        ),
        zoom: 15,
      ),
    );
  }

  RouteDocument document() => RouteDocument(
    points: List.unmodifiable(points),
    mode: mode,
    coordinateSystem: system,
    speed: speed,
    vehicle: vehicle.toJson(),
    start: start,
    end: end,
  );
  void _restore(RouteDocument route) {
    mode = route.mode;
    system = route.coordinateSystem;
    speed = route.speed;
    points = List.of(route.points);
    start = route.start ?? (mode == 'navigation' ? points.first : null);
    end = route.end ?? (mode == 'navigation' ? points.last : null);
    startName = start?.name ?? '';
    endName = end?.name ?? '';
    vehicle = VehicleOptions.fromJson(route.vehicle ?? {});
  }

  Future<void> load(RouteDocument route) async {
    if (!editable) throw StateError('请先停止模拟');
    _cancelRoute();
    _restore(route);
    alternatives = [];
    model.applyRoute(route);
    message = '路线已恢复';
    changed();
    await fit();
  }

  Future<void> startSimulation() async {
    if (model.realLocation || running || !canSave) return;
    final route = document();
    model.applyRoute(route);
    if (mode == 'single') {
      message = '位置已应用';
      model.changed();
      changed();
      return;
    }
    follow = true;
    await model.startSimulation();
    if (_closed) return;
    if (history != null && model.simulating) {
      await history!.addRoute('模拟路线', route, automatic: true);
      await refreshHistory();
    }
    changed();
  }

  void stop() {
    model.stopSimulation();
    model.changed();
    message = '模拟已停止';
    changed(geometry: false);
  }

  Future<void> refreshHistory() async {
    final data = await history?.read();
    if (_closed || data == null) return;
    searches = (data['searches'] as List)
        .map((v) => Map<String, dynamic>.from(v))
        .toList();
    records = (data['routes'] as List)
        .map((v) => Map<String, dynamic>.from(v))
        .toList();
    changed(geometry: false);
  }

  Future<void> record(String name) async {
    if (history == null) throw StateError('应用存储不可用');
    await history!.addRoute(name, document());
    await refreshHistory();
  }

  Future<void> saveVehicle(VehicleOptions options) async {
    if (!editable) throw StateError('请先停止模拟');
    vehicle = options;
    changed(geometry: false);
    if (model.storage != null) {
      await _write('map-vehicle.json', options.toJson());
    }
  }

  Future<void> settings({
    required String city,
    required String style,
    required String path,
  }) async {
    defaultLocation = city;
    mapStyle = style;
    pathStyle = path;
    changed();
    if (model.storage != null) {
      await _write('map-settings.json', {
        'defaultLocation': city,
        'mapStyle': style,
        'pathStyle': path,
      });
    }
    await _initialCamera();
  }

  Future<void> _write(String name, Map<String, dynamic> data) {
    final next = _writes.then((_) => model.storage!.write(name, data));
    _writes = next.then<void>((_) {}, onError: (Object _, StackTrace _) {});
    return next;
  }

  Future<void> appearance({required bool dark, required bool reduceMotion}) {
    _dark = dark;
    _reduceMotion = reduceMotion;
    return _call('appearance', {'dark': dark, 'reduceMotion': reduceMotion});
  }

  @override
  void dispose() {
    _closed = true;
    ++_document;
    model.removeListener(_modelChanged);
    service.close();
    super.dispose();
  }
}

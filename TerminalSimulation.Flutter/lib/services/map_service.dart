import 'dart:async';
import 'dart:convert';
import '../core/geo.dart';
import 'http_tool.dart';

/// Requests use independent lanes. Replacing a suggestion never cancels a route.
class MapService {
  final Map<String, HttpTool> _requests = {};
  bool _closed = false;
  void cancel(String lane) => _requests.remove(lane)?.cancel();
  void close() {
    _closed = true;
    for (final request in _requests.values) {
      request.cancel();
    }
    _requests.clear();
  }

  Future<Map<String, dynamic>> _get(String lane, Uri uri) async {
    if (_closed) throw StateError('地图服务已关闭');
    final tool = _requests[lane];
    if (tool == null) throw StateError('地图请求已取消');
    try {
      final response = await tool.send(
        method: 'GET',
        url: uri,
        headers: {
          'Accept': 'application/json',
          'Accept-Language': 'zh-CN,zh;q=0.9',
          'User-Agent': 'CarTerminalSimulation/Flutter-0.1.0',
        },
        timeout: const Duration(seconds: 18),
      );
      if (response.status != 200) {
        throw StateError('地图服务返回 HTTP ${response.status}');
      }
      final decoded = jsonDecode(utf8.decode(response.bytes));
      if (decoded is! Map<String, dynamic>) {
        throw const FormatException('地图服务响应格式无效');
      }
      return decoded;
    } on TimeoutException {
      throw StateError('地图服务请求超时，请重试');
    } on FormatException {
      throw const FormatException('地图服务返回了无效数据');
    } catch (error) {
      // Network exception strings can contain the full request URL and its key.
      if (error is StateError) rethrow;
      throw StateError('地图服务连接失败，请检查网络或服务权限');
    }
  }

  Future<Map<String, dynamic>> request(
    String operation,
    Map<String, dynamic> data, {
    String key = '',
  }) async {
    final lane = operation;
    cancel(lane);
    final tool = HttpTool();
    _requests[lane] = tool;
    try {
      if (key.trim().isEmpty) {
        throw StateError('内置地图凭据不可用，请使用构建脚本重新构建应用。');
      }
      if (operation == 'locate') return await _locate(lane, key);
      if (operation == 'route') {
        final start = GeoPoint.fromJson(data['start']),
            end = GeoPoint.fromJson(data['end']);
        final query = <String, String>{
          'output': 'json',
          'key': key,
          'get_mp': '1',
          'get_speed': '1',
          'no_step': '0',
          'policy': 'LEAST_TIME,REAL_TRAFFIC,NAV_POINT_FIRST',
          'from': _latLng(start),
          'to': _latLng(end),
          if ('${data['startPoiId'] ?? ''}'.isNotEmpty)
            'from_poi': '${data['startPoiId']}',
          if ('${data['endPoiId'] ?? ''}'.isNotEmpty)
            'to_poi': '${data['endPoiId']}',
        };
        final root = await _get(
          lane,
          Uri.https('apis.map.qq.com', '/ws/direction/v1/driving/', query),
        );
        _ensureTencent(root);
        return {'routes': parseTencentRoutes(root)};
      }
      if (!['search', 'suggest'].contains(operation)) {
        throw const FormatException('未知地图请求');
      }
      final query = '${data['query'] ?? ''}'.trim();
      if (query.length < 2 || query.length > 120) {
        throw const FormatException('搜索词需为 2–120 字符');
      }
      final center = GeoPoint.fromJson(data);
      {
        final root = await _get(
          lane,
          Uri.https(
            'apis.map.qq.com',
            operation == 'suggest'
                ? '/ws/place/v1/suggestion'
                : '/ws/place/v1/search',
            {
              'output': 'json',
              'page_size': operation == 'suggest' ? '8' : '12',
              'page_index': '1',
              'get_subpois': '1',
              'keyword': query,
              'key': key,
              if (operation == 'suggest')
                'location': _latLng(center)
              else
                'boundary': 'nearby(${_latLng(center)},1000,1)',
            },
          ),
        );
        _ensureTencent(root);
        return {'results': parseTencentPlaces(root)};
      }
    } finally {
      if (identical(_requests[lane], tool)) _requests.remove(lane);
      tool.cancel();
    }
  }

  Future<Map<String, dynamic>> _locate(String lane, String key) async {
    {
      final root = await _get(
        lane,
        Uri.https('apis.map.qq.com', '/ws/location/v1/ip', {
          'output': 'json',
          'key': key,
        }),
      );
      _ensureTencent(root);
      final result = _object(root['result']),
          raw = _object(_object(root['result'])['location']);
      final point = Geo.convert(
        GeoPoint.fromJson(raw),
        CoordinateSystem.gcj02,
        CoordinateSystem.wgs84,
      );
      final info = _object(result['ad_info']);
      return {
        ...point.toJson(),
        'accuracy': 25000,
        'label': [
          info['province'],
          info['city'],
          info['district'],
        ].whereType<String>().where((v) => v.isNotEmpty).join(' · '),
      };
    }
  }

  static String _latLng(GeoPoint p) =>
      '${p.lat.toStringAsFixed(6)},${p.lng.toStringAsFixed(6)}';
  static Map<String, dynamic> _object(dynamic v) =>
      v is Map ? Map<String, dynamic>.from(v) : {};
  static List<dynamic> _array(dynamic v) => v is List ? v : [];
  static double _number(dynamic v) {
    final n = v is num ? v.toDouble() : double.tryParse('$v');
    if (n == null || !n.isFinite) throw const FormatException('地图数值无效');
    return n;
  }

  static int _integer(dynamic v) => v == null ? 0 : _number(v).round();
  static void _ensureTencent(Map<String, dynamic> root) {
    if (root['status'] != 0) {
      throw StateError('腾讯地图服务错误 ${root['status']}，请检查 Key 类型、配额和授权');
    }
  }

  static List<Map<String, dynamic>> parseTencentPlaces(
    Map<String, dynamic> root,
  ) {
    final result = <Map<String, dynamic>>[];
    for (final raw in _array(root['data'])) {
      final p = _object(raw), info = _object(_object(raw)['ad_info']);
      try {
        final location = GeoPoint.fromJson(_object(p['location']));
        result.add({
          'id': '${p['id'] ?? ''}',
          'title': p['title'] ?? '未命名地点',
          'address': p['address'] ?? '',
          'category': p['category'] ?? '地点',
          for (final field in ['province', 'city', 'district'])
            field: info[field] ?? p[field] ?? '',
          'latitude': location.lat,
          'longitude': location.lng,
        });
      } on FormatException {
        continue;
      }
    }
    return result;
  }

  static List<Map<String, dynamic>> parseTencentRoutes(
    Map<String, dynamic> root,
  ) {
    final result = <Map<String, dynamic>>[];
    for (final raw in _array(_object(root['result'])['routes'])) {
      final route = _object(raw), line = _array(_object(raw)['polyline']);
      if (line.length < 4 || line.length.isOdd || line.length > 200000) {
        continue;
      }
      final values = line.map(_number).toList();
      for (var i = 2; i < values.length; i++) {
        values[i] = values[i - 2] + values[i] / 1000000;
      }
      final points = <Map<String, dynamic>>[];
      for (var i = 0; i < values.length; i += 2) {
        final point = GeoPoint(values[i], values[i + 1]);
        points.add({'latitude': point.lat, 'longitude': point.lng});
      }
      result.add({
        'index': result.length,
        'distance': _integer(route['distance']),
        'durationMinutes': _integer(route['duration']),
        'trafficLightCount': _integer(route['traffic_light_count']),
        'toll': route['toll'] ?? 0,
        'tags': _array(route['tags']),
        'points': points,
        'steps': _array(route['steps']).map((v) {
          final s = _object(v);
          return {
            'instruction': s['instruction'] ?? '',
            'roadName': s['road_name'] ?? '',
            'distance': _integer(s['distance']),
            'action': s['act_desc'] ?? '',
          };
        }).toList(),
      });
    }
    if (result.isEmpty) throw StateError('腾讯地图未返回可用的驾车路线');
    return result;
  }
}

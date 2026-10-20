import 'dart:convert';
import 'dart:math' as math;
import 'geo.dart';

class RouteDocument {
  RouteDocument({
    required this.points,
    this.mode = 'draw',
    this.coordinateSystem = CoordinateSystem.gcj02,
    this.speed = 60.5,
    this.vehicle,
    this.start,
    this.end,
    this.applicationVersion = 'Flutter 0.1.0',
    DateTime? savedAt,
  }) : savedAt = savedAt ?? DateTime.now().toUtc() {
    validate();
  }
  final List<GeoPoint> points;
  final String mode, applicationVersion;
  final CoordinateSystem coordinateSystem;
  final double speed;
  final Map<String, dynamic>? vehicle;
  final GeoPoint? start, end;
  final DateTime savedAt;
  void validate() {
    if (!['draw', 'navigation', 'single'].contains(mode)) {
      throw const FormatException('路线模式无效');
    }
    if (points.length > 100000 ||
        (mode == 'single' ? points.length != 1 : points.length < 2)) {
      throw const FormatException('单点模式需要一个点，路线需要 2–100000 个点');
    }
    if (!speed.isFinite || speed < 1 || speed > 300) {
      throw const FormatException('路线速度范围为 1–300 km/h');
    }
  }

  factory RouteDocument.fromJson(Map<String, dynamic> json) {
    if (json['formatVersion'] != 1) throw const FormatException('不支持的路线格式版本');
    final system = CoordinateSystem.values
        .where((s) => s.name == json['coordinateSystem'])
        .firstOrNull;
    if (system == null) throw const FormatException('路线坐标系无效');
    final points = (json['points'] as List)
        .map((p) => GeoPoint.fromJson(Map<String, dynamic>.from(p)))
        .toList();
    final rawSpeed = (json['speed'] as num? ?? 60.5).toDouble();
    return RouteDocument(
      points: points,
      mode: json['mode'] as String,
      coordinateSystem: system,
      speed: rawSpeed.isFinite ? rawSpeed.clamp(1, 300) : 60.5,
      vehicle: json['vehicle'] == null
          ? null
          : Map<String, dynamic>.from(json['vehicle']),
      start: json['start'] == null ? null : GeoPoint.fromJson(json['start']),
      end: json['end'] == null ? null : GeoPoint.fromJson(json['end']),
      applicationVersion: json['applicationVersion'] as String? ?? '',
      savedAt: DateTime.tryParse(json['savedAtUtc'] as String? ?? ''),
    );
  }
  factory RouteDocument.parse(String content) {
    if (utf8.encode(content).length > 25 * 1024 * 1024) {
      throw const FormatException('路线文件超过 25 MiB');
    }
    content = content.replaceFirst(RegExp('^\uFEFF'), '');
    final lineEnd = content.indexOf('\n');
    if (lineEnd < 0 ||
        content.substring(0, lineEnd).trimRight() != 'CTSROUTE/1') {
      throw const FormatException('不是有效的 .ctsroute 文件');
    }
    return RouteDocument.fromJson(
      jsonDecode(content.substring(lineEnd + 1)) as Map<String, dynamic>,
    );
  }
  Map<String, dynamic> toJson() => {
    'formatVersion': 1,
    'applicationVersion': applicationVersion,
    'savedAtUtc': savedAt.toIso8601String(),
    'mode': mode,
    'coordinateSystem': coordinateSystem.name,
    'speed': speed,
    'vehicle': vehicle,
    'points': points.map((p) => p.toJson()).toList(),
    'start': (start ?? (mode == 'navigation' ? points.first : null))?.toJson(),
    'end': (end ?? (mode == 'navigation' ? points.last : null))?.toJson(),
  };
  String serialize() =>
      'CTSROUTE/1\n${const JsonEncoder.withIndent('  ').convert(toJson())}';
  bool sameShape(RouteDocument other) {
    if (mode != other.mode || points.length != other.points.length) {
      return false;
    }
    for (var i = 0; i < points.length; i++) {
      if (Geo.distance(
            Geo.convert(points[i], coordinateSystem, CoordinateSystem.wgs84),
            Geo.convert(
              other.points[i],
              other.coordinateSystem,
              CoordinateSystem.wgs84,
            ),
          ) >
          .5) {
        return false;
      }
    }
    return true;
  }
}

class RoutePosition {
  const RoutePosition(this.point, this.direction, this.distance, this.finished);
  final GeoPoint point;
  final double direction, distance;
  final bool finished;
}

/// Fixed-speed route integration; a monotonic elapsed time is supplied by the host.
class RoutePlayback {
  RoutePlayback(this.route) {
    for (var i = 1; i < route.points.length; i++) {
      _lengths.add(Geo.distance(route.points[i - 1], route.points[i]));
    }
    totalDistance = _lengths.fold(0, (a, b) => a + b);
  }
  final RouteDocument route;
  final List<double> _lengths = [];
  late final double totalDistance;
  double _distance = 0;
  RoutePosition advance(Duration elapsed, {double? speedKph}) {
    final speed = speedKph ?? route.speed;
    if (!speed.isFinite || speed < 0 || elapsed.isNegative) {
      throw const FormatException('模拟速度或时间步无效');
    }
    _distance = math.min(
      totalDistance,
      _distance + speed / 3.6 * math.min(.5, elapsed.inMicroseconds / 1e6),
    );
    return at(_distance);
  }

  RoutePosition at(double distance) {
    if (!distance.isFinite) throw const FormatException('距离无效');
    distance = distance.clamp(0, totalDistance);
    var left = distance;
    for (var i = 0; i < _lengths.length; i++) {
      if (left <= _lengths[i] && _lengths[i] > 0) {
        final t = left / _lengths[i],
            a = route.points[i],
            b = route.points[i + 1];
        return RoutePosition(
          GeoPoint(a.lat + (b.lat - a.lat) * t, a.lng + (b.lng - a.lng) * t),
          Geo.bearing(a, b),
          distance,
          distance >= totalDistance,
        );
      }
      left -= _lengths[i];
    }
    return RoutePosition(
      route.points.last,
      _lengths.isEmpty
          ? 0
          : Geo.bearing(
              route.points[route.points.length - 2],
              route.points.last,
            ),
      distance,
      true,
    );
  }
}

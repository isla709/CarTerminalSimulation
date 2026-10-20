import 'dart:math' as math;

enum CoordinateSystem { wgs84, gcj02, bd09 }

class GeoPoint {
  GeoPoint(this.lat, this.lng, {this.name = ''}) {
    if (!lat.isFinite || !lng.isFinite || lat.abs() > 90 || lng.abs() > 180) {
      throw const FormatException('坐标超出范围');
    }
  }
  final double lat, lng;
  final String name;
  factory GeoPoint.fromJson(Map<String, dynamic> json) => GeoPoint(
    (json['lat'] as num).toDouble(),
    (json['lng'] as num).toDouble(),
    name: (json['name'] as String? ?? '').substring(
      0,
      math.min(200, (json['name'] as String? ?? '').length),
    ),
  );
  Map<String, dynamic> toJson() => {'lat': lat, 'lng': lng, 'name': name};
}

class Geo {
  static const _a = 6378245.0,
      _ee = 0.00669342162296594323,
      _xpi = math.pi * 3000 / 180;
  static GeoPoint convert(
    GeoPoint point,
    CoordinateSystem from,
    CoordinateSystem to,
  ) {
    if (from == to) return point;
    final wgs = switch (from) {
      CoordinateSystem.wgs84 => point,
      CoordinateSystem.gcj02 => _toWgs(point),
      CoordinateSystem.bd09 => _toWgs(_bdToGcj(point)),
    };
    return switch (to) {
      CoordinateSystem.wgs84 => wgs,
      CoordinateSystem.gcj02 => _toGcj(wgs),
      CoordinateSystem.bd09 => _gcjToBd(_toGcj(wgs)),
    };
  }

  static bool _outside(GeoPoint p) =>
      p.lng < 72.004 || p.lng > 137.8347 || p.lat < 0.8293 || p.lat > 55.8271;
  static GeoPoint _toGcj(GeoPoint p) {
    if (_outside(p)) return p;
    final x = p.lng - 105, y = p.lat - 35;
    var dlat =
        -100 +
        2 * x +
        3 * y +
        .2 * y * y +
        .1 * x * y +
        .2 * math.sqrt(x.abs());
    dlat +=
        (20 * math.sin(6 * x * math.pi) + 20 * math.sin(2 * x * math.pi)) *
        2 /
        3;
    dlat +=
        (20 * math.sin(y * math.pi) + 40 * math.sin(y / 3 * math.pi)) * 2 / 3;
    dlat +=
        (160 * math.sin(y / 12 * math.pi) + 320 * math.sin(y * math.pi / 30)) *
        2 /
        3;
    var dlng =
        300 + x + 2 * y + .1 * x * x + .1 * x * y + .1 * math.sqrt(x.abs());
    dlng +=
        (20 * math.sin(6 * x * math.pi) + 20 * math.sin(2 * x * math.pi)) *
        2 /
        3;
    dlng +=
        (20 * math.sin(x * math.pi) + 40 * math.sin(x / 3 * math.pi)) * 2 / 3;
    dlng +=
        (150 * math.sin(x / 12 * math.pi) + 300 * math.sin(x / 30 * math.pi)) *
        2 /
        3;
    final rad = p.lat / 180 * math.pi,
        magic = 1 - _ee * math.pow(math.sin(rad), 2),
        root = math.sqrt(magic);
    dlat = dlat * 180 / ((_a * (1 - _ee)) / (magic * root) * math.pi);
    dlng = dlng * 180 / (_a / root * math.cos(rad) * math.pi);
    return GeoPoint(p.lat + dlat, p.lng + dlng);
  }

  static GeoPoint _toWgs(GeoPoint p) {
    if (_outside(p)) return p;
    var estimate = p;
    for (var i = 0; i < 8; i++) {
      final converted = _toGcj(estimate),
          dlat = p.lat - converted.lat,
          dlng = p.lng - converted.lng;
      estimate = GeoPoint(estimate.lat + dlat, estimate.lng + dlng);
      if (dlat.abs() < 1e-10 && dlng.abs() < 1e-10) break;
    }
    return estimate;
  }

  static GeoPoint _gcjToBd(GeoPoint p) {
    final z =
            math.sqrt(p.lng * p.lng + p.lat * p.lat) +
            .00002 * math.sin(p.lat * _xpi),
        theta = math.atan2(p.lat, p.lng) + .000003 * math.cos(p.lng * _xpi);
    return GeoPoint(z * math.sin(theta) + .006, z * math.cos(theta) + .0065);
  }

  static GeoPoint _bdToGcj(GeoPoint p) {
    final x = p.lng - .0065,
        y = p.lat - .006,
        z = math.sqrt(x * x + y * y) - .00002 * math.sin(y * _xpi),
        theta = math.atan2(y, x) - .000003 * math.cos(x * _xpi);
    return GeoPoint(z * math.sin(theta), z * math.cos(theta));
  }

  static double distance(GeoPoint a, GeoPoint b) {
    final dlat = (b.lat - a.lat) * math.pi / 180,
        dlng = (b.lng - a.lng) * math.pi / 180;
    final h =
        math.pow(math.sin(dlat / 2), 2) +
        math.cos(a.lat * math.pi / 180) *
            math.cos(b.lat * math.pi / 180) *
            math.pow(math.sin(dlng / 2), 2);
    return 6371000 * 2 * math.asin(math.sqrt(h.clamp(0, 1)));
  }

  static double bearing(GeoPoint a, GeoPoint b) {
    final p1 = a.lat * math.pi / 180,
        p2 = b.lat * math.pi / 180,
        d = (b.lng - a.lng) * math.pi / 180;
    return (math.atan2(
                  math.sin(d) * math.cos(p2),
                  math.cos(p1) * math.sin(p2) -
                      math.sin(p1) * math.cos(p2) * math.cos(d),
                ) *
                180 /
                math.pi +
            360) %
        360;
  }
}

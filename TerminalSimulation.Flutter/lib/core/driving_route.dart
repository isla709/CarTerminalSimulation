import 'dart:math' as math;
import 'geo.dart';
import 'route.dart';
import 'vehicle_driver.dart';
import 'vehicle_events.dart';
import 'vehicle_options.dart';

class _Bend {
  const _Bend(this.at, this.radius, this.angle, this.exitDistance, this.lastAt);
  final double at, radius, angle, exitDistance, lastAt;
}

class RouteSpeedProfile {
  RouteSpeedProfile(List<GeoPoint> points)
    : points = List.unmodifiable(points) {
    if (points.length < 2 || points.length > 100000) {
      throw const FormatException('路径点数量必须为 2–100000');
    }
    offsets.add(0);
    for (var i = 1; i < points.length; i++) {
      offsets.add(offsets.last + distance(points[i - 1], points[i]));
    }
    for (var i = 1; i < points.length - 1; i++) {
      final before = offsets[i] - offsets[i - 1],
          after = offsets[i + 1] - offsets[i];
      if (before < 1 || after < 1) continue;
      final signed =
          (Geo.bearing(points[i], points[i + 1]) -
                  Geo.bearing(points[i - 1], points[i]) +
                  540) %
              360 -
          180;
      final delta = signed.abs();
      if (delta < 2) continue;
      final radius = math.max(
        5.0,
        math.min(35, (before + after) / 2) /
            (2 * math.sin(delta * math.pi / 360)),
      );
      final exit = (radius * .7).clamp(12.0, 40.0);
      if (_curves.isNotEmpty &&
          _curves.last.angle.sign == signed.sign &&
          (_curves.last.angle + signed).abs() < 175 &&
          delta < 45 &&
          offsets[i] - _curves.last.lastAt <= 35) {
        final previous = _curves.last, weight = previous.angle.abs() + delta;
        final apex =
            (previous.at * previous.angle.abs() + offsets[i] * delta) / weight;
        _curves[_curves.length - 1] = _Bend(
          apex,
          math.min(previous.radius, radius),
          previous.angle + signed,
          offsets[i] - apex + exit,
          offsets[i],
        );
      } else {
        _curves.add(_Bend(offsets[i], radius, signed, exit, offsets[i]));
      }
    }
    _curveOffsets.addAll(_curves.map((e) => e.at));
  }
  final List<GeoPoint> points;
  final List<double> offsets = [];
  final List<_Bend> _curves = [];
  final List<double> _curveOffsets = [];
  double get totalDistance => offsets.last;
  // Use the original CTS atan2 distance, preserving its numeric trajectory.
  static double distance(GeoPoint a, GeoPoint b) {
    final phi1 = a.lat * math.pi / 180, phi2 = b.lat * math.pi / 180;
    final dphi = (b.lat - a.lat) * math.pi / 180,
        dlng = (b.lng - a.lng) * math.pi / 180;
    final h =
        (math.sin(dphi / 2) * math.sin(dphi / 2) +
                math.cos(phi1) *
                    math.cos(phi2) *
                    math.sin(dlng / 2) *
                    math.sin(dlng / 2))
            .clamp(0.0, 1.0);
    return 6371000 * 2 * math.atan2(math.sqrt(h), math.sqrt(1 - h));
  }

  static int _search(List<double> values, double target) {
    var low = 0, high = values.length - 1;
    while (low <= high) {
      final mid = low + ((high - low) >> 1);
      if (values[mid] == target) return mid;
      if (values[mid] < target) {
        low = mid + 1;
      } else {
        high = mid - 1;
      }
    }
    return ~low;
  }

  ({double kph, bool isCurve, String constraint}) speedLimit(
    double traveled,
    VehicleOptions options,
  ) {
    final style = options.drivingStyle == 'calm'
        ? .8
        : options.drivingStyle == 'sport'
        ? 1.2
        : 1.0;
    final lateral =
        (options.powertrain == 'diesel' && options.maxAcceleration < 1
            ? 1.1
            : 2) *
        style;
    final brake = math.min(options.brakeDeceleration, 2.2 * style);
    var limit = options.maxSpeedKph / 3.6, constraint = 'cruise';
    var index = _search(_curveOffsets, traveled);
    if (index < 0) index = math.max(0, ~index - 1);
    for (; index < _curves.length; index++) {
      final curve = _curves[index];
      if (curve.at - traveled > 350) break;
      if (traveled > curve.at + curve.exitDistance) continue;
      var corner = math.max(2.0, math.sqrt(lateral * curve.radius));
      if (curve.angle.abs() >= 65) {
        corner = math.min(
          corner,
          (curve.angle > 0 ? 20 : 25) / 3.6 * math.sqrt(style),
        );
      }
      double candidate;
      String phase;
      if (traveled < curve.at) {
        final reaction = math.max(5.0, options.averageSpeedKph / 3.6 * 1.1);
        candidate = math.sqrt(
          corner * corner +
              2 * brake * math.max(0, curve.at - traveled - reaction),
        );
        phase = curve.angle > 0 ? 'turnRight' : 'turnLeft';
      } else {
        final acceleration = math.min(
          options.maxAcceleration * .65,
          1.5 * style,
        );
        candidate = math.sqrt(
          corner * corner +
              2 *
                  acceleration *
                  (traveled - curve.at + math.min(4, curve.radius * .15)),
        );
        phase = 'turnExit';
      }
      if (candidate < limit) {
        limit = candidate;
        constraint = phase;
      }
    }
    return (
      kph: limit * 3.6,
      isCurve: limit * 3.6 < options.maxSpeedKph - .1,
      constraint: constraint,
    );
  }

  DrivingContext drivingContext(double traveled) {
    var index = _search(_curveOffsets, traveled);
    if (index < 0) index = ~index;
    final inBend =
        index > 0 &&
        traveled <= _curves[index - 1].at + _curves[index - 1].exitDistance;
    for (
      var i = index;
      i < _curves.length && _curves[i].at - traveled <= 500;
      i++
    ) {
      if (_curves[i].angle.abs() >= 45) {
        return DrivingContext(
          traveled,
          turnDistanceMeters: _curves[i].at - traveled,
          turnAngle: _curves[i].angle,
          inBend: inBend || _curves[i].at - traveled < 15,
        );
      }
    }
    return DrivingContext(traveled, inBend: inBend);
  }

  ({GeoPoint point, int bearing}) sample(double distance) {
    var index = _search(offsets, distance.clamp(0, totalDistance));
    if (index < 0) index = ~index - 1;
    index = index.clamp(0, points.length - 2);
    while (index < points.length - 2 && offsets[index + 1] <= offsets[index]) {
      index++;
    }
    final length = offsets[index + 1] - offsets[index];
    final fraction = length > 0
        ? ((distance - offsets[index]) / length).clamp(0, 1)
        : 1.0;
    final from = points[index], to = points[index + 1];
    return (
      point: GeoPoint(
        from.lat + (to.lat - from.lat) * fraction,
        from.lng + (to.lng - from.lng) * fraction,
      ),
      bearing: _roundEven(Geo.bearing(from, to)) % 360,
    );
  }

  static int _roundEven(double value) {
    final floor = value.floor();
    return value - floor == .5
        ? (floor.isEven ? floor : floor + 1)
        : value.round();
  }
}

class DrivingPlayback {
  DrivingPlayback(
    RouteDocument route, {
    DrivingRandom? random,
    RouteSpeedProfile? geometry,
  }) : profile = geometry ?? RouteSpeedProfile(route.points) {
    final options = VehicleOptions.fromJson(route.vehicle ?? {});
    driver = options.enabled ? VehicleDriver(options, random: random) : null;
    telemetry =
        driver?.tick(.001, options.maxSpeedKph, profile.totalDistance) ??
        VehicleTelemetry.fixed(route.speed);
    position = profile.sample(0);
    if (profile.totalDistance == 0) _finish();
  }
  final RouteSpeedProfile profile;
  late final VehicleDriver? driver;
  late VehicleTelemetry telemetry;
  late ({GeoPoint point, int bearing}) position;
  double traveled = 0, _elapsed = 0;
  bool finished = false;
  void advance(Duration elapsed, {required double fixedSpeedKph}) {
    if (finished) return;
    if (elapsed.isNegative || !fixedSpeedKph.isFinite || fixedSpeedKph < 0) {
      throw const FormatException('模拟时间或速度无效');
    }
    final dt = (elapsed.inMicroseconds / 1e6).clamp(.001, 1.0);
    final steps = math.max(1, (dt / .05).ceil()), step = dt / steps;
    for (var i = 0; i < steps && traveled < profile.totalDistance; i++) {
      _elapsed += step;
      final before = driver == null
          ? math.max(0.0, fixedSpeedKph)
          : telemetry.speedKph;
      if (driver != null) {
        final limit = profile.speedLimit(traveled, driver!.options);
        telemetry = driver!.tick(
          step,
          limit.kph,
          profile.totalDistance - traveled,
          routeConstraint: limit.constraint,
          context: profile.drivingContext(traveled),
        );
      } else {
        telemetry = VehicleTelemetry.fixed(before);
      }
      final after = telemetry.speedKph;
      traveled = math.min(
        profile.totalDistance,
        traveled + (before + after) / 2 / 3.6 * step,
      );
      if (driver != null &&
          profile.totalDistance - traveled < .15 &&
          after < .5) {
        traveled = profile.totalDistance;
      }
    }
    position = profile.sample(traveled);
    telemetry = telemetry.withValues({
      'traveledMeters': traveled,
      'remainingMeters': math.max(0, profile.totalDistance - traveled),
      'actualAverageSpeedKph': _elapsed > 0 ? traveled / _elapsed * 3.6 : 0.0,
    });
    if (traveled >= profile.totalDistance) _finish();
  }

  void _finish() {
    finished = true;
    position = profile.sample(profile.totalDistance);
    telemetry = telemetry.withValues({
      'speedKph': 0.0,
      'acceleration': 0.0,
      'throttle': 0.0,
      'brake': 0.0,
      'phase': 'finished',
      'remainingMeters': 0.0,
    });
  }
}

import 'dart:io';
import 'package:flutter_test/flutter_test.dart';
import 'package:terminal_simulation/core/geo.dart';
import 'package:terminal_simulation/core/route.dart';
import 'package:terminal_simulation/services/storage.dart';

void main() {
  test('coordinate roundtrip and out-of-China identity', () {
    final p = GeoPoint(39.9, 116.4);
    for (final system in CoordinateSystem.values) {
      final roundtrip = Geo.convert(
        Geo.convert(p, CoordinateSystem.wgs84, system),
        system,
        CoordinateSystem.wgs84,
      );
      expect(Geo.distance(p, roundtrip), lessThan(.5));
    }
    final outside = GeoPoint(51.5, -.1);
    expect(
      Geo.convert(outside, CoordinateSystem.wgs84, CoordinateSystem.gcj02),
      same(outside),
    );
  });
  test('CTS route BOM, CRLF, metadata and vehicle preserved', () {
    final route = RouteDocument(
      points: [GeoPoint(39, 116), GeoPoint(39.001, 116.001)],
      coordinateSystem: CoordinateSystem.wgs84,
      vehicle: {
        'enabled': true,
        'trafficEvents': {'enabled': false},
      },
    );
    final restored = RouteDocument.parse(
      '\uFEFF${route.serialize().replaceAll('\n', '\r\n')}',
    );
    expect(restored.coordinateSystem, CoordinateSystem.wgs84);
    expect(restored.vehicle, route.vehicle);
    expect(restored.sameShape(route), isTrue);
    expect(
      () => RouteDocument(points: [GeoPoint(0, 0)]),
      throwsFormatException,
    );
    expect(() => RouteDocument.parse('garbage'), throwsFormatException);
    final reverse = RouteDocument(
      points: route.points.reversed.toList(),
      coordinateSystem: route.coordinateSystem,
    );
    expect(route.sameShape(reverse), isFalse);
  });
  test(
    'map history serializes writers, persists, deduplicates and retains rename',
    () async {
      final dir = await Directory.systemTemp.createTemp('cts-test-');
      try {
        final storage = AppStorage(dir),
            a = MapHistory(storage),
            b = MapHistory(storage);
        final route = RouteDocument(
          points: [GeoPoint(39, 116), GeoPoint(39.001, 116.001)],
        );
        await Future.wait([
          a.addSearch('北京'),
          a.addRoute('route', route),
          a.addSearch('上海'),
        ]);
        final data = await a.read();
        expect((data['searches'] as List).length, 2);
        expect((await b.read())['routes'], hasLength(1));
        final id = data['routes'][0]['id'] as String;
        await a.rename(id, 'renamed');
        await a.addRoute('automatic', route, automatic: true);
        expect((await a.read())['routes'].single['name'], 'renamed');
      } finally {
        await dir.delete(recursive: true);
      }
    },
  );
  test('fixed-speed route distance and large pause clamp', () {
    final route = RouteDocument(
          points: [GeoPoint(0, 0), GeoPoint(0, .01)],
          speed: 36,
        ),
        playback = RoutePlayback(route);
    final first = playback.advance(const Duration(milliseconds: 100));
    expect(first.distance, closeTo(1, 1e-6));
    final next = playback.advance(const Duration(seconds: 30));
    expect(next.distance, closeTo(6, 1e-6));
    expect(next.direction, closeTo(90, .01));
  });
}

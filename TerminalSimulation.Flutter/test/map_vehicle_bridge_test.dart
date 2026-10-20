import 'dart:convert';
import 'dart:io';
import 'package:flutter_test/flutter_test.dart';
import 'package:terminal_simulation/core/geo.dart';
import 'package:terminal_simulation/core/route.dart';
import 'package:terminal_simulation/core/vehicle_options.dart';
import 'package:terminal_simulation/services/app_model.dart';
import 'package:terminal_simulation/services/map_session.dart';
import 'package:terminal_simulation/services/storage.dart';

void main() {
  test(
    'map vehicle settings persist across documents and restore traffic catalog',
    () async {
      final directory = await Directory.systemTemp.createTemp(
        'cts-map-driving-',
      );
      final model = AppModel(storage: AppStorage(directory));
      final scripts = <String>[];
      MapSession session() => MapSession(
        model: model,
        token: 'local-test',
        execute: (script) async {
          scripts.add(script);
        },
        openRoute: () async => null,
        saveRoute: (_) async => false,
      );
      final first = session(), fresh = session();
      Future<void> send(MapSession s, Map<String, dynamic> data) => s.receive(
        jsonEncode({'token': 'local-test', 'message': jsonEncode(data)}),
      );
      Map<String, dynamic> last(String callback) {
        final script = scripts.lastWhere(
          (v) => v.startsWith('window.ctsMap.$callback?.('),
        );
        return jsonDecode(
              script.substring(script.indexOf('?.(') + 3, script.length - 2),
            )
            as Map<String, dynamic>;
      }

      try {
        await send(first, {
          'type': 'vehicle_settings',
          'action': 'save',
          'options': {
            ...VehicleOptions.presets['manual-truck']!.toJson(),
            'enabled': true,
            'randomEventsEnabled': true,
          },
        });
        expect(last('receiveVehicleSettings')['succeeded'], isTrue);
        await send(fresh, {'type': 'vehicle_settings', 'action': 'get'});
        expect(last('receiveVehicleSettings')['options']['enabled'], isTrue);
        expect(
          last('receiveVehicleSettings')['options']['preset'],
          'manual-truck',
        );
        expect(
          last('receiveVehicleSettings')['trafficEventCatalog'],
          hasLength(14),
        );
        await send(first, {'type': 'vehicle_settings', 'action': 'get'});
        expect(
          last('receiveVehicleSettings')['options']['preset'],
          'manual-truck',
        );
        expect(last('receiveVehicleSettings')['options']['enabled'], isTrue);
        await send(first, {
          'type': 'vehicle_settings',
          'action': 'save',
          'options': {'averageSpeedKph': 1000},
        });
        expect(last('receiveVehicleSettings')['succeeded'], isFalse);
        await send(first, {'type': 'vehicle_settings', 'action': 'get'});
        expect(
          last('receiveVehicleSettings')['options']['preset'],
          'manual-truck',
        );
      } finally {
        first.close();
        fresh.close();
        model.dispose();
        await directory.delete(recursive: true);
      }
    },
  );
  test(
    'map starts real driver, publishes stationary telemetry and accepts manual controls',
    () async {
      final model = AppModel();
      final scripts = <String>[];
      final session = MapSession(
        model: model,
        token: 'run-test',
        execute: (script) async {
          scripts.add(script);
        },
        openRoute: () async => null,
        saveRoute: (_) async => false,
      );
      session.ready = true;
      Future<void> send(Map<String, dynamic> data) => session.receive(
        jsonEncode({'token': 'run-test', 'message': jsonEncode(data)}),
      );
      try {
        final options = {
          ...VehicleOptions.presets['small-manual']!.toJson(),
          'enabled': true,
        };
        await send({
          'type': 'simulate_path',
          'coordinateSystem': 'gcj02',
          'speed': 60,
          'path': [GeoPoint(30, 120).toJson(), GeoPoint(31, 120).toJson()],
          'vehicle': options,
        });
        expect(model.simulating, isTrue);
        expect(model.vehicleTelemetry!.values['transmission'], 'manual');
        await send({
          'type': 'vehicle_control',
          'controls': {
            'automatic': false,
            'gear': 0,
            'throttle': 1,
            'brake': 0,
          },
        });
        await Future<void>.delayed(const Duration(milliseconds: 240));
        expect(model.vehicleTelemetry!.phase, anyOf('shifting', 'neutral'));
        expect(model.speed, 0);
        expect(
          model.locationSnapshot().speed,
          model.vehicleTelemetry!.speedKph,
        );
        expect(
          scripts.any((v) => v.startsWith('window.ctsVehicleUi.render?.(')),
          isTrue,
        );
        await send({'type': 'stop_simulation'});
        expect(model.simulating, isFalse);
        final lastSpeed = model.speed;
        await Future<void>.delayed(const Duration(milliseconds: 150));
        expect(model.speed, lastSpeed);
      } finally {
        session.close();
        model.dispose();
      }
    },
  );
  test('file route can run enabled vehicle options through AppModel', () async {
    final model = AppModel();
    try {
      model.applyRoute(
        RouteDocument(
          points: [GeoPoint(30, 120), GeoPoint(30.001, 120)],
          vehicle: {
            ...VehicleOptions.presets['electric']!.toJson(),
            'enabled': true,
          },
        ),
      );
      expect(await model.startSimulation(), isTrue);
      await Future<void>.delayed(const Duration(milliseconds: 220));
      expect(model.speed, greaterThan(0));
      expect(model.vehicleTelemetry!.values['transmission'], 'singleSpeed');
      model.stopSimulation();
      expect(model.simulating, isFalse);
    } finally {
      model.dispose();
    }
  });
}

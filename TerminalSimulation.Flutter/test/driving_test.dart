import 'dart:convert';
import 'dart:io';
import 'package:flutter_test/flutter_test.dart';
import 'package:terminal_simulation/core/driving_route.dart';
import 'package:terminal_simulation/core/geo.dart';
import 'package:terminal_simulation/core/route.dart';
import 'package:terminal_simulation/core/vehicle_driver.dart';
import 'package:terminal_simulation/core/vehicle_events.dart';
import 'package:terminal_simulation/core/vehicle_options.dart';
import 'package:terminal_simulation/services/app_model.dart';

class FixedRandom implements DrivingRandom {
  FixedRandom([this.value = .01]);
  final double value;
  @override
  double nextDouble() => value;
  @override
  int nextBetween(int min, int exclusiveMax) => min;
}

void equivalent(dynamic actual, dynamic expected, String path) {
  if (expected is num) {
    expect(actual, isA<num>(), reason: path);
    expect(
      (actual as num).toDouble(),
      closeTo(expected.toDouble(), 1e-7),
      reason: path,
    );
  } else if (expected is Map) {
    expect(actual, isA<Map>(), reason: path);
    for (final key in expected.keys) {
      equivalent(actual[key], expected[key], '$path.$key');
    }
  } else if (expected is List) {
    expect(actual, isA<List>(), reason: path);
    expect((actual as List).length, expected.length, reason: path);
    for (var i = 0; i < expected.length; i++) {
      equivalent(actual[i], expected[i], '$path[$i]');
    }
  } else {
    expect(actual, expected, reason: path);
  }
}

void main() {
  final fixture =
      jsonDecode(File('test/fixtures/driving-csharp.json').readAsStringSync())
          as Map<String, dynamic>;
  test('vehicle presets and all 14 traffic defaults match original C#', () {
    equivalent(
      VehicleOptions.presets.map((k, v) => MapEntry(k, v.toJson())),
      fixture['presets'],
      'presets',
    );
    equivalent(
      trafficCatalog.map((e) => e.toJson()).toList(),
      fixture['catalog'],
      'catalog',
    );
  });
  for (final entry in fixture['drivers'] as List) {
    test(
      'C# driver trace ${entry['preset']} including shifts, manual and endpoint braking',
      () {
        final driver = VehicleDriver(
          VehicleOptions.fromJson(checkedObject(entry['options'])),
          random: FixedRandom(),
        );
        final expected = {
          for (final item in entry['samples']) item['tick'] as int: item,
        };
        for (var tick = 0; tick < 2600; tick++) {
          if (entry['preset'] == 'small-manual') {
            switch (tick) {
              case 1400:
                driver.setControls(
                  const VehicleControls(automatic: false, gear: 1, throttle: 1),
                );
              case 1500:
                driver.setControls(
                  const VehicleControls(automatic: false, gear: 0, throttle: 1),
                );
              case 1600:
                driver.setControls(
                  const VehicleControls(
                    automatic: false,
                    gear: 1,
                    throttle: .8,
                  ),
                );
              case 1750:
                driver.setControls(
                  const VehicleControls(automatic: false, gear: 1, brake: 1),
                );
              case 1850:
                driver.setControls(const VehicleControls());
            }
          }
          final state = driver.tick(
            .05,
            tick >= 800 && tick < 1100 ? 18 : 300,
            tick >= 2300 ? .1 : 10000,
          );
          if (expected.containsKey(tick)) {
            equivalent(
              state.toJson(),
              expected[tick]['state'],
              '${entry['preset']} tick $tick',
            );
          }
        }
      },
    );
  }
  for (final entry in fixture['events'] as List) {
    test('C# traffic trace ${entry['id']} completes all phases and cycles', () {
      final options = VehicleOptions.fromJson(checkedObject(entry['options']));
      final driver = VehicleDriver(options, random: FixedRandom());
      var traveled = 0.0, previous = 0.0;
      for (var tick = 0; tick < 1000; tick++) {
        final state = driver.tick(
          .05,
          75,
          10000 - traveled,
          context: DrivingContext(traveled),
        );
        traveled += (previous + state.speedKph) / 2 / 3.6 * .05;
        previous = state.speedKph;
      }
      driver.events.start(
        entry['id'],
        TrafficRule.fromJson(checkedObject(entry['rule'])),
        traveled + 120,
      );
      final expected = {
        for (final item in entry['samples']) item['tick'] as int: item,
      };
      final stages = <String>[];
      String? lastStage;
      var finalTick = -1;
      for (var tick = 0; tick < 16000; tick++) {
        final state = driver.tick(
          .05,
          75,
          10000 - traveled,
          context: DrivingContext(traveled),
        );
        traveled += (previous + state.speedKph) / 2 / 3.6 * .05;
        previous = state.speedKph;
        final values = state.toJson(),
            stage =
                '${state.values['eventStage']}:${state.values['eventCycle']}';
        if (stage != lastStage) stages.add(stage);
        lastStage = stage;
        if (expected.containsKey(tick)) {
          equivalent(
            values,
            expected[tick]['state'],
            '${entry['id']} tick $tick',
          );
          expect(traveled, closeTo(expected[tick]['traveled'], 1e-7));
        }
        expect(state.speedKph, inInclusiveRange(0, 75));
        if (state.drivingEvent.isEmpty) {
          finalTick = tick;
          break;
        }
      }
      expect(finalTick, entry['finalTick']);
      expect(stages, entry['stages']);
    });
  }
  for (final entry in fixture['profiles'] as List) {
    test(
      'C# geometry profile ${entry['name']} sampling, bends and constraints',
      () {
        final profile = RouteSpeedProfile(
          (entry['path'] as List)
              .map((v) => GeoPoint.fromJson(checkedObject(v)))
              .toList(),
        );
        expect(profile.totalDistance, closeTo(entry['totalDistance'], 1e-7));
        for (final expected in entry['samples']) {
          final at = (expected['at'] as num).toDouble();
          final limit = profile.speedLimit(
            at,
            VehicleOptions.presets['electric']!,
          );
          final context = profile.drivingContext(at),
              position = profile.sample(at);
          equivalent(
            {
              'at': at,
              'kph': limit.kph,
              'isCurve': limit.isCurve,
              'constraint': limit.constraint,
              'turnDistanceMeters': context.turnDistanceMeters.isFinite
                  ? context.turnDistanceMeters
                  : null,
              'turnAngle': context.turnAngle,
              'inBend': context.inBend,
              'point': {'lat': position.point.lat, 'lng': position.point.lng},
              'bearing': position.bearing,
            },
            expected,
            '${entry['name']} at $at',
          );
        }
      },
    );
  }
  test(
    'options reject invalid data and keep immutable snapshots independent',
    () {
      for (final bad in [
        {'averageSpeedKph': 101},
        {'maxAcceleration': double.nan},
        {
          'gearRatios': [1, 2],
        },
        {'upshiftRpm': 1000},
        {'powertrain': 'unknown'},
        {
          'trafficEvents': {
            'events': {'unknown': {}},
          },
        },
        {
          'trafficEvents': {
            'events': {
              'stopGo': {'minCycles': 4, 'maxCycles': 2},
            },
          },
        },
      ]) {
        expect(() => VehicleOptions.fromJson(bad), throwsFormatException);
      }
      final first = VehicleOptions.fromJson({
        'enabled': true,
        'randomEventsEnabled': true,
      });
      final second = VehicleOptions.fromJson(first.toJson());
      expect(second.trafficEvents.events.length, 14);
      expect(identical(first.gearRatios, second.gearRatios), isFalse);
      expect(() => second.gearRatios[0] = 5, throwsUnsupportedError);
      expect(second.trafficEvents.events['railCrossing']!.enabled, isFalse);
    },
  );
  test('scheduler honors all 14 eligible types, probability and placement', () {
    for (final item in trafficCatalog) {
      VehicleOptions options({double probability = 100}) =>
          VehicleOptions.fromJson({
            ...VehicleOptions.presets['electric']!.toJson(),
            'enabled': true,
            'randomEventsEnabled': true,
            'trafficEvents': {
              'minimumGapSeconds': 5,
              'events': {
                for (final other in trafficCatalog)
                  other.id: {
                    ...other.defaults.toJson(),
                    'enabled': other.id == item.id,
                    'probabilityPercent': probability,
                    'intervalSeconds': 5,
                  },
              },
            },
          });
      final controller = VehicleEventController(FixedRandom());
      final context = item.id == 'leftTurnYield'
          ? const DrivingContext(100, turnDistanceMeters: 240, turnAngle: -90)
          : const DrivingContext(100);
      EventInfluence state = EventInfluence.none;
      for (var i = 0; i < 600 && state.kind.isEmpty; i++) {
        state = controller.update(.05, context, 40, 5000, options(), true);
      }
      expect(state.kind, item.id);
      final disabled = VehicleEventController(FixedRandom());
      for (var i = 0; i < 600; i++) {
        expect(
          disabled
              .update(.05, context, 40, 5000, options(probability: 0), true)
              .kind,
          isEmpty,
        );
      }
    }
  });
  test(
    'route integration uses measured velocity, exact endpoint and suspension clamp',
    () {
      final route = RouteDocument(
        points: [GeoPoint(30, 120), GeoPoint(30.00002, 120)],
        vehicle: {
          ...VehicleOptions.presets['electric']!.toJson(),
          'enabled': true,
        },
      );
      final playback = DrivingPlayback(route, random: FixedRandom());
      var previousSpeed = playback.telemetry.speedKph, integrated = 0.0;
      for (var i = 0; i < 1000 && !playback.finished; i++) {
        playback.advance(const Duration(milliseconds: 50), fixedSpeedKph: 60);
        integrated +=
            (previousSpeed + playback.telemetry.speedKph) / 2 / 3.6 * .05;
        previousSpeed = playback.telemetry.speedKph;
        if (!playback.finished) {
          expect(playback.traveled, closeTo(integrated, 1e-7));
        }
      }
      expect(playback.finished, isTrue);
      expect(playback.position.point.lat, route.points.last.lat);
      expect(playback.telemetry.phase, 'finished');
      expect(playback.telemetry.speedKph, 0);
      final fixed = DrivingPlayback(
        RouteDocument(points: [GeoPoint(30, 120), GeoPoint(31, 120)]),
      );
      fixed.advance(const Duration(hours: 1), fixedSpeedKph: 36);
      expect(fixed.traveled, closeTo(10, 1e-7));
    },
  );
  test(
    'async profile preparation can be cancelled without starting a stale timer',
    () async {
      final model = AppModel();
      model.applyRoute(
        RouteDocument(points: [GeoPoint(30, 120), GeoPoint(31, 120)]),
      );
      final task = model.startSimulation();
      model.stopSimulation();
      expect(await task, isFalse);
      expect(model.simulating, isFalse);
      expect(model.simulationPreparing, isFalse);
      model.dispose();
    },
  );
  test('driven turns brake before apex and accelerate out on both sides', () {
    for (final right in [true, false]) {
      final points = [
        GeoPoint(30, 120),
        GeoPoint(30.005, 120),
        GeoPoint(30.005, right ? 120.005 : 119.995),
      ];
      final route = RouteDocument(
        points: points,
        vehicle: {
          ...VehicleOptions.presets['electric']!.toJson(),
          'enabled': true,
          'averageSpeedKph': 50,
          'maxSpeedKph': 75,
        },
      );
      final playback = DrivingPlayback(route, random: FixedRandom());
      final apex = RouteSpeedProfile.distance(points[0], points[1]);
      double? entry, exit, recovered, acceleration;
      for (var i = 0; i < 4000 && playback.traveled < apex + 45; i++) {
        playback.advance(const Duration(milliseconds: 50), fixedSpeedKph: 60);
        if (playback.traveled > apex - 5) entry ??= playback.telemetry.speedKph;
        if (playback.traveled > apex + 8 && exit == null) {
          exit = playback.telemetry.speedKph;
          acceleration = (playback.telemetry.values['acceleration'] as num)
              .toDouble();
        }
        if (playback.traveled > apex + 35) {
          recovered ??= playback.telemetry.speedKph;
        }
      }
      expect(entry, inInclusiveRange(10, right ? 24 : 29));
      expect(acceleration, greaterThan(0));
      expect(recovered, greaterThan(entry! + 5));
    }
  });
  test(
    'manual takeover cancels traffic wait and invalid controls clamp safely',
    () {
      final options = VehicleOptions.fromJson({
        ...VehicleOptions.presets['small-manual']!.toJson(),
        'enabled': true,
        'randomEventsEnabled': true,
      });
      final driver = VehicleDriver(options, random: FixedRandom());
      final rule = TrafficRule.fromJson({
        ...trafficCatalog
            .firstWhere((e) => e.id == 'signalQueue')
            .defaults
            .toJson(),
        'minDurationSeconds': 1,
        'maxDurationSeconds': 1,
        'minCycles': 2,
        'maxCycles': 2,
      });
      driver.events.start('signalQueue', rule, 0);
      expect(
        driver.tick(.05, 75, 1000, context: const DrivingContext(0)).phase,
        'waiting',
      );
      driver.setControls(const VehicleControls(automatic: false, throttle: 1));
      final manual = driver.tick(
        .05,
        75,
        1000,
        context: const DrivingContext(0),
      );
      expect(manual.drivingEvent, isEmpty);
      expect(manual.values['controlMode'], 'manual');
      driver.setControls(const VehicleControls());
      expect(driver.tick(.05, 75, 1000).drivingEvent, isEmpty);
      driver.setControls(
        const VehicleControls(
          automatic: false,
          gear: 999,
          throttle: double.infinity,
          brake: double.nan,
        ),
      );
      final safe = driver.tick(.05, 75, 1000);
      expect(safe.gear, options.gearRatios.length);
      expect(safe.values['throttle'], 0);
      expect(safe.values['brake'], 1);
    },
  );
}

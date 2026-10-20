import 'dart:collection';
import 'dart:math' as math;
import 'vehicle_options.dart';

class DrivingContext {
  const DrivingContext(
    this.traveledMeters, {
    this.turnDistanceMeters = double.infinity,
    this.turnAngle = 0,
    this.inBend = false,
  });
  final double traveledMeters, turnDistanceMeters, turnAngle;
  final bool inBend;
}

class EventInfluence {
  const EventInfluence(
    this.kind,
    this.stage,
    this.remainingSeconds,
    this.cruiseMultiplier,
    this.speedLimitKph,
    this.hold, {
    this.cycle = 0,
    this.totalCycles = 0,
  });
  static const none = EventInfluence('', '', 0, 1, double.infinity, false);
  final String kind, stage;
  final double remainingSeconds, cruiseMultiplier, speedLimitKph;
  final bool hold;
  final int cycle, totalCycles;
}

abstract interface class DrivingRandom {
  double nextDouble();
  int nextBetween(int min, int exclusiveMax);
}

class DefaultDrivingRandom implements DrivingRandom {
  DefaultDrivingRandom([int? seed]) : _random = math.Random(seed);
  final math.Random _random;
  @override
  double nextDouble() => _random.nextDouble();
  @override
  int nextBetween(int min, int exclusiveMax) =>
      min + _random.nextInt(exclusiveMax - min);
}

class _Phase {
  const _Phase(
    this.stage,
    this.seconds,
    this.limitKph, {
    this.stop = false,
    this.travelMeters = 0,
    this.cycle = 1,
  });
  final String stage;
  final double seconds, limitKph, travelMeters;
  final bool stop;
  final int cycle;
}

/// Synthetic scenarios influence longitudinal speed, never the saved route.
class VehicleEventController {
  VehicleEventController(this.random);
  final DrivingRandom random;
  final Map<String, double> _clocks = {};
  final Queue<_Phase> _phases = Queue();
  _Phase? _phase;
  TrafficRule? _rule;
  String _kind = '';
  double _gap = 0, _remaining = 0, _targetAt = 0, _elapsed = 0;
  double _lastSignalAt = double.negativeInfinity;
  bool _initialized = false, _stopped = false;
  int _cycles = 0;
  double _between(double min, double max) =>
      min + random.nextDouble() * (max - min);
  double _nextOpportunity(double mean) =>
      -math.log(math.max(1e-9, 1 - random.nextDouble().clamp(1e-6, 1 - 1e-9))) *
      mean;

  void start(String kind, TrafficRule rule, double stopAt, {int? cycles}) {
    final definition = trafficCatalog.where((e) => e.id == kind).firstOrNull;
    if (definition == null) throw const FormatException('未知交通事件');
    rule.validate();
    _kind = kind;
    _rule = rule;
    _phase = null;
    _phases.clear();
    _elapsed = 0;
    _cycles = definition.hasCycles
        ? cycles ?? random.nextBetween(rule.minCycles, rule.maxCycles + 1)
        : 1;
    double duration() =>
        _between(rule.minDurationSeconds, rule.maxDurationSeconds);
    void stop(String stage, [int cycle = 1]) =>
        _phases.add(_Phase(stage, duration(), 0, stop: true, cycle: cycle));
    void timed(String stage, double seconds, double speed, [int cycle = 1]) =>
        _phases.add(_Phase(stage, seconds, speed, cycle: cycle));
    void move(String stage, double meters, double speed, [int cycle = 1]) =>
        _phases.add(
          _Phase(stage, 0, speed, travelMeters: meters, cycle: cycle),
        );
    void reaction([int cycle = 1]) =>
        timed('startDelay', _between(.8, 2.2), 0, cycle);
    switch (kind) {
      case 'signalSingle':
        stop('red');
        reaction();
        timed('releasing', 4, 22);
      case 'signalQueue':
        for (var cycle = 1; cycle <= _cycles; cycle++) {
          stop('redQueue', cycle);
          reaction(cycle);
          if (cycle < _cycles) move('queueMove', 16, rule.slowSpeedKph, cycle);
        }
        timed('releasing', 5, 22, _cycles);
      case 'stopGo':
      case 'mergeAlternating':
        for (var cycle = 1; cycle <= _cycles; cycle++) {
          stop(kind == 'stopGo' ? 'queueStop' : 'mergeWait', cycle);
          reaction(cycle);
          if (cycle < _cycles) {
            move('queueMove', _between(12, 25), rule.slowSpeedKph, cycle);
          }
        }
        timed('releasing', 6, rule.slowSpeedKph + 8, _cycles);
      case 'phantomJam':
        for (var cycle = 1; cycle <= _cycles; cycle++) {
          final floor = random.nextDouble() < .3
              ? 0.0
              : rule.slowSpeedKph * _between(.55, 1);
          timed('compression', duration(), floor, cycle);
          timed('waveRelease', _between(5, 10), rule.slowSpeedKph + 16, cycle);
        }
      case 'slowLeader':
        timed('following', duration(), rule.slowSpeedKph);
      case 'cutIn':
        timed('gapOpening', duration(), rule.slowSpeedKph);
        timed('recovering', 5, rule.slowSpeedKph + 12);
      case 'speedBump':
        _phases.add(
          _Phase('bumpApproach', 0, rule.slowSpeedKph, travelMeters: 1),
        );
        timed('bumpPass', duration(), rule.slowSpeedKph);
      default:
        stop(switch (kind) {
          'leftTurnYield' => 'oncomingWait',
          'crosswalkYield' => 'pedestrianWait',
          'busObstruction' => 'obstructionWait',
          'roadworks' => 'workzoneWait',
          'emergencyYield' => 'emergencyWait',
          'railCrossing' => 'barrierWait',
          _ => throw const FormatException('未知交通事件'),
        });
        reaction();
        timed(
          kind == 'roadworks' ? 'narrowPass' : 'cautiousPass',
          kind == 'roadworks' ? duration() : 5,
          rule.slowSpeedKph,
        );
    }
    _targetAt = stopAt;
  }

  EventInfluence update(
    double seconds,
    DrivingContext context,
    double speedKph,
    double routeRemaining,
    VehicleOptions options,
    bool automatic,
  ) {
    final settings = options.trafficEvents;
    if (!_initialized) {
      _initialized = true;
      _gap = settings.minimumGapSeconds;
      settings.events.forEach((id, rule) {
        _clocks[id] = _nextOpportunity(rule.intervalSeconds);
      });
    }
    if (!options.enabled || !options.randomEventsEnabled || !automatic) {
      if (_kind.isNotEmpty) _finish(settings);
      _gap = settings.minimumGapSeconds;
      return EventInfluence.none;
    }
    if (_kind.isEmpty) {
      if (speedKph < 8 || routeRemaining < 100) return EventInfluence.none;
      if (_gap > 0) {
        _gap -= seconds;
        return EventInfluence.none;
      }
      _schedule(seconds, context, speedKph, routeRemaining, options);
      if (_kind.isEmpty) return EventInfluence.none;
    }
    final currentRule = settings.events[_kind];
    if (currentRule == null ||
        !currentRule.enabled ||
        currentRule.probabilityPercent == 0) {
      _finish(settings);
      return EventInfluence.none;
    }
    if (_phase == null) _enterNext(context.traveledMeters, first: true);
    final phase = _phase!;
    _elapsed += seconds;
    final distance = math.max(0.0, _targetAt - context.traveledMeters);
    if (phase.stop) {
      if (distance <= .35 || (distance < 1.5 && speedKph < .5)) _stopped = true;
      if (_stopped) {
        if (speedKph < .2) _remaining = math.max(0, _remaining - seconds);
        if (_remaining <= 0) return _advance(context, speedKph, settings);
        return _influence(speedKph < .2 ? phase.stage : 'braking', 0, true);
      }
      final brake = math.min(
        options.brakeDeceleration,
        options.drivingStyle == 'calm' ? 1.5 : 1.9,
      );
      final cap =
          math.sqrt(2 * brake * math.max(0, distance - speedKph / 3.6 * 1.1)) *
          3.6;
      return _influence('approaching', cap, false);
    }
    if (phase.travelMeters > 0) {
      if (distance <= .35) return _advance(context, speedKph, settings);
      final cap = phase.stage == 'bumpApproach'
          ? math.sqrt(
                  math.pow(_rule!.slowSpeedKph / 3.6, 2) +
                      2 *
                          math.min(options.brakeDeceleration, 1.8) *
                          math.max(0, distance - 9),
                ) *
                3.6
          : math.min(
              phase.limitKph,
              math.sqrt(
                    2 *
                        math.min(options.brakeDeceleration, 1.5) *
                        math.max(0, distance + 6 - speedKph / 3.6 * 1.1),
                  ) *
                  3.6,
            );
      return _influence(phase.stage, cap, false);
    }
    if (phase.limitKph > 0 || speedKph < .2) {
      _remaining = math.max(0, _remaining - seconds);
    }
    if (_remaining <= 0) return _advance(context, speedKph, settings);
    final limit = phase.stage == 'following'
        ? phase.limitKph *
              (1 +
                  .09 * math.sin(_elapsed / 2.8) +
                  .04 * math.sin(_elapsed / 6.1))
        : phase.limitKph;
    return _influence(phase.stage, limit, phase.limitKph == 0);
  }

  EventInfluence _influence(String stage, double limit, bool hold) =>
      EventInfluence(
        _kind,
        stage,
        _phase!.travelMeters > 0 ? 0 : _remaining,
        1,
        limit,
        hold,
        cycle: _phase!.cycle,
        totalCycles: _cycles,
      );
  EventInfluence _advance(
    DrivingContext context,
    double speedKph,
    TrafficOptions settings,
  ) {
    if (_phases.isEmpty) {
      _finish(settings);
      return EventInfluence.none;
    }
    _enterNext(context.traveledMeters, first: false);
    return _influence(
      _phase!.stop ? 'approaching' : _phase!.stage,
      _phase!.stop ? math.min(speedKph, _rule!.slowSpeedKph) : _phase!.limitKph,
      _phase!.limitKph == 0 && !_phase!.stop,
    );
  }

  void _enterNext(double traveled, {required bool first}) {
    _phase = _phases.removeFirst();
    _remaining = _phase!.seconds;
    _elapsed = 0;
    _stopped = false;
    if (_phase!.stop && !first) {
      _targetAt = traveled + 6;
    } else if (_phase!.travelMeters > 0) {
      _targetAt = _phase!.stage == 'bumpApproach'
          ? _targetAt + 9
          : traveled + _phase!.travelMeters;
    }
  }

  void _schedule(
    double seconds,
    DrivingContext context,
    double speedKph,
    double remaining,
    VehicleOptions options,
  ) {
    final settings = options.trafficEvents;
    final brake = math.min(
      options.brakeDeceleration,
      options.drivingStyle == 'calm' ? 1.5 : 1.9,
    );
    final brakingDistance =
        math.pow(speedKph / 3.6, 2) / (2 * brake) + speedKph / 3.6 * 2.5 + 15;
    if (context.inBend || remaining < brakingDistance + 70) return;
    String? winner;
    var earliest = double.infinity;
    for (final definition in trafficCatalog) {
      final rule = settings.events[definition.id];
      if (rule == null || !rule.enabled || rule.probabilityPercent <= 0) {
        continue;
      }
      final signal = ['signalSingle', 'signalQueue'].contains(definition.id);
      final turnJunction =
          context.turnAngle.abs() >= 40 &&
          context.turnDistanceMeters > brakingDistance + 35 &&
          context.turnDistanceMeters < brakingDistance + 260;
      final virtualJunction =
          settings.allowVirtualSignalsOnStraight &&
          context.turnDistanceMeters > brakingDistance + 280 &&
          context.traveledMeters + brakingDistance + 35 - _lastSignalAt >=
              settings.virtualSignalSpacingMeters;
      if (signal && !(turnJunction || virtualJunction)) continue;
      if (signal &&
          context.traveledMeters + context.turnDistanceMeters - _lastSignalAt <
              80) {
        continue;
      }
      if (definition.id == 'leftTurnYield' &&
          !(turnJunction && context.turnAngle < -40)) {
        continue;
      }
      if (!signal &&
          definition.id != 'leftTurnYield' &&
          context.turnDistanceMeters < brakingDistance + 45) {
        continue;
      }
      _clocks.putIfAbsent(
        definition.id,
        () => _nextOpportunity(rule.intervalSeconds),
      );
      _clocks[definition.id] = _clocks[definition.id]! - seconds;
      if (_clocks[definition.id]! > 0) continue;
      final overdue = _clocks[definition.id]!;
      _clocks[definition.id] = _nextOpportunity(rule.intervalSeconds);
      if (random.nextDouble() * 100 >= rule.probabilityPercent) continue;
      if (overdue < earliest) {
        earliest = overdue;
        winner = definition.id;
      }
    }
    if (winner == null) return;
    final selected = settings.events[winner]!;
    var cycles = random.nextBetween(selected.minCycles, selected.maxCycles + 1);
    final isSignal = ['signalSingle', 'signalQueue'].contains(winner);
    final useTurn =
        (isSignal || winner == 'leftTurnYield') &&
        context.turnAngle.abs() >= 40 &&
        context.turnDistanceMeters < brakingDistance + 260;
    var finalStop =
        context.traveledMeters +
        (useTurn
            ? context.turnDistanceMeters - 8
            : brakingDistance + _between(25, 55));
    if (winner == 'signalQueue') {
      if (!useTurn) finalStop += (cycles - 1) * 22;
      final fit =
          1 +
          ((finalStop - context.traveledMeters - brakingDistance) / 22).floor();
      cycles = math.min(cycles, fit);
      if (cycles < selected.minCycles) return;
    }
    final firstStop =
        finalStop - (winner == 'signalQueue' ? (cycles - 1) * 22 : 0);
    final reserve = ['stopGo', 'mergeAlternating'].contains(winner)
        ? cycles * 35 + 35
        : 35;
    if (finalStop + reserve >= context.traveledMeters + remaining) return;
    if (isSignal) _lastSignalAt = finalStop;
    start(winner, selected, firstStop, cycles: cycles);
  }

  void _finish(TrafficOptions settings) {
    _kind = '';
    _phase = null;
    _phases.clear();
    _rule = null;
    _gap =
        settings.minimumGapSeconds +
        _between(0, settings.minimumGapSeconds * .5);
    settings.events.forEach((id, rule) {
      _clocks[id] = _nextOpportunity(rule.intervalSeconds);
    });
  }
}

import 'dart:math' as math;
import 'vehicle_events.dart';
import 'vehicle_options.dart';

class VehicleControls {
  const VehicleControls({
    this.automatic = true,
    this.gear = 1,
    this.throttle = 0,
    this.brake = 0,
  });
  final bool automatic;
  final int gear;
  final double throttle, brake;
  factory VehicleControls.fromJson(Map<String, dynamic> value) {
    final gear = checkedNumber(value, 'gear', 1);
    if (gear != gear.roundToDouble()) throw const FormatException('挡位必须为整数');
    return VehicleControls(
      automatic: checkedBool(value, 'automatic', true),
      gear: gear.toInt(),
      throttle: checkedNumber(value, 'throttle', 0),
      brake: checkedNumber(value, 'brake', 0),
    );
  }
}

class VehicleTelemetry {
  VehicleTelemetry(Map<String, dynamic> values)
    : values = Map.unmodifiable(values);
  final Map<String, dynamic> values;
  double get speedKph => (values['speedKph'] as num).toDouble();
  String get phase => values['phase'] as String;
  int get gear => values['gear'] as int;
  String get drivingEvent => values['drivingEvent'] as String;
  Map<String, dynamic> toJson() => Map.from(values);
  VehicleTelemetry withValues(Map<String, dynamic> changes) =>
      VehicleTelemetry({...values, ...changes});
  factory VehicleTelemetry.fixed(double speed, {String phase = 'cruising'}) =>
      VehicleTelemetry({
        'speedKph': speed,
        'targetSpeedKph': speed,
        'acceleration': 0.0,
        'gear': 0,
        'rpm': 0.0,
        'throttle': 0.0,
        'brake': 0.0,
        'controlMode': 'auto',
        'transmission': 'fixed',
        'powertrain': 'none',
        'phase': phase,
        'constraint': 'fixed',
        'traveledMeters': 0.0,
        'remainingMeters': 0.0,
        'actualAverageSpeedKph': 0.0,
        'drivingEvent': '',
        'eventStage': '',
        'eventRemainingSeconds': 0.0,
        'eventCycle': 0,
        'eventTotalCycles': 0,
      });
}

/// CTS longitudinal approximation. It is not a calibrated vehicle dynamics model.
class VehicleDriver {
  VehicleDriver(this.options, {DrivingRandom? random})
    : events = VehicleEventController(random ?? DefaultDrivingRandom());
  final VehicleOptions options;
  final VehicleEventController events;
  VehicleControls _controls = const VehicleControls();
  double _speed = 0,
      _acceleration = 0,
      _time = 0,
      _shiftRemaining = 0,
      _shiftCooldown = 0,
      _estimatedDistance = 0;
  int _gear = 1;
  void setControls(VehicleControls controls) {
    _controls = VehicleControls(
      automatic: options.transmission != 'manual' || controls.automatic,
      gear: controls.gear.clamp(0, options.gearRatios.length),
      throttle: controls.throttle.isFinite ? controls.throttle.clamp(0, 1) : 0,
      brake: controls.brake.isFinite ? controls.brake.clamp(0, 1) : 1,
    );
  }

  VehicleTelemetry tick(
    double seconds,
    double routeLimitKph,
    double remainingMeters, {
    String routeConstraint = 'cruise',
    DrivingContext? context,
  }) {
    if (!seconds.isFinite || seconds <= 0 || seconds > .25) {
      throw const FormatException('驾驶时间步必须为 0–0.25 秒');
    }
    if (!routeLimitKph.isFinite ||
        routeLimitKph < 0 ||
        !remainingMeters.isFinite ||
        remainingMeters < 0) {
      throw const FormatException('驾驶路线约束无效');
    }
    _time += seconds;
    _shiftRemaining = math.max(0, _shiftRemaining - seconds);
    _shiftCooldown = math.max(0, _shiftCooldown - seconds);
    final controls = _controls, automatic = controls.automatic;
    final style = options.drivingStyle == 'calm'
        ? .8
        : options.drivingStyle == 'sport'
        ? 1.2
        : 1.0;
    final comfortBrake = math.min(options.brakeDeceleration, 2.2 * style);
    _estimatedDistance += _speed * seconds;
    final incident = events.update(
      seconds,
      context ?? DrivingContext(_estimatedDistance),
      _speed * 3.6,
      remainingMeters,
      options,
      automatic,
    );
    final variation =
        options.averageSpeedKph *
        (.035 * math.sin(_time / 3.1) + .018 * math.sin(_time / 7.7 + 1.4)) *
        style;
    var target = math.min(
      options.maxSpeedKph,
      (options.averageSpeedKph + variation) * incident.cruiseMultiplier,
    );
    target = math.min(target, math.max(0, routeLimitKph));
    target = math.min(target, incident.speedLimitKph);
    final finishLimit =
        math.sqrt(2 * comfortBrake * math.max(0, remainingMeters - .1)) * 3.6;
    if (finishLimit < target) {
      target = finishLimit;
      routeConstraint = 'finish';
    }
    var rpm = engineRpm(_gear, _speed), warning = '';
    if (!automatic && controls.gear != _gear) {
      if (controls.gear > 0 &&
          engineRpm(controls.gear, _speed) > options.redlineRpm * 1.03) {
        warning = 'shiftProtected';
      } else {
        _shiftTo(controls.gear);
      }
    } else if (automatic &&
        !['cvt', 'singleSpeed'].contains(options.transmission) &&
        _shiftCooldown <= 0) {
      if (_gear == 0) {
        _shiftTo(1);
      } else if (rpm > options.upshiftRpm &&
          _gear < options.gearRatios.length) {
        _shiftTo(_gear + 1);
      } else if (rpm < options.downshiftRpm &&
          _gear > 1 &&
          engineRpm(_gear - 1, _speed) < options.redlineRpm) {
        _shiftTo(_gear - 1);
      }
    }
    rpm = engineRpm(_gear, _speed);
    var resistance = .07 + .00016 * _speed * _speed;
    if (options.powertrain == 'electric') resistance += .1;
    final desired = (target / 3.6 - _speed) * .8 * style;
    var brake = automatic
        ? (-desired / comfortBrake).clamp(0, 1) *
              comfortBrake /
              options.brakeDeceleration
        : controls.brake;
    var gearFactor = ['cvt', 'singleSpeed'].contains(options.transmission)
        ? 1.0
        : (options.gearRatios[math.max(0, _gear - 1)] /
                  options.gearRatios[0] *
                  2.5)
              .clamp(.32, 1.0);
    if (options.powertrain == 'hybrid') gearFactor = math.max(gearFactor, .5);
    final envelope = options.powertrain == 'electric'
        ? (1.15 - _speed / (options.maxSpeedKph / 3.6) * .6).clamp(.45, 1.0)
        : (1 - math.max(0, rpm / options.redlineRpm - .65) * 1.8).clamp(
            .2,
            1.0,
          );
    final availableAcceleration =
        options.maxAcceleration * gearFactor * envelope;
    var throttle = automatic
        ? ((desired + resistance) / availableAcceleration).clamp(
            0.0,
            options.drivingStyle == 'calm' ? .65 : 1.0,
          )
        : controls.throttle;
    if (incident.hold) {
      throttle = 0;
      brake = math.max(brake, .25);
    }
    var drive = _gear == 0 || _shiftRemaining > 0
        ? 0.0
        : availableAcceleration * throttle;
    if (rpm >= options.redlineRpm && _gear != 0) {
      drive = 0;
      warning = 'rpmLimit';
    }
    var acceleration = drive - resistance - options.brakeDeceleration * brake;
    if (_speed <= 0 && acceleration < 0) acceleration = 0;
    final jerk = !automatic && brake > .1
        ? 12.0
        : (options.powertrain == 'diesel' ? 1.1 : 3) * style;
    _acceleration += (acceleration - _acceleration).clamp(
      -jerk * seconds,
      jerk * seconds,
    );
    if (_gear == 0 || _shiftRemaining > 0) {
      _acceleration = math.min(_acceleration, acceleration);
    }
    _speed = (_speed + _acceleration * seconds).clamp(
      0,
      options.maxSpeedKph / 3.6,
    );
    if (_speed == 0 && _acceleration < 0) _acceleration = 0;
    rpm = _gear == 0
        ? options.idleRpm +
              throttle * (options.redlineRpm - options.idleRpm) * .7
        : engineRpm(_gear, _speed);
    var phase = _shiftRemaining > 0
        ? 'shifting'
        : _gear == 0
        ? 'neutral'
        : _speed < .05
        ? 'stopped'
        : brake > .05
        ? 'braking'
        : _acceleration > .08
        ? 'accelerating'
        : _acceleration < -.08
        ? 'coasting'
        : 'cruising';
    if (incident.hold && _speed < .05) phase = 'waiting';
    return VehicleTelemetry({
      'speedKph': _speed * 3.6,
      'targetSpeedKph': target,
      'acceleration': _acceleration,
      'gear': _gear,
      'rpm': rpm.clamp(
        options.powertrain == 'electric' ? 0 : options.idleRpm,
        options.redlineRpm,
      ),
      'throttle': throttle,
      'brake': brake,
      'controlMode': automatic ? 'auto' : 'manual',
      'transmission': options.transmission,
      'powertrain': options.powertrain,
      'phase': phase,
      'constraint': warning.isNotEmpty ? warning : routeConstraint,
      'traveledMeters': 0.0,
      'remainingMeters': 0.0,
      'actualAverageSpeedKph': 0.0,
      'drivingEvent': incident.kind,
      'eventStage': incident.stage,
      'eventRemainingSeconds': incident.remainingSeconds,
      'eventCycle': incident.cycle,
      'eventTotalCycles': incident.totalCycles,
    });
  }

  void _shiftTo(int gear) {
    _gear = gear;
    _shiftRemaining = options.transmission == 'dct'
        ? math.min(.18, options.shiftDuration)
        : options.shiftDuration;
    _shiftCooldown = _shiftRemaining + .5;
  }

  double engineRpm(int gear, double speed) {
    if (gear == 0) {
      return options.powertrain == 'electric' ? 0 : options.idleRpm;
    }
    if (options.transmission == 'cvt' && speed > .5) {
      return (1500 + speed * 45).clamp(
        options.idleRpm,
        math.min(2800, options.redlineRpm),
      );
    }
    return math.max(
      options.powertrain == 'electric' ? 0 : options.idleRpm,
      speed /
          (2 * math.pi * options.wheelRadius) *
          60 *
          options.gearRatios[gear - 1] *
          options.finalDrive,
    );
  }
}

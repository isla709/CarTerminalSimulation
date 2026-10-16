namespace TerminalSimulation.Wpf.Services;

public sealed record VehicleSimulationOptions
{
    public bool Enabled { get; init; }
    public bool RandomEventsEnabled { get; init; }
    public TrafficEventOptions TrafficEvents { get; init; } = new();
    public string Preset { get; init; } = "small-auto";
    public double MaxSpeedKph { get; init; } = 100;
    public double AverageSpeedKph { get; init; } = 50;
    public string DrivingStyle { get; init; } = "normal";
    public double MaxAcceleration { get; init; } = 2.6;
    public double BrakeDeceleration { get; init; } = 6;
    public string Transmission { get; init; } = "automatic";
    public string Powertrain { get; init; } = "petrol";
    public double[] GearRatios { get; init; } = [3.4, 2.1, 1.4, 1, .8, .64];
    public double FinalDrive { get; init; } = 4.1;
    public double WheelRadius { get; init; } = .3;
    public double IdleRpm { get; init; } = 800;
    public double RedlineRpm { get; init; } = 6500;
    public double UpshiftRpm { get; init; } = 3200;
    public double DownshiftRpm { get; init; } = 1200;
    public double ShiftDuration { get; init; } = .45;

    public VehicleSimulationOptions Validate()
    {
        static void Range(double value, double min, double max, string label)
        {
            if (!double.IsFinite(value) || value < min || value > max)
                throw new ArgumentException($"{label}必须为 {min}–{max}");
        }
        Range(MaxSpeedKph, 5, 300, "最大速度");
        Range(AverageSpeedKph, 1, MaxSpeedKph, "平均速度");
        Range(MaxAcceleration, .1, 12, "最大加速度");
        Range(BrakeDeceleration, .5, 12, "最大刹车减速度");
        Range(FinalDrive, 1, 15, "主减速比");
        Range(WheelRadius, .2, 1, "车轮半径");
        Range(IdleRpm, 400, 2000, "怠速转速");
        Range(RedlineRpm, 1500, 16000, "红线转速");
        Range(DownshiftRpm, IdleRpm, RedlineRpm - 300, "降挡转速");
        Range(UpshiftRpm, DownshiftRpm + 200, RedlineRpm, "升挡转速");
        Range(ShiftDuration, .05, 3, "换挡时间");
        if (DrivingStyle is not ("calm" or "normal" or "sport")) throw new ArgumentException("驾驶风格无效");
        if (Transmission is not ("manual" or "automatic" or "cvt" or "dct" or "singleSpeed")) throw new ArgumentException("变速箱类型无效");
        if (Powertrain is not ("petrol" or "diesel" or "electric" or "hybrid")) throw new ArgumentException("动力类型无效");
        var ratios = Transmission is "cvt" or "singleSpeed" ? new[] { 1d } : GearRatios?.ToArray();
        if (ratios is null || ratios.Length is < 1 or > 12) throw new ArgumentException("挡位数量必须为 1–12");
        for (var i = 0; i < ratios.Length; i++)
        {
            Range(ratios[i], .1, 20, $"{i + 1} 挡齿比");
            if (i > 0 && ratios[i] >= ratios[i - 1]) throw new ArgumentException("挡位齿比必须逐挡递减");
        }
        return this with { GearRatios = ratios, TrafficEvents = (TrafficEvents ?? throw new ArgumentException("交通事件设置不能为空")).Validate() };
    }

    public static IReadOnlyDictionary<string, VehicleSimulationOptions> Presets { get; } = new Dictionary<string, VehicleSimulationOptions>
    {
        ["small-auto"] = new(),
        ["small-manual"] = new() { Preset = "small-manual", Transmission = "manual", MaxAcceleration = 2.4, GearRatios = [3.6, 2.1, 1.36, 1, .82], UpshiftRpm = 3000, DownshiftRpm = 1100, ShiftDuration = .65 },
        ["electric"] = new() { Preset = "electric", Transmission = "singleSpeed", Powertrain = "electric", MaxSpeedKph = 140, AverageSpeedKph = 60, MaxAcceleration = 4.2, GearRatios = [1], FinalDrive = 9, WheelRadius = .31, RedlineRpm = 14000, UpshiftRpm = 12000, DownshiftRpm = 1800, ShiftDuration = .05 },
        ["manual-truck"] = new() { Preset = "manual-truck", Transmission = "manual", Powertrain = "diesel", MaxSpeedKph = 65, AverageSpeedKph = 32, DrivingStyle = "calm", MaxAcceleration = .7, BrakeDeceleration = 4.2, GearRatios = [12.2, 9.3, 7.2, 5.4, 4.1, 3.1, 2.3, 1.75, 1.3, 1], FinalDrive = 4.8, WheelRadius = .5, IdleRpm = 650, RedlineRpm = 2800, UpshiftRpm = 2100, DownshiftRpm = 900, ShiftDuration = .85 }
    };
}

public sealed record VehicleControls(bool Automatic = true, int Gear = 1, double Throttle = 0, double Brake = 0);

public sealed record VehicleTelemetry(double SpeedKph, double TargetSpeedKph, double Acceleration,
    int Gear, double Rpm, double Throttle, double Brake, string ControlMode, string Transmission,
    string Powertrain, string Phase, string Constraint, double TraveledMeters = 0, double RemainingMeters = 0,
    double ActualAverageSpeedKph = 0, string DrivingEvent = "", string EventStage = "", double EventRemainingSeconds = 0,
    int EventCycle = 0, int EventTotalCycles = 0);

// Behavioral longitudinal approximation, not a calibrated physics/traffic model.
// The same calculated velocity drives movement and protocol speed reporting.
internal sealed class VehicleDriver
{
    public VehicleSimulationOptions Options { get; }
    internal VehicleEventController Events { get; }
    private volatile VehicleControls _controls = new();
    private double _speed, _acceleration, _time, _shiftRemaining, _shiftCooldown, _estimatedDistance;
    private int _gear = 1;

    public VehicleDriver(VehicleSimulationOptions options, Random? random = null)
    {
        Options = options.Validate();
        Events = new VehicleEventController(random ?? new Random());
    }
    public void SetControls(VehicleControls controls) => _controls = controls with
    {
        Automatic = Options.Transmission != "manual" || controls.Automatic,
        Gear = Math.Clamp(controls.Gear, 0, Options.GearRatios.Length),
        Throttle = double.IsFinite(controls.Throttle) ? Math.Clamp(controls.Throttle, 0, 1) : 0,
        Brake = double.IsFinite(controls.Brake) ? Math.Clamp(controls.Brake, 0, 1) : 1
    };

    internal VehicleTelemetry Tick(double seconds, double routeLimitKph, double remainingMeters, string routeConstraint = "cruise", RouteDrivingContext? context = null)
    {
        if (!double.IsFinite(seconds) || seconds <= 0 || seconds > .25) throw new ArgumentOutOfRangeException(nameof(seconds));
        _time += seconds; _shiftRemaining = Math.Max(0, _shiftRemaining - seconds); _shiftCooldown = Math.Max(0, _shiftCooldown - seconds);
        var controls = _controls;
        var automatic = controls.Automatic;
        var style = Options.DrivingStyle == "calm" ? .8 : Options.DrivingStyle == "sport" ? 1.2 : 1;
        var comfortBrake = Math.Min(Options.BrakeDeceleration, 2.2 * style);
        _estimatedDistance += _speed * seconds;
        var incident = Events.Update(seconds, context ?? new RouteDrivingContext(_estimatedDistance), _speed * 3.6, remainingMeters, Options, automatic);
        var variation = Options.AverageSpeedKph * (.035 * Math.Sin(_time / 3.1) + .018 * Math.Sin(_time / 7.7 + 1.4)) * style;
        var target = Math.Min(Options.MaxSpeedKph, (Options.AverageSpeedKph + variation) * incident.CruiseMultiplier);
        target = Math.Min(target, Math.Max(0, routeLimitKph));
        target = Math.Min(target, incident.SpeedLimitKph);
        var finishLimit = Math.Sqrt(2 * comfortBrake * Math.Max(0, remainingMeters - .1)) * 3.6;
        if (finishLimit < target) { target = finishLimit; routeConstraint = "finish"; }
        var rpm = EngineRpm(_gear, _speed);
        var warning = "";
        if (!automatic && controls.Gear != _gear)
        {
            if (controls.Gear > 0 && EngineRpm(controls.Gear, _speed) > Options.RedlineRpm * 1.03) warning = "shiftProtected";
            else ShiftTo(controls.Gear);
        }
        else if (automatic && Options.Transmission is not ("cvt" or "singleSpeed") && _shiftCooldown <= 0)
        {
            if (_gear == 0) ShiftTo(1);
            else if (rpm > Options.UpshiftRpm && _gear < Options.GearRatios.Length) ShiftTo(_gear + 1);
            else if (rpm < Options.DownshiftRpm && _gear > 1 && EngineRpm(_gear - 1, _speed) < Options.RedlineRpm) ShiftTo(_gear - 1);
        }
        rpm = EngineRpm(_gear, _speed);
        var resistance = .07 + .00016 * _speed * _speed;
        if (Options.Powertrain == "electric") resistance += .1; // mild regenerative coast drag
        var desired = (target / 3.6 - _speed) * .8 * style;
        var brake = automatic ? Math.Clamp(-desired / comfortBrake, 0, 1) * comfortBrake / Options.BrakeDeceleration : controls.Brake;
        var gearFactor = Options.Transmission is "cvt" or "singleSpeed" ? 1 : Math.Clamp(Options.GearRatios[Math.Max(0, _gear - 1)] / Options.GearRatios[0] * 2.5, .32, 1);
        if (Options.Powertrain == "hybrid") gearFactor = Math.Max(gearFactor, .5); // approximate low-speed electric assist
        var envelope = Options.Powertrain == "electric" ? Math.Clamp(1.15 - _speed / (Options.MaxSpeedKph / 3.6) * .6, .45, 1)
            : Math.Clamp(1 - Math.Max(0, rpm / Options.RedlineRpm - .65) * 1.8, .2, 1);
        var availableAcceleration = Options.MaxAcceleration * gearFactor * envelope;
        var throttle = automatic ? Math.Clamp((desired + resistance) / availableAcceleration, 0, Options.DrivingStyle == "calm" ? .65 : 1) : controls.Throttle;
        if (incident.Hold) { throttle = 0; brake = Math.Max(brake, .25); }
        var drive = _gear == 0 || _shiftRemaining > 0 ? 0 : availableAcceleration * throttle;
        if (rpm >= Options.RedlineRpm && _gear != 0) { drive = 0; warning = "rpmLimit"; }
        var acceleration = drive - resistance - Options.BrakeDeceleration * brake;
        if (_speed <= 0 && acceleration < 0) acceleration = 0;
        var jerk = !automatic && brake > .1 ? 12 : (Options.Powertrain == "diesel" ? 1.1 : 3) * style;
        _acceleration += Math.Clamp(acceleration - _acceleration, -jerk * seconds, jerk * seconds);
        // Smoothing must not create traction during neutral or a gear change.
        if (_gear == 0 || _shiftRemaining > 0) _acceleration = Math.Min(_acceleration, acceleration);
        _speed = Math.Clamp(_speed + _acceleration * seconds, 0, Options.MaxSpeedKph / 3.6);
        if (_speed == 0 && _acceleration < 0) _acceleration = 0;
        rpm = _gear == 0 ? Options.IdleRpm + throttle * (Options.RedlineRpm - Options.IdleRpm) * .7 : EngineRpm(_gear, _speed);
        var phase = _shiftRemaining > 0 ? "shifting" : _gear == 0 ? "neutral" : _speed < .05 ? "stopped" : brake > .05 ? "braking" : _acceleration > .08 ? "accelerating" : _acceleration < -.08 ? "coasting" : "cruising";
        if (incident.Hold && _speed < .05) phase = "waiting";
        return new VehicleTelemetry(_speed * 3.6, target, _acceleration, _gear, Math.Clamp(rpm, Options.Powertrain == "electric" ? 0 : Options.IdleRpm, Options.RedlineRpm),
            throttle, brake, automatic ? "auto" : "manual", Options.Transmission, Options.Powertrain, phase,
            warning.Length > 0 ? warning : routeConstraint, DrivingEvent: incident.Kind, EventStage: incident.Stage, EventRemainingSeconds: incident.RemainingSeconds,
            EventCycle: incident.Cycle, EventTotalCycles: incident.TotalCycles);
    }

    private void ShiftTo(int gear)
    {
        _gear = gear;
        _shiftRemaining = Options.Transmission == "dct" ? Math.Min(.18, Options.ShiftDuration) : Options.ShiftDuration;
        _shiftCooldown = _shiftRemaining + .5;
    }
    private double EngineRpm(int gear, double speed)
    {
        if (gear == 0) return Options.Powertrain == "electric" ? 0 : Options.IdleRpm;
        if (Options.Transmission == "cvt" && speed > .5)
            return Math.Clamp(1500 + speed * 45, Options.IdleRpm, Math.Min(2800, Options.RedlineRpm));
        return Math.Max(Options.Powertrain == "electric" ? 0 : Options.IdleRpm,
            speed / (2 * Math.PI * Options.WheelRadius) * 60 * Options.GearRatios[gear - 1] * Options.FinalDrive);
    }
}

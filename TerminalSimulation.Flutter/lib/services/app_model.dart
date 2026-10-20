import 'dart:async';
import 'package:flutter/foundation.dart';
import '../core/bytes.dart';
import '../core/jt808.dart';
import '../core/route.dart';
import '../core/geo.dart';
import '../core/driving_route.dart';
import '../core/vehicle_driver.dart';
import '../core/vehicle_options.dart';
import 'storage.dart';
import 'terminal_session.dart';
import 'video_push.dart';
import 'map_credentials.dart';

class LogEntry {
  LogEntry(this.category, this.message) : time = DateTime.now();
  final DateTime time;
  final String category, message;
}

class AppModel extends ChangeNotifier {
  AppModel({
    this.storage,
    this.mapCredentials = const MapCredentials.unavailable(),
  }) {
    controller = TerminalController(
      session,
      onAuthCode: (value) {
        authCode = value;
        log('终端', '收到注册成功应答，鉴权码已更新');
      },
      onText: (text) {
        downlinks.insert(0, text);
        if (downlinks.length > 500) downlinks.removeLast();
        log('文本', text);
      },
      onPassthrough: (type, data) {
        log('透传', '类型 $type · ${hex(data)}');
      },
      onError: (text) => log('错误', text),
      onStatus: (text) {
        terminalStatus = text;
        log('终端', text);
      },
    );
    controller.location = locationSnapshot;
    controller.onVideoRequest =
        (host, port, channel, dataType, streamType) async {
          if (channel < 1 || channel > 36 || dataType > 1) return 3;
          final source = videoSources[channel];
          if (source == null) {
            log('媒体', '通道 $channel 未选择媒体源');
            return 1;
          }
          try {
            final stream = videoSessions[channel - 1];
            await stream.stop();
            stream.sim = session.phone;
            await stream.start(
              host: host,
              port: port,
              path: source.path,
              fps: source.fps,
            );
            return 0;
          } catch (e) {
            log('媒体', '$e');
            return 1;
          }
        };
    controller.onVideoControl = (channel, command) async {
      if (command != 0 || channel < 0 || channel > 36) return 3;
      if (channel == 0) {
        await Future.wait(videoSessions.map((s) => s.stop()));
      } else {
        await videoSessions[channel - 1].stop();
      }
      return 0;
    };
    _subscriptions.add(
      session.wire.listen((event) {
        if (event.outbound) {
          sentBytes += event.data.length;
        } else {
          receivedBytes += event.data.length;
        }
        log(event.outbound ? '发送' : '接收', hex(event.data));
      }),
    );
    _subscriptions.add(
      session.states.listen((_) {
        if (session.state != ConnectionState.connected) {
          heartbeat = false;
          reporting = false;
        }
        notifyListeners();
      }),
    );
    _subscriptions.add(session.errors.listen((message) => log('错误', message)));
  }
  final AppStorage? storage;
  final MapCredentials mapCredentials;
  final session = TerminalSession();
  final Map<int, ({String path, double fps})> videoSources = {};
  String terminalStatus = '等待连接';
  late final videoSessions = List.generate(
    36,
    (i) => VideoPushSession(
      channel: i + 1,
      sim: session.phone,
      onError: (error) => log('媒体', '通道 ${i + 1}：$error'),
    ),
  );
  late final TerminalController controller;
  final List<StreamSubscription<dynamic>> _subscriptions = [];
  final List<LogEntry> logs = [];
  final List<String> downlinks = [];
  int sentBytes = 0, receivedBytes = 0;
  String host = '127.0.0.1', phone = '013800000001', authCode = '';
  int port = 7611;
  ProtocolVersion version = ProtocolVersion.v2013;
  double latitude = 39.9042,
      longitude = 116.4074,
      speed = 60,
      oil = 50,
      mileage = 0,
      consumption = 8;
  int altitude = 0, direction = 0, status = 3, alarm = 0;
  Map<int, List<int>> attachments = {};
  bool heartbeat = false, reporting = false, reduceMotion = false, glass = true;
  String theme = 'system';
  RouteDocument? route;
  CoordinateSystem coordinateSystem = CoordinateSystem.gcj02;
  Timer? _playbackTimer, _notifyTimer;
  DrivingPlayback? _drivingPlayback;
  VehicleTelemetry? vehicleTelemetry;
  int _simulationGeneration = 0;
  bool simulationPreparing = false;
  bool _disposed = false;
  bool get simulating => _playbackTimer != null;
  LocationReport locationSnapshot() => LocationReport(
    latitude: latitude,
    longitude: longitude,
    altitude: altitude,
    speed: speed,
    direction: direction,
    status: status,
    alarm: alarm,
    attachments: Map<int, List<int>>.from(attachments),
  );
  void log(String category, String message) {
    if (_disposed) return;
    logs.insert(0, LogEntry(category, message));
    if (logs.length > 2000) logs.removeRange(2000, logs.length);
    _notifyTimer ??= Timer(const Duration(milliseconds: 50), () {
      _notifyTimer = null;
      if (!_disposed) notifyListeners();
    });
  }

  Future<void> guard(Future<void> Function() work) async {
    try {
      await work();
    } catch (e) {
      log('错误', '$e');
    }
  }

  Future<void> connect() =>
      session.connect(host, port, phone: phone, version: version);
  void toggleHeartbeat(int seconds) {
    if (!heartbeat) {
      controller.startHeartbeat(Duration(seconds: seconds));
    } else {
      controller.stopHeartbeat();
    }
    heartbeat = !heartbeat;
    notifyListeners();
  }

  void toggleReports(int milliseconds) {
    if (!reporting) {
      controller.startLocationReports(Duration(milliseconds: milliseconds));
    } else {
      controller.stopLocationReports();
    }
    reporting = !reporting;
    notifyListeners();
  }

  void applyRoute(RouteDocument document) {
    stopSimulation();
    route = document;
    vehicleTelemetry = null;
    coordinateSystem = document.coordinateSystem;
    latitude = document.points.first.lat;
    longitude = document.points.first.lng;
    speed = document.speed;
    notifyListeners();
  }

  Future<bool> startSimulation() async {
    if (route == null) throw StateError('请先载入路线');
    final document = route!;
    if (document.points.length < 2) throw const FormatException('路径至少需要两个点');
    VehicleOptions.fromJson(document.vehicle ?? {});
    stopSimulation();
    final generation = _simulationGeneration;
    simulationPreparing = true;
    notifyListeners();
    late RouteSpeedProfile geometry;
    try {
      geometry = await compute(RouteSpeedProfile.new, document.points);
    } catch (_) {
      if (generation == _simulationGeneration && !_disposed) {
        simulationPreparing = false;
        notifyListeners();
      }
      rethrow;
    }
    if (_disposed || generation != _simulationGeneration) return false;
    final playback = DrivingPlayback(document, geometry: geometry);
    _drivingPlayback = playback;
    simulationPreparing = false;
    speed = playback.telemetry.speedKph;
    vehicleTelemetry = playback.telemetry;
    if (playback.finished) {
      notifyListeners();
      return true;
    }
    final watch = Stopwatch()..start();
    var previous = Duration.zero;
    _playbackTimer = Timer.periodic(const Duration(milliseconds: 100), (_) {
      if (_disposed || generation != _simulationGeneration) return;
      final now = watch.elapsed, step = now - previous;
      previous = now;
      try {
        playback.advance(step, fixedSpeedKph: speed);
        latitude = playback.position.point.lat;
        longitude = playback.position.point.lng;
        direction = playback.position.bearing;
        vehicleTelemetry = playback.telemetry;
        speed = playback.telemetry.speedKph;
        if (playback.finished) stopSimulation();
      } catch (_) {
        stopSimulation();
        speed = 0;
        log('模拟', '驾驶模拟已停止，时间或路线参数无效');
      }
      notifyListeners();
    });
    notifyListeners();
    return true;
  }

  void setVehicleControls(VehicleControls controls) =>
      _drivingPlayback?.driver?.setControls(controls);

  void stopSimulation() {
    ++_simulationGeneration;
    simulationPreparing = false;
    _playbackTimer?.cancel();
    _playbackTimer = null;
    _drivingPlayback = null;
  }

  Future<void> loadSettings() async {
    final data = await storage?.read('settings.json');
    if (data == null) return;
    host = data['host'] as String? ?? host;
    port = data['port'] as int? ?? port;
    phone = data['phone'] as String? ?? phone;
    version = data['version'] == 'v2019'
        ? ProtocolVersion.v2019
        : ProtocolVersion.v2013;
    theme = data['theme'] as String? ?? 'system';
    glass = data['glass'] as bool? ?? true;
    reduceMotion = data['reduceMotion'] as bool? ?? false;
    notifyListeners();
  }

  Future<void> saveSettings() async {
    if (storage == null) throw StateError('应用存储不可用');
    // Auth codes and HTTP credentials are deliberately absent from plain settings.
    await storage!.write('settings.json', {
      'host': host,
      'port': port,
      'phone': phone,
      'version': version.name,
      'theme': theme,
      'glass': glass,
      'reduceMotion': reduceMotion,
    });
    log('配置', '配置已保存');
  }

  void changed() => notifyListeners();
  @override
  void dispose() {
    _disposed = true;
    stopSimulation();
    _notifyTimer?.cancel();
    for (final stream in videoSessions) {
      unawaited(stream.stop());
    }
    for (final subscription in _subscriptions) {
      unawaited(subscription.cancel());
    }
    unawaited(controller.dispose().then((_) => session.dispose()));
    super.dispose();
  }
}

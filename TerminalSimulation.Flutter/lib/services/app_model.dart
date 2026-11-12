import 'dart:async';
import 'dart:math' as math;
import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart' hide ConnectionState;
import 'package:flutter/services.dart' show PlatformException;
import '../core/bytes.dart';
import '../core/jt808.dart';
import '../core/location_attachments.dart';
import '../core/message_analysis.dart' show messageNames;
import '../core/route.dart';
import '../core/geo.dart';
import '../core/driving_route.dart';
import '../core/vehicle_driver.dart';
import '../core/vehicle_options.dart';
import 'storage.dart';
import 'terminal_session.dart';
import 'video_push.dart';
import 'map_credentials.dart';
import 'mobile_runtime.dart';

class LogEntry {
  LogEntry(this.category, this.message, {this.sequence = 0, Uint8List? raw})
    : time = DateTime.now(),
      raw = raw == null ? null : Uint8List.fromList(raw).asUnmodifiableView();
  final DateTime time;
  final int sequence;
  final String category, message;
  final Uint8List? raw;
  bool get communication =>
      const {'发送', '接收', '终端', '协议', '网络', '媒体', '透传', '文本'}.contains(category);
  int get storageBytes => message.length * 2 + (raw?.length ?? 0) + 128;
  String get full =>
      '${time.toIso8601String()} [$category] $message${raw == null ? '' : '\nRAW: ${hex(raw!)}'}';
}

class PassthroughEntry {
  PassthroughEntry(
    this.outbound,
    this.type,
    Uint8List data, {
    this.textMessage = false,
  }) : data = Uint8List.fromList(data).asUnmodifiableView(),
       time = DateTime.now();
  final bool outbound, textMessage;
  final int type;
  final Uint8List data;
  final DateTime time;
}

class AppModel extends ChangeNotifier with WidgetsBindingObserver {
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
        if (text == '鉴权成功') {
          _authenticated = true;
          _retryAttempt = 0;
          _restorePeriodicWork();
        } else if (text.startsWith('鉴权失败')) {
          _authenticated = false;
          _resumeReports = false;
        }
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
          _lastInbound.reset();
        }
        Jt808Packet? packet;
        try {
          packet = Jt808Codec.decode(event.data);
        } on FormatException {
          /* Keep malformed wire data for diagnosis. */
        }
        final summary = packet == null
            ? '无效 JT808 帧 · ${event.data.length} B'
            : '0x${packet.id.toRadixString(16).padLeft(4, '0').toUpperCase()} ${messageNames[packet.id] ?? '自定义消息'} · 流水 ${packet.sequence} · ${event.data.length} B';
        if (packet != null && packet.encryption == 0 && !packet.fragmented) {
          if ((packet.id == 0x0900 || packet.id == 0x8900) &&
              packet.body.isNotEmpty) {
            passthroughMessages.insert(
              0,
              PassthroughEntry(
                event.outbound,
                packet.body.first,
                Uint8List.sublistView(packet.body, 1),
              ),
            );
          } else if (packet.id == 0x8300 &&
              packet.body.length >=
                  (packet.version == ProtocolVersion.v2019 ? 2 : 1)) {
            passthroughMessages.insert(
              0,
              PassthroughEntry(
                false,
                packet.body.first,
                Uint8List.sublistView(
                  packet.body,
                  packet.version == ProtocolVersion.v2019 ? 2 : 1,
                ),
                textMessage: true,
              ),
            );
          }
          if (passthroughMessages.length > 500) {
            passthroughMessages.removeRange(500, passthroughMessages.length);
          }
        }
        log(event.outbound ? '发送' : '接收', summary, raw: event.data);
      }),
    );
    _subscriptions.add(
      session.states.listen((state) {
        if (state == ConnectionState.disconnected &&
            _connectedDuration.isRunning) {
          if (_connectedDuration.elapsed >= const Duration(seconds: 30)) {
            _retryAttempt = 0;
          }
          _connectedDuration.stop();
        }
        if (session.state != ConnectionState.connected) {
          if (_wantConnection) {
            _resumeHeartbeat = _resumeHeartbeat || heartbeat;
            _resumeReports = _resumeReports || reporting;
            _needsAuthentication = _needsAuthentication || _authenticated;
          }
          _authenticated = false;
          heartbeat = false;
          reporting = false;
        }
        if (state == ConnectionState.disconnected) _scheduleReconnect();
        notifyListeners();
      }),
    );
    _subscriptions.add(session.errors.listen((message) => log('错误', message)));
    runtime.onEvent = _runtimeEvent;
    WidgetsBinding.instance.addObserver(this);
    _attachmentTimer = Timer.periodic(const Duration(seconds: 1), (_) {
      if (_disposed) return;
      if (_wantConnection &&
          backgroundConnection &&
          _authenticated &&
          heartbeat &&
          session.state == ConnectionState.connected &&
          _lastInbound.elapsed >
              Duration(seconds: math.max(90, _heartbeatSeconds * 3))) {
        log('网络', '平台响应超时，正在恢复连接');
        unawaited(session.disconnect());
      }
      final fresh = hasDeviceFix;
      if (realLocation || _lastDeviceFresh != fresh) {
        _lastDeviceFresh = fresh;
        locationRevision++;
        notifyListeners();
      }
      if (locationAttachments.simulating &&
          locationAttachments.tick(
            realLocation && !fresh ? 0 : speed,
            _attachmentRandom,
          )) {
        notifyListeners();
      }
    });
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
  final List<PassthroughEntry> passthroughMessages = [];
  int logRevision = 0, _logSequence = 0, _logBytes = 0, droppedLogs = 0;
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
  LocationAttachments locationAttachments = LocationAttachments();
  final _attachmentRandom = math.Random();
  Timer? _attachmentTimer;
  final runtime = MobileRuntime();
  final _lastInbound = Stopwatch()..start();
  final _connectedDuration = Stopwatch();
  bool backgroundConnection = MobileRuntime.supported;
  bool realLocation = false, locationChanging = false;
  DeviceLocationFix? deviceFix, _lastGnssFix;
  int locationRevision = 0;
  int? _satellitesUsed, _satellitesVisible;
  Stopwatch? _satelliteAge;
  int? get satellitesUsed =>
      _satelliteAge != null && _satelliteAge!.elapsedMilliseconds <= 15000
      ? _satellitesUsed
      : null;
  int? get satellitesVisible =>
      _satelliteAge != null && _satelliteAge!.elapsedMilliseconds <= 15000
      ? _satellitesVisible
      : null;
  int? get reportGnssCount =>
      hasDeviceFix &&
          (deviceFix!.source.satellite ||
              deviceFix!.source == DeviceLocationSource.fused)
      ? satellitesUsed
      : null;
  String deviceLocationMessage = '等待系统定位', backgroundMessage = '';
  bool _lastDeviceFresh = false;
  bool get hasDeviceFix => realLocation && deviceFix?.valid == true;
  String get locationStatus => hasDeviceFix
      ? '${deviceFix!.source.label} · ${deviceFix!.quality}'
      : deviceFix != null
      ? '定位数据已过期，等待系统更新'
      : deviceLocationMessage;
  String get gnssStatus {
    if (!hasDeviceFix) return '尚无有效的实时位置';
    final source = deviceFix!.source;
    if (source.satellite) {
      final age = deviceFix!.ageMilliseconds ~/ 1000;
      return '最近位置来自 GNSS，$age 秒前更新';
    }
    if (source == DeviceLocationSource.fused ||
        source == DeviceLocationSource.unknown) {
      return 'GNSS 是否参与本次定位：系统未提供';
    }
    final previous = _lastGnssFix;
    return previous == null
        ? '尚未收到 GNSS 位置，当前使用${source.label}'
        : '已切换为${source.label}；最近 GNSS 位置在 ${previous.ageMilliseconds ~/ 1000} 秒前';
  }

  (double, double, int, double, int, CoordinateSystem)? _manualPosition;
  bool _wantConnection = false,
      _connecting = false,
      _authenticated = false,
      _needsAuthentication = false,
      _resumeHeartbeat = false,
      _resumeReports = false;
  int _connectionIntent = 0,
      _retryAttempt = 0,
      _heartbeatSeconds = 30,
      _reportInterval = 1000;
  Timer? _reconnectTimer;
  (String, int, String, ProtocolVersion)? _connectionTarget;
  bool get reconnecting =>
      _wantConnection &&
      backgroundConnection &&
      MobileRuntime.supported &&
      session.state == ConnectionState.disconnected;
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
  LocationReport locationSnapshot() {
    if (realLocation && !hasDeviceFix) {
      throw StateError('未获得有效系统位置，暂不发送位置汇报：$locationStatus');
    }
    return LocationReport(
      latitude: latitude,
      longitude: longitude,
      altitude: altitude,
      speed: speed,
      direction: direction,
      status: realLocation ? status | 2 : status,
      time: realLocation ? deviceFix!.time : null,
      alarm: alarm,
      rawAttachments: locationAttachmentBytes(),
    );
  }

  Uint8List locationAttachmentBytes() => locationAttachments.encode(
    realLocation && !hasDeviceFix ? 0 : speed,
    systemLocation: realLocation,
    gnssCount: reportGnssCount,
  );

  Future<void> _syncRuntime() => runtime.configure(
    connection: backgroundConnection && _wantConnection,
    location: realLocation,
  );

  Future<void> setBackgroundConnection(bool value) async {
    final previous = backgroundConnection;
    backgroundConnection = value;
    try {
      await _syncRuntime();
      if (!value) {
        _reconnectTimer?.cancel();
        _reconnectTimer = null;
      }
      backgroundMessage = value ? '后台连接保护已开启' : '后台连接保护已关闭';
      changed();
    } catch (_) {
      backgroundConnection = previous;
      rethrow;
    }
  }

  Future<void> setRealLocation(bool value) async {
    if (locationChanging || realLocation == value) return;
    if (!MobileRuntime.supported) throw StateError('此设备不支持系统定位');
    locationChanging = true;
    if (value) {
      _manualPosition = (
        latitude,
        longitude,
        altitude,
        speed,
        direction,
        coordinateSystem,
      );
      stopSimulation();
      vehicleTelemetry = null;
      deviceFix = null;
      _lastGnssFix = null;
      _satelliteAge = null;
      coordinateSystem = CoordinateSystem.wgs84;
      deviceLocationMessage = '等待系统定位，可使用 GNSS、网络或室内定位';
    }
    realLocation = value;
    changed();
    try {
      await _syncRuntime();
      if (!value) _restoreManualPosition();
    } catch (_) {
      realLocation = !value;
      if (value) _restoreManualPosition();
      rethrow;
    } finally {
      locationChanging = false;
      changed();
    }
  }

  void _restoreManualPosition() {
    final p = _manualPosition;
    if (p != null) {
      latitude = p.$1;
      longitude = p.$2;
      altitude = p.$3;
      speed = p.$4;
      direction = p.$5;
      coordinateSystem = p.$6;
    }
    _manualPosition = null;
    deviceFix = null;
    _lastGnssFix = null;
    _satelliteAge = null;
  }

  void _runtimeEvent(Map<Object?, Object?> event) {
    if (_disposed) return;
    if (event['type'] == 'fix' && realLocation) {
      try {
        final fix = DeviceLocationFix(event);
        if (!fix.valid) {
          deviceLocationMessage = fix.rejectionReason!;
          changed();
          return;
        }
        final previous = deviceFix;
        if (fix.source.satellite &&
            (_lastGnssFix == null || !fix.time.isBefore(_lastGnssFix!.time))) {
          _lastGnssFix = fix;
        }
        // A GNSS callback can arrive slightly behind a less accurate network fix.
        // Permit that improvement without accepting arbitrary out-of-order fixes.
        final improvesAccuracy =
            previous != null &&
            fix.ageMilliseconds < 5000 &&
            fix.accuracy > 0 &&
            (previous.accuracy == 0 || fix.accuracy < previous.accuracy * 0.5);
        if (previous != null &&
            fix.time.isBefore(previous.time) &&
            !improvesAccuracy) {
          return;
        }
        // Prefer a recent accurate fix; permit a coarse fallback after five seconds.
        if (previous != null &&
            previous.valid &&
            previous.ageMilliseconds < 5000 &&
            previous.accuracy > 0 &&
            (fix.accuracy == 0 ||
                fix.accuracy > math.max(25, previous.accuracy * 2))) {
          return;
        }
        deviceFix = fix;
        deviceLocationMessage = '';
        latitude = fix.latitude;
        longitude = fix.longitude;
        altitude = (fix.altitude ?? 0).round().clamp(0, 65535);
        speed = ((fix.speedMps ?? 0) * 3.6).clamp(0, 6553.5);
        direction = (((fix.bearing ?? 0) % 360 + 360) % 360).truncate();
        _lastDeviceFresh = true;
        changed();
      } catch (_) {
        deviceLocationMessage = '定位数据无效，等待新位置';
        changed();
      }
    } else if (event['type'] == 'satellites' && realLocation) {
      _satellitesUsed = (event['used'] as num?)?.toInt();
      _satellitesVisible = (event['visible'] as num?)?.toInt();
      _satelliteAge = Stopwatch()..start();
      locationRevision++;
      changed();
    } else if (event['type'] == 'status' && realLocation) {
      final text = '${event['message'] ?? ''}';
      if (event['invalidate'] == true) {
        deviceFix = null;
        _satelliteAge = null;
      }
      if (deviceLocationMessage != text || event['invalidate'] == true) {
        deviceLocationMessage = text;
        changed();
      }
    } else if (event['type'] == 'stop') {
      backgroundMessage = '${event['message'] ?? '后台任务已停止'}';
      realLocation = false;
      _restoreManualPosition();
      unawaited(disconnect().catchError((Object _) {}));
      log('系统', backgroundMessage);
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed &&
        !_disposed &&
        !locationChanging &&
        !_connecting) {
      if (_wantConnection || realLocation) {
        unawaited(
          _syncRuntime().catchError((Object e) {
            backgroundMessage = '后台运行不可用，请检查系统权限';
            log('系统', backgroundMessage);
          }),
        );
      }
      _scheduleReconnect();
    }
  }

  void log(String category, String message, {Uint8List? raw}) {
    if (_disposed) return;
    final entry = LogEntry(
      category,
      message.length > 8192 ? '${message.substring(0, 8192)}…（内容已截断）' : message,
      sequence: ++_logSequence,
      raw: raw,
    );
    logs.insert(0, entry);
    _logBytes += entry.storageBytes;
    while (logs.length > 2000 || _logBytes > 8 * 1024 * 1024) {
      _logBytes -= logs.removeLast().storageBytes;
      droppedLogs++;
    }
    logRevision++;
    _notifyTimer ??= Timer(const Duration(milliseconds: 50), () {
      _notifyTimer = null;
      if (!_disposed) notifyListeners();
    });
  }

  void clearLogs() {
    logs.clear();
    _logBytes = 0;
    droppedLogs = 0;
    logRevision++;
    changed();
  }

  void clearPassthrough() {
    passthroughMessages.clear();
    logRevision++;
    changed();
  }

  Future<void> guard(Future<void> Function() work) async {
    try {
      await work();
    } catch (e) {
      log('错误', '$e');
    }
  }

  Future<void> connect() async {
    if (_connecting) return;
    if (host.trim().isEmpty || port < 1 || port > 65535) {
      throw const FormatException('服务器地址或端口无效');
    }
    Jt808Codec.encode(
      Jt808Packet(id: 2, phone: phone, sequence: 0, version: version),
    );
    _needsAuthentication = _resumeHeartbeat = _resumeReports = _authenticated =
        false;
    _connectionTarget = (host, port, phone, version);
    _wantConnection = true;
    ++_connectionIntent;
    _retryAttempt = 0;
    await _connectOnce();
  }

  Future<void> _connectOnce() async {
    final target = _connectionTarget;
    if (_disposed || !_wantConnection || _connecting || target == null) return;
    _connecting = true;
    final intent = _connectionIntent;
    try {
      await _syncRuntime();
      if (!_wantConnection || intent != _connectionIntent || _disposed) return;
      await session.connect(
        target.$1,
        target.$2,
        phone: target.$3,
        version: target.$4,
      );
      _lastInbound.reset();
      _connectedDuration
        ..reset()
        ..start();
      if (!_wantConnection || intent != _connectionIntent || _disposed) {
        await session.disconnect();
        return;
      }
      if (_needsAuthentication && authCode.isNotEmpty) {
        await controller.authenticate(authCode);
      } else {
        _restorePeriodicWork();
      }
    } catch (e) {
      if (e is FormatException || e is RangeError || e is PlatformException) {
        _wantConnection = false;
        await session.disconnect();
        unawaited(_syncRuntime().catchError((Object _) {}));
      } else {
        _scheduleReconnect(force: true);
      }
      rethrow;
    } finally {
      _connecting = false;
    }
  }

  void _restorePeriodicWork() {
    if (session.state != ConnectionState.connected ||
        (_needsAuthentication && !_authenticated)) {
      return;
    }
    _needsAuthentication = false;
    if (_resumeHeartbeat ||
        backgroundConnection && MobileRuntime.supported && _authenticated) {
      controller.startHeartbeat(Duration(seconds: _heartbeatSeconds));
      heartbeat = true;
    }
    if (_resumeReports) {
      controller.startLocationReports(Duration(milliseconds: _reportInterval));
      reporting = true;
    }
    _resumeHeartbeat = _resumeReports = false;
  }

  void _scheduleReconnect({bool force = false}) {
    if (_disposed ||
        !_wantConnection ||
        !backgroundConnection ||
        !MobileRuntime.supported ||
        session.state != ConnectionState.disconnected ||
        _reconnectTimer != null ||
        _connecting && !force) {
      return;
    }
    final seconds = math.min(30, 1 << _retryAttempt.clamp(0, 5));
    _retryAttempt++;
    _reconnectTimer = Timer(Duration(seconds: seconds), () {
      _reconnectTimer = null;
      if (_wantConnection && !_disposed) {
        unawaited(
          _connectOnce().catchError((Object e) {
            log('网络', '连接中断，正在等待重连');
          }),
        );
      }
    });
    terminalStatus = '连接中断，${seconds}s 后重连';
  }

  Future<void> disconnect() async {
    _wantConnection = false;
    ++_connectionIntent;
    _reconnectTimer?.cancel();
    _reconnectTimer = null;
    _resumeReports = _resumeHeartbeat = _needsAuthentication = _authenticated =
        false;
    await session.disconnect();
    await _syncRuntime();
    terminalStatus = '已断开';
    changed();
  }

  void toggleHeartbeat(int seconds) {
    _heartbeatSeconds = seconds;
    if (!heartbeat) {
      controller.startHeartbeat(Duration(seconds: seconds));
    } else {
      controller.stopHeartbeat();
    }
    heartbeat = !heartbeat;
    notifyListeners();
  }

  void toggleReports(int milliseconds) {
    _reportInterval = milliseconds;
    if (!reporting) {
      locationSnapshot().encode();
      controller.startLocationReports(Duration(milliseconds: milliseconds));
    } else {
      controller.stopLocationReports();
    }
    reporting = !reporting;
    notifyListeners();
  }

  void applyRoute(RouteDocument document) {
    if (realLocation) throw StateError('请先关闭设备系统定位');
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
    if (realLocation) throw StateError('请先关闭设备系统定位');
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
    backgroundConnection =
        data['backgroundConnection'] as bool? ?? MobileRuntime.supported;
    if (data['locationAttachments']
        case final Map<String, dynamic> attachments) {
      try {
        locationAttachments = LocationAttachments.fromJson(attachments);
      } on FormatException catch (e) {
        log('配置', '位置附加项配置无效，已使用默认值：${e.message}');
      }
    }
    notifyListeners();
  }

  Future<void> saveSettings() async {
    if (storage == null) throw StateError('应用存储不可用');
    locationAttachments.encode(speed);
    // Auth codes and HTTP credentials are deliberately absent from plain settings.
    await storage!.write('settings.json', {
      'host': host,
      'port': port,
      'phone': phone,
      'version': version.name,
      'theme': theme,
      'glass': glass,
      'reduceMotion': reduceMotion,
      'backgroundConnection': backgroundConnection,
      'locationAttachments': locationAttachments.toJson(),
    });
    log('配置', '配置已保存');
  }

  void changed() => notifyListeners();
  @override
  void dispose() {
    _disposed = true;
    WidgetsBinding.instance.removeObserver(this);
    _wantConnection = false;
    _reconnectTimer?.cancel();
    unawaited(runtime.dispose().catchError((Object _) {}));
    stopSimulation();
    _notifyTimer?.cancel();
    _attachmentTimer?.cancel();
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

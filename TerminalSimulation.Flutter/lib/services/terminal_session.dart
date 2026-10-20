import 'dart:async';
import 'dart:io';
import 'dart:typed_data';
import 'package:fast_gbk/fast_gbk.dart';
import '../core/bytes.dart';
import '../core/jt808.dart';

enum ConnectionState { disconnected, connecting, connected }

class WireEvent {
  WireEvent(this.outbound, List<int> data)
    : data = Uint8List.fromList(data),
      time = DateTime.now();
  final bool outbound;
  final Uint8List data;
  final DateTime time;
}

/// A single direct TCP session. Generations invalidate callbacks and queued writes.
class TerminalSession {
  final _packets = StreamController<Jt808Packet>.broadcast(sync: true);
  final _wire = StreamController<WireEvent>.broadcast(sync: true);
  final _states = StreamController<ConnectionState>.broadcast(sync: true);
  final _errors = StreamController<String>.broadcast(sync: true);
  Stream<Jt808Packet> get packets => _packets.stream;
  Stream<WireEvent> get wire => _wire.stream;
  Stream<ConnectionState> get states => _states.stream;
  Stream<String> get errors => _errors.stream;
  ConnectionState _state = ConnectionState.disconnected;
  ConnectionState get state => _state;
  Socket? _socket;
  StreamSubscription<Uint8List>? _subscription;
  Future<void> _sendTail = Future.value();
  int _generation = 0, _sequence = 0, _queued = 0;
  bool _disposed = false;
  String _phone = '013800000001';
  ProtocolVersion _version = ProtocolVersion.v2013;
  String get phone => _phone;
  ProtocolVersion get version => _version;
  int get epoch => _generation;

  void _setState(ConnectionState state) {
    _state = state;
    if (!_disposed) _states.add(state);
  }

  Future<void> connect(
    String host,
    int port, {
    required String phone,
    required ProtocolVersion version,
  }) async {
    if (_disposed) throw StateError('会话已释放');
    // Validate identity before changing the active session.
    Jt808Codec.encode(
      Jt808Packet(id: 2, phone: phone, sequence: 0, version: version),
    );
    if (host.trim().isEmpty || port < 1 || port > 65535) {
      throw const FormatException('服务器地址或端口无效');
    }
    await disconnect();
    final generation = ++_generation;
    _phone = phone;
    _version = version;
    _sequence = 0;
    _setState(ConnectionState.connecting);
    try {
      final socket = await Socket.connect(
        host.trim(),
        port,
        timeout: const Duration(seconds: 10),
      );
      if (generation != _generation || _disposed) {
        socket.destroy();
        return;
      }
      socket.setOption(SocketOption.tcpNoDelay, true);
      _socket = socket;
      final framer = Jt808Framer();
      _subscription = socket.listen(
        (data) {
          if (generation != _generation) return;
          for (final frame in framer.add(data)) {
            _wire.add(WireEvent(false, frame));
            try {
              _packets.add(Jt808Codec.decode(frame));
            } on FormatException catch (e) {
              _errors.add(e.message);
            }
          }
        },
        onError: (Object e) {
          if (generation == _generation) {
            _errors.add('$e');
            unawaited(disconnect());
          }
        },
        onDone: () {
          if (generation == _generation) unawaited(disconnect());
        },
        cancelOnError: true,
      );
      _setState(ConnectionState.connected);
    } catch (e) {
      if (generation == _generation) {
        _setState(ConnectionState.disconnected);
        rethrow;
      }
    }
  }

  Future<int> send(int id, [List<int> body = const []]) async {
    final sequence = _sequence++ & 0xffff;
    await sendFrame(
      Jt808Codec.encode(
        Jt808Packet(
          id: id,
          phone: _phone,
          sequence: sequence,
          version: _version,
          body: body,
        ),
      ),
    );
    return sequence;
  }

  Future<void> sendFrame(List<int> bytes) {
    if (_state != ConnectionState.connected || _socket == null) {
      return Future.error(StateError('请先连接平台'));
    }
    if (_queued >= 256) return Future.error(StateError('发送队列已满，请降低发送频率'));
    final generation = _generation, socket = _socket!;
    final snapshot = Uint8List.fromList(bytes);
    _queued++;
    final operation = _sendTail
        .then((_) async {
          if (generation != _generation || _socket != socket) {
            throw StateError('连接已改变，取消旧会话发送');
          }
          socket.add(snapshot);
          try {
            await socket.flush().timeout(const Duration(seconds: 10));
          } catch (_) {
            if (generation == _generation) await disconnect();
            rethrow;
          }
          if (generation == _generation) _wire.add(WireEvent(true, snapshot));
        })
        .whenComplete(() {
          _queued--;
        });
    _sendTail = operation.then<void>(
      (_) {},
      onError: (Object _, StackTrace _) {},
    );
    return operation;
  }

  Future<void> disconnect() async {
    ++_generation;
    final subscription = _subscription;
    _subscription = null;
    final socket = _socket;
    _socket = null;
    socket?.destroy();
    _setState(ConnectionState.disconnected);
    await subscription?.cancel();
  }

  Future<void> dispose() async {
    await disconnect();
    await _sendTail;
    _disposed = true;
    await Future.wait([
      _packets.close(),
      _wire.close(),
      _states.close(),
      _errors.close(),
    ]);
  }
}

/// Downlink handlers are independent from widgets and operate on a session snapshot.
class TerminalController {
  TerminalController(
    this.session, {
    this.onText,
    this.onPassthrough,
    this.onAuthCode,
    this.onError,
    this.onStatus,
  }) {
    _subscription = session.packets.listen((packet) {
      unawaited(_dispatch(packet));
    });
    _stateSubscription = session.states.listen((state) {
      if (state != ConnectionState.connected) {
        stopHeartbeat();
        stopLocationReports();
      }
    });
  }
  final TerminalSession session;
  TerminalIdentity identity = const TerminalIdentity();
  LocationReport Function()? location;
  final void Function(String text)? onText, onAuthCode, onError, onStatus;
  Future<int> Function(
    String host,
    int port,
    int channel,
    int dataType,
    int streamType,
  )?
  onVideoRequest;
  Future<int> Function(int channel, int command)? onVideoControl;
  final void Function(int type, List<int> data)? onPassthrough;
  late final StreamSubscription<Jt808Packet> _subscription;
  late final StreamSubscription<ConnectionState> _stateSubscription;
  Timer? _heartbeat, _report;
  bool _heartbeatBusy = false, _reportBusy = false;
  Future<void> register() async {
    await session.send(0x0100, identity.registerBody(session.version));
  }

  Future<void> authenticate(String code) async {
    if (code.isEmpty) throw const FormatException('鉴权码不能为空');
    await session.send(0x0102, identity.authBody(code, session.version));
  }

  Future<void> report() async {
    final snapshot = location?.call();
    if (snapshot == null) throw StateError('位置未配置');
    await session.send(0x0200, snapshot.encode());
  }

  void startHeartbeat(Duration interval) {
    if (interval < const Duration(seconds: 1)) {
      throw const FormatException('心跳间隔至少 1 秒');
    }
    stopHeartbeat();
    _heartbeat = Timer.periodic(interval, (_) async {
      if (_heartbeatBusy) return;
      _heartbeatBusy = true;
      try {
        await session.send(2);
      } catch (e) {
        onError?.call('$e');
      } finally {
        _heartbeatBusy = false;
      }
    });
  }

  void stopHeartbeat() {
    _heartbeat?.cancel();
    _heartbeat = null;
  }

  void startLocationReports(Duration interval) {
    if (interval < const Duration(milliseconds: 100)) {
      throw const FormatException('汇报间隔至少 100 ms');
    }
    stopLocationReports();
    _report = Timer.periodic(interval, (_) async {
      if (_reportBusy) return;
      _reportBusy = true;
      try {
        await report();
      } catch (e) {
        onError?.call('$e');
      } finally {
        _reportBusy = false;
      }
    });
  }

  void stopLocationReports() {
    _report?.cancel();
    _report = null;
  }

  Future<void> passthrough(int type, List<int> data) async {
    final w = ByteWriter();
    w.u8(type);
    w.add(data);
    await session.send(0x0900, w.take());
  }

  Future<void> _dispatch(Jt808Packet packet) async {
    // Reject packets for other terminals and unsupported encrypted/fragmented bodies.
    if (packet.phone !=
            session.phone.padLeft(
              packet.version == ProtocolVersion.v2019 ? 20 : 12,
              '0',
            ) ||
        packet.version != session.version ||
        packet.encryption != 0 ||
        packet.fragmented) {
      return;
    }
    final epoch = session.epoch;
    try {
      final r = ByteReader(packet.body);
      switch (packet.id) {
        case 0x8100:
          r.u16();
          final result = r.u8();
          if (result == 0) {
            onAuthCode?.call(gbk.decode(r.read(r.remaining)));
            onStatus?.call('已注册，待鉴权');
          } else {
            onStatus?.call('注册失败：$result');
          }
          return;
        case 0x8001:
          r.u16();
          final id = r.u16(), result = r.u8();
          if (id == 0x0102) {
            onStatus?.call(result == 0 ? '鉴权成功' : '鉴权失败：$result');
          }
          return;
        case 0x8107:
          await session.send(0x0107, identity.attributesBody(packet.version));
          return;
        case 0x8104:
          await session.send(0x0104, identity.parametersBody(packet.sequence));
          return;
        case 0x9101:
          final host = String.fromCharCodes(r.read(r.u8())),
              tcp = r.u16(),
              udp = r.u16(),
              channel = r.u8(),
              dataType = r.u8(),
              streamType = r.u8();
          final result =
              await onVideoRequest?.call(
                host,
                tcp > 0 ? tcp : udp,
                channel,
                dataType,
                streamType,
              ) ??
              3;
          if (session.epoch == epoch) await _ack(packet, result);
          return;
        case 0x9102:
          final channel = r.u8(), command = r.u8();
          r.u8();
          r.u8();
          final result = await onVideoControl?.call(channel, command) ?? 3;
          if (session.epoch == epoch) await _ack(packet, result);
          return;
        case 0x8300:
          r.u8();
          if (packet.version == ProtocolVersion.v2019) r.u8();
          onText?.call(gbk.decode(r.read(r.remaining)));
        case 0x8900:
          final type = r.u8();
          onPassthrough?.call(type, r.read(r.remaining));
        case 0x8201:
          if (location != null) {
            final w = ByteWriter();
            w.u16(packet.sequence);
            w.add(location!().encode());
            await session.send(0x0201, w.take());
            return;
          }
        default:
          if (packet.id < 0x8000) return;
          // Unsupported commands are acknowledged as unsupported, never as implemented.
          await _ack(packet, 3);
          return;
      }
      if (session.epoch == epoch) await _ack(packet, 0);
    } catch (e) {
      onError?.call('下行 0x${packet.id.toRadixString(16)}：$e');
    }
  }

  Future<void> _ack(Jt808Packet packet, int result) async {
    final w = ByteWriter();
    w.u16(packet.sequence);
    w.u16(packet.id);
    w.u8(result);
    await session.send(1, w.take());
  }

  Future<void> dispose() async {
    stopHeartbeat();
    stopLocationReports();
    await _subscription.cancel();
    await _stateSubscription.cancel();
  }
}

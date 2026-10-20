import 'dart:async';
import 'dart:io';
import 'dart:typed_data';
import 'package:flutter/foundation.dart';
import '../core/jt1078.dart';

class VideoPushSession {
  VideoPushSession({
    required this.channel,
    required this.sim,
    this.onChanged,
    this.onError,
  });
  final int channel;
  String sim;
  final VoidCallback? onChanged;
  final void Function(String)? onError;
  Socket? _socket;
  StreamSubscription<Uint8List>? _read;
  Future<void>? _task;
  Completer<void>? _cancel;
  int _generation = 0;
  int bytesSent = 0;
  bool _connecting = false;
  bool get running => _socket != null;
  bool get connecting => _connecting;
  Future<void> start({
    required String host,
    required int port,
    required String path,
    double fps = 25,
  }) async {
    if (!fps.isFinite || fps <= 0 || fps > 120) {
      throw const FormatException('帧率范围为 0–120 fps（不含 0）');
    }
    await stop();
    final generation = ++_generation;
    _connecting = true;
    onChanged?.call();
    try {
      final file = File(path);
      if (await file.length() > 256 * 1024 * 1024) {
        throw const FormatException('裸流文件超过当前 256 MiB 解析上限');
      }
      final data = await file.readAsBytes();
      final frames = await compute(parseH264, data);
      if (generation != _generation) return;
      final socket = await Socket.connect(
        host,
        port,
        timeout: const Duration(seconds: 10),
      );
      if (generation != _generation) {
        socket.destroy();
        return;
      }
      socket.setOption(SocketOption.tcpNoDelay, true);
      _socket = socket;
      _cancel = Completer<void>();
      bytesSent = 0;
      _read = socket.listen(
        (_) {},
        onDone: () {
          if (generation == _generation) unawaited(stop());
        },
        onError: (Object e) {
          if (generation == _generation) {
            onError?.call('$e');
            unawaited(stop());
          }
        },
        cancelOnError: true,
      );
      _task = _push(socket, frames, fps, generation, _cancel!.future);
    } finally {
      if (generation == _generation) {
        _connecting = false;
        onChanged?.call();
      }
    }
  }

  Future<void> _push(
    Socket socket,
    List<H264Frame> frames,
    double fps,
    int generation,
    Future<void> cancelled,
  ) async {
    final watch = Stopwatch()..start(),
        base = DateTime.now().millisecondsSinceEpoch;
    var sequence = 0, frameIndex = 0, lastI = 0, previous = 0;
    double scheduleOffset = 0;
    final framePeriod = 1000000 / fps;
    try {
      while (generation == _generation) {
        final frame = frames[frameIndex % frames.length],
            timestamp = base + (frameIndex * 1000 / fps).floor();
        final packets = Jt1078Packet.videoFrame(
          sim: sim,
          channel: channel,
          sequence: sequence,
          data: frame.data,
          keyFrame: frame.keyFrame,
          timestamp: timestamp,
          lastFrameInterval: previous == 0 ? 0 : timestamp - previous,
          lastIFrameInterval: lastI == 0 ? 0 : timestamp - lastI,
        );
        final aggregate = BytesBuilder(copy: false);
        for (final packet in packets) {
          aggregate.add(packet);
        }
        sequence = (sequence + packets.length) & 0xffff;
        final bytes = aggregate.takeBytes();
        socket.add(bytes);
        await socket.flush().timeout(const Duration(seconds: 10));
        if (generation != _generation) return;
        bytesSent += bytes.length;
        previous = timestamp;
        if (frame.keyFrame) lastI = timestamp;
        frameIndex++;
        onChanged?.call();
        final delay =
            (scheduleOffset +
                    frameIndex * framePeriod -
                    watch.elapsedMicroseconds)
                .round();
        if (delay > 0) {
          await Future.any([
            Future<void>.delayed(Duration(microseconds: delay)),
            cancelled,
          ]);
        } else if (delay < -1000000) {
          scheduleOffset = watch.elapsedMicroseconds - frameIndex * framePeriod;
        }
      }
    } catch (e) {
      if (generation == _generation) {
        onError?.call('$e');
        _socket = null;
        socket.destroy();
      }
    } finally {
      if (generation == _generation) onChanged?.call();
    }
  }

  Future<void> stop() async {
    ++_generation;
    _connecting = false;
    final task = _task;
    _task = null;
    _cancel?.complete();
    _cancel = null;
    final socket = _socket;
    _socket = null;
    socket?.destroy();
    await _read?.cancel();
    _read = null;
    await task;
    onChanged?.call();
  }
}

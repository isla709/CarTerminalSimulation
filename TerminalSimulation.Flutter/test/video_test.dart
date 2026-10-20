import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';
import 'package:flutter_test/flutter_test.dart';
import 'package:terminal_simulation/core/bytes.dart';
import 'package:terminal_simulation/core/jt1078.dart';
import 'package:terminal_simulation/services/video_push.dart';

void main() {
  final fixtures =
      jsonDecode(File('test/fixtures/jt1078-csharp.json').readAsStringSync())
          as List;
  for (final fixture in fixtures) {
    test('JT1078 C# oracle ${fixture['name']}', () {
      final audio = fixture['name'] == 'audio';
      final packet = Jt1078Packet(
        sim: '013800000001',
        channel: 1,
        sequence: 65535,
        body: [0, 0, 0, 1, 0x65, 0x88],
        type: audio ? AvDataType.audio : AvDataType.iFrame,
        payloadType: audio ? 6 : 98,
        timestamp: 1792368000000,
        lastIFrameInterval: 80,
        lastFrameInterval: 40,
      );
      expect(hex(packet.encode()).replaceAll(' ', ''), fixture['hex']);
    });
  }
  test('JT1078 fragmentation and sequence wrap', () {
    final packets = Jt1078Packet.videoFrame(
      sim: '1',
      channel: 36,
      sequence: 65535,
      data: List.filled(2000, 0x42),
      keyFrame: true,
      timestamp: 100,
    );
    expect(packets.length, 3);
    expect(packets.map((p) => p[15]), [1, 3, 2]);
    expect(packets.map((p) => p[5]), [98, 98, 226]);
    expect((packets[1][6] << 8) | packets[1][7], 0);
    expect(packets[0].length, 980);
    expect(packets.last.length, 130);
  });
  test('Annex-B 3/4 byte codes, multi-slice frames and SPS/PPS repeat', () {
    final bytes = Uint8List.fromList([
      0,
      0,
      0,
      1,
      0x67,
      0x42,
      0,
      0,
      1,
      0x68,
      0xce,
      0,
      0,
      1,
      0x65,
      0x80,
      0,
      0,
      1,
      0x65,
      0x40,
      0,
      0,
      1,
      0x41,
      0x80,
      0,
      0,
      1,
      0x65,
      0x80,
    ]);
    final frames = parseH264(bytes);
    expect(frames.length, 3);
    expect(frames.map((f) => f.keyFrame), [true, false, true]);
    expect(frames.last.data.take(6), [0, 0, 0, 1, 0x67, 0x42]);
  });
  test('push and stop loopback session without residual sends', () async {
    final directory = await Directory.systemTemp.createTemp('cts-video-'),
        server = await ServerSocket.bind(InternetAddress.loopbackIPv4, 0);
    final sockets = <Socket>[], received = Completer<void>();
    final listener = server.listen((socket) {
      sockets.add(socket);
      socket.listen((data) {
        if (data.isNotEmpty && !received.isCompleted) received.complete();
      });
    });
    final path = '${directory.path}/sample.h264';
    await File(path).writeAsBytes([
      0,
      0,
      0,
      1,
      0x67,
      0x42,
      0,
      0,
      1,
      0x68,
      0xce,
      0,
      0,
      1,
      0x65,
      0x80,
    ]);
    final session = VideoPushSession(channel: 1, sim: '1');
    try {
      await session.start(host: '127.0.0.1', port: server.port, path: path);
      await received.future.timeout(const Duration(seconds: 3));
      expect(session.running, isTrue);
      await session.stop();
      final bytes = session.bytesSent;
      await Future<void>.delayed(const Duration(milliseconds: 100));
      expect(session.bytesSent, bytes);
      expect(session.running, isFalse);
      for (var i = 0; i < 5; i++) {
        await session.start(host: '127.0.0.1', port: server.port, path: path);
        await session.stop();
      }
    } finally {
      await session.stop();
      for (final socket in sockets) {
        socket.destroy();
      }
      await listener.cancel();
      await server.close();
      await directory.delete(recursive: true);
    }
  });
}

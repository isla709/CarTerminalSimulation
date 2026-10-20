import 'dart:async';
import 'dart:io';
import 'dart:convert';
import 'package:terminal_simulation/core/bytes.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:terminal_simulation/core/jt808.dart';
import 'package:terminal_simulation/services/terminal_session.dart';

void main() {
  test(
    'query attributes/parameters and remote video controls use the current session',
    () async {
      final server = await ServerSocket.bind(InternetAddress.loopbackIPv4, 0),
          session = TerminalSession();
      final received = <Jt808Packet>[],
          complete = Completer<void>(),
          accepted = Completer<Socket>(),
          calls = <String>[];
      final controller = TerminalController(session, onStatus: calls.add)
        ..identity = const TerminalIdentity(
          province: 11,
          city: 1101,
          plate: '京A00001',
        );
      controller.onVideoRequest =
          (host, port, channel, dataType, streamType) async {
            calls.add('$host:$port/$channel/$dataType/$streamType');
            return 0;
          };
      controller.onVideoControl = (channel, command) async {
        calls.add('control:$channel/$command');
        return 0;
      };
      final listener = server.listen((socket) {
        accepted.complete(socket);
        final framer = Jt808Framer();
        socket.listen((bytes) {
          for (final frame in framer.add(bytes)) {
            received.add(Jt808Codec.decode(frame));
            if (received.length == 4 && !complete.isCompleted) {
              complete.complete();
            }
          }
        }, onError: (Object _) {});
      });
      try {
        await session.connect(
          '127.0.0.1',
          server.port,
          phone: '013800000001',
          version: ProtocolVersion.v2013,
        );
        final socket = await accepted.future;
        for (final request in [
          Jt808Packet(id: 0x8107, phone: session.phone, sequence: 99),
          Jt808Packet(id: 0x8104, phone: session.phone, sequence: 100),
          Jt808Packet(
            id: 0x9101,
            phone: session.phone,
            sequence: 101,
            body: [9, ...ascii.encode('127.0.0.1'), 0x04, 0x36, 0, 0, 1, 1, 0],
          ),
          Jt808Packet(
            id: 0x9102,
            phone: session.phone,
            sequence: 102,
            body: [0, 0, 0, 0],
          ),
        ]) {
          socket.add(Jt808Codec.encode(request));
        }
        await complete.future.timeout(const Duration(seconds: 3));
        expect(received.map((p) => p.id), [0x107, 0x104, 1, 1]);
        final fixtures =
            jsonDecode(
                  File('test/fixtures/jt808-csharp.json').readAsStringSync(),
                )
                as List;
        for (final entry in [('attributes', 0x107), ('parameters', 0x104)]) {
          final fixture = fixtures.firstWhere(
            (p) => p['name'] == entry.$1 && p['version'] == 'v2013',
          );
          expect(
            received.firstWhere((p) => p.id == entry.$2).body,
            Jt808Codec.decode(unhex(fixture['hex'])).body,
          );
        }
        expect(calls, containsAll(['127.0.0.1:1078/1/1/0', 'control:0/0']));
        expect(received[2].body, [0, 101, 0x91, 1, 0]);
        expect(received[3].body, [0, 102, 0x91, 2, 0]);
      } finally {
        await controller.dispose();
        await session.dispose();
        if (accepted.isCompleted) (await accepted.future).destroy();
        await listener.cancel();
        await server.close();
      }
    },
  );
  test(
    'real loopback TCP ordered send, register downlink and malformed recovery',
    () async {
      final server = await ServerSocket.bind(InternetAddress.loopbackIPv4, 0),
          session = TerminalSession();
      final received = <Jt808Packet>[],
          code = Completer<String>(),
          errors = <String>[];
      final controller = TerminalController(
        session,
        onAuthCode: (value) {
          if (!code.isCompleted) code.complete(value);
        },
      );
      final errorSub = session.errors.listen(errors.add);
      final accepted = Completer<Socket>();
      final listener = server.listen((socket) {
        accepted.complete(socket);
        final framer = Jt808Framer();
        socket.listen((data) {
          for (final frame in framer.add(data)) {
            final packet = Jt808Codec.decode(frame);
            received.add(packet);
            if (packet.id == 0x100) {
              final reply = Jt808Codec.encode(
                Jt808Packet(
                  id: 0x8100,
                  phone: packet.phone,
                  sequence: 1,
                  body: [
                    packet.sequence >> 8,
                    packet.sequence & 255,
                    0,
                    65,
                    66,
                    67,
                  ],
                ),
              );
              socket.add([0x7e, ...List.filled(13, 1), 0x7e]);
              socket.add(reply.take(4).toList());
              socket.add(reply.skip(4).toList());
            }
          }
        });
      });
      try {
        await session.connect(
          '127.0.0.1',
          server.port,
          phone: '013800000001',
          version: ProtocolVersion.v2013,
        );
        await controller.register();
        expect(await code.future.timeout(const Duration(seconds: 3)), 'ABC');
        await Future.wait(List.generate(20, (_) => session.send(2)));
        await Future<void>.delayed(const Duration(milliseconds: 100));
        expect(received.map((p) => p.sequence), List.generate(21, (i) => i));
        expect(errors, isNotEmpty);
        await session.disconnect();
        expect(session.state, ConnectionState.disconnected);
        await expectLater(session.send(2), throwsStateError);
      } finally {
        await controller.dispose();
        await errorSub.cancel();
        await session.dispose();
        if (accepted.isCompleted) (await accepted.future).destroy();
        await listener.cancel();
        await server.close();
      }
    },
  );
}

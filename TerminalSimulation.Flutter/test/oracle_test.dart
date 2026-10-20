import 'dart:convert';
import 'dart:io';
import 'package:flutter_test/flutter_test.dart';
import 'package:terminal_simulation/core/bytes.dart';
import 'package:terminal_simulation/core/jt808.dart';

void main() {
  final samples =
      jsonDecode(File('test/fixtures/jt808-csharp.json').readAsStringSync())
          as List;
  for (final sample in samples) {
    test('C# CTS oracle ${sample['version']} ${sample['name']}', () {
      final version = ProtocolVersion.values.byName(sample['version']);
      const identity = TerminalIdentity(
        province: 11,
        city: 1101,
        plate: '京A00001',
      );
      final body = switch (sample['name']) {
        'heartbeat' => <int>[],
        'register' => identity.registerBody(version),
        'auth' => identity.authBody('AUTH', version),
        'attributes' => identity.attributesBody(version),
        'parameters' => identity.parametersBody(100),
        'location' => LocationReport(
          latitude: 39.9,
          longitude: 116.4,
          altitude: 30,
          speed: 60.5,
          direction: 90,
          alarm: 4,
          status: 3,
          time: DateTime(2026, 10, 19, 12, 34, 56),
        ).encode(),
        _ => throw StateError('Unknown oracle sample'),
      };
      final frame = Jt808Codec.encode(
        Jt808Packet(
          id: sample['id'],
          phone: sample['phone'],
          sequence: sample['sequence'],
          version: version,
          body: body,
        ),
      );
      expect(hex(frame).replaceAll(' ', ''), sample['hex']);
    });
  }
}

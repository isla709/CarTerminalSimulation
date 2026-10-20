import 'dart:typed_data';
import 'package:flutter_test/flutter_test.dart';
import 'package:terminal_simulation/core/bytes.dart';
import 'package:terminal_simulation/core/jt808.dart';

void main() {
  test('known 2013 heartbeat bytes', () {
    final bytes = Jt808Codec.encode(
      Jt808Packet(id: 2, phone: '013800000001', sequence: 1),
    );
    expect(hex(bytes), '7E 00 02 00 00 01 38 00 00 00 01 00 01 3B 7E');
  });
  for (final version in ProtocolVersion.values) {
    test('${version.name} escape and body-length boundaries', () {
      for (final length in [0, 1, 100, 1023]) {
        final p = Jt808Packet(
          id: 0x900,
          phone: '13800000001',
          sequence: 65535,
          version: version,
          body: List.generate(length, (i) => i % 2 == 0 ? 0x7e : 0x7d),
        );
        final frame = Jt808Codec.encode(p), decoded = Jt808Codec.decode(frame);
        expect(decoded.id, p.id);
        expect(decoded.sequence, 65535);
        expect(decoded.version, version);
        expect(decoded.body, p.body);
      }
      expect(
        () => Jt808Codec.encode(
          Jt808Packet(
            id: 2,
            phone: '1',
            sequence: 0,
            version: version,
            body: Uint8List(1024),
          ),
        ),
        throwsFormatException,
      );
    });
  }
  test('reject checksum, malformed escape and length', () {
    final frame = Jt808Codec.encode(
      Jt808Packet(id: 2, phone: '1', sequence: 0),
    );
    final broken = Uint8List.fromList(frame)..[2] = 3;
    expect(() => Jt808Codec.decode(broken), throwsFormatException);
    expect(
      () => Jt808Codec.decode([0x7e, ...List.filled(12, 0), 0x7d, 3, 0x7e]),
      throwsFormatException,
    );
    expect(
      () => Jt808Codec.encode(Jt808Packet(id: 2, phone: 'abc', sequence: 0)),
      throwsFormatException,
    );
  });
  test('split and coalesced TCP frames, garbage and bounded recovery', () {
    final a = Jt808Codec.encode(Jt808Packet(id: 2, phone: '1', sequence: 1)),
        b = Jt808Codec.encode(Jt808Packet(id: 3, phone: '1', sequence: 2));
    final framer = Jt808Framer();
    expect(framer.add([1, 2, 3, ...a.take(5)]).toList(), isEmpty);
    expect(
      framer.add([...a.skip(5), ...b]).map(Jt808Codec.decode).map((p) => p.id),
      [2, 3],
    );
    expect(
      framer
          .add([0x7e, ...List.filled(70000, 1), ...a])
          .map(Jt808Codec.decode)
          .single
          .id,
      2,
    );
  });
  test('location signed coordinates, GBK identity and attachment bytes', () {
    final location = LocationReport(
      latitude: -31.25,
      longitude: -120.5,
      speed: 60.5,
      altitude: 30,
      direction: 90,
      status: 3,
      alarm: 4,
      time: DateTime(2026, 10, 19, 12, 34, 56),
      attachments: {
        1: [0, 0, 0, 100],
        0xee: [0x7d, 0x7e],
      },
    );
    final bytes = location.encode(), decoded = LocationReport.decode(bytes);
    expect(hex(bytes.take(8)), '00 00 00 04 00 00 00 0F');
    expect(decoded.latitude, -31.25);
    expect(decoded.longitude, -120.5);
    expect(decoded.speed, 60.5);
    expect(decoded.attachments[0xee], [0x7d, 0x7e]);
    expect(decoded.time, DateTime(2026, 10, 19, 12, 34, 56));
    final body = const TerminalIdentity(
      plate: '京A',
    ).registerBody(ProtocolVersion.v2013);
    expect(hex(body.sublist(37)), 'BE A9 41');
  });
}

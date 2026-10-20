import 'dart:typed_data';
import 'package:fast_gbk/fast_gbk.dart';
import 'bytes.dart';

enum ProtocolVersion { v2013, v2019 }

class Jt808Packet {
  Jt808Packet({
    required this.id,
    required this.phone,
    required this.sequence,
    List<int> body = const [],
    this.version = ProtocolVersion.v2013,
    this.protocolVersion = 1,
    this.encryption = 0,
    this.packageCount,
    this.packageIndex,
  }) : body = Uint8List.fromList(body);
  final int id, sequence, protocolVersion, encryption;
  final String phone;
  final ProtocolVersion version;
  final Uint8List body;
  final int? packageCount, packageIndex;
  bool get fragmented => packageCount != null;
  Map<String, Object?> toJson() => {
    '消息ID': id.toRadixString(16).padLeft(4, '0'),
    '协议': version.name,
    '终端手机号': phone,
    '流水号': sequence,
    '加密方式': encryption,
    '总包数': packageCount,
    '包序号': packageIndex,
    '消息体长度': body.length,
    '消息体': hex(body),
  };
}

class Jt808Codec {
  static Uint8List encode(Jt808Packet packet) {
    if (packet.body.length > 1023) {
      throw const FormatException('消息体超过 1023 字节，必须分包发送');
    }
    if ((packet.packageCount == null) != (packet.packageIndex == null) ||
        (packet.fragmented &&
            (packet.packageCount! < 1 ||
                packet.packageIndex! < 1 ||
                packet.packageIndex! > packet.packageCount!))) {
      throw const FormatException('无效分包头');
    }
    if (packet.encryption < 0 || packet.encryption > 7) {
      throw const FormatException('无效加密标志');
    }
    final writer = ByteWriter();
    writer.u16(packet.id);
    writer.u16(
      packet.body.length |
          packet.encryption << 10 |
          (packet.fragmented ? 0x2000 : 0) |
          (packet.version == ProtocolVersion.v2019 ? 0x4000 : 0),
    );
    if (packet.version == ProtocolVersion.v2019) {
      writer.u8(packet.protocolVersion);
    }
    writer.bcd(packet.phone, packet.version == ProtocolVersion.v2019 ? 10 : 6);
    writer.u16(packet.sequence);
    if (packet.fragmented) {
      writer.u16(packet.packageCount!);
      writer.u16(packet.packageIndex!);
    }
    writer.add(packet.body);
    final content = writer.take();
    final checksum = content.fold<int>(0, (a, b) => a ^ b);
    final escaped = <int>[0x7e];
    for (final b in [...content, checksum]) {
      if (b == 0x7e) {
        escaped.addAll([0x7d, 2]);
      } else if (b == 0x7d) {
        escaped.addAll([0x7d, 1]);
      } else {
        escaped.add(b);
      }
    }
    escaped.add(0x7e);
    return Uint8List.fromList(escaped);
  }

  static Jt808Packet decode(List<int> frame) {
    if (frame.length < 15 || frame.first != 0x7e || frame.last != 0x7e) {
      throw const FormatException('JT808 帧边界或长度无效');
    }
    final raw = <int>[];
    for (var i = 1; i < frame.length - 1; i++) {
      var b = frame[i];
      if (b == 0x7e) throw const FormatException('帧内出现未转义边界');
      if (b == 0x7d) {
        if (++i >= frame.length - 1 || (frame[i] != 1 && frame[i] != 2)) {
          throw const FormatException('无效转义');
        }
        b = frame[i] == 1 ? 0x7d : 0x7e;
      }
      raw.add(b);
    }
    if (raw.fold<int>(0, (a, b) => a ^ b) != 0) {
      throw const FormatException('校验码不匹配');
    }
    final r = ByteReader(raw.sublist(0, raw.length - 1));
    final id = r.u16(), properties = r.u16();
    final version = properties & 0x4000 != 0
        ? ProtocolVersion.v2019
        : ProtocolVersion.v2013;
    final protocolVersion = version == ProtocolVersion.v2019 ? r.u8() : 0;
    final phone = r.bcd(version == ProtocolVersion.v2019 ? 10 : 6),
        sequence = r.u16();
    final fragmented = properties & 0x2000 != 0;
    final count = fragmented ? r.u16() : null,
        index = fragmented ? r.u16() : null;
    if (fragmented && (count! < 1 || index! < 1 || index > count)) {
      throw const FormatException('无效分包头');
    }
    if (r.remaining != (properties & 0x3ff)) {
      throw const FormatException('消息体长度与头部不一致');
    }
    return Jt808Packet(
      id: id,
      phone: phone,
      sequence: sequence,
      body: r.read(r.remaining),
      version: version,
      protocolVersion: protocolVersion,
      encryption: properties >> 10 & 7,
      packageCount: count,
      packageIndex: index,
    );
  }
}

/// Incremental delimiter framing; noise and oversized frames are bounded.
class Jt808Framer {
  final List<int> _buffer = [];
  bool _inside = false;
  Iterable<Uint8List> add(List<int> data) sync* {
    for (final b in data) {
      if (b == 0x7e) {
        if (_inside && _buffer.length > 1) {
          yield Uint8List.fromList([..._buffer, b]);
        }
        _inside = true;
        _buffer
          ..clear()
          ..add(b);
      } else if (_inside) {
        _buffer.add(b);
        if (_buffer.length >= 65536) {
          _buffer.clear();
          _inside = false;
        }
      }
    }
  }
}

class TerminalIdentity {
  const TerminalIdentity({
    this.province = 0,
    this.city = 0,
    this.manufacturer = 'CTS',
    this.model = 'TerminalSimulation',
    this.terminalId = '0000001',
    this.plateColor = 1,
    this.plate = '测试车辆',
    this.imei = '123456789012345',
    this.software = 'V1.0.0',
    this.iccid = '89860000000000000000',
    this.hardware = '1.0',
  });
  final int province, city, plateColor;
  final String manufacturer,
      model,
      terminalId,
      plate,
      imei,
      software,
      iccid,
      hardware;
  Uint8List attributesBody(ProtocolVersion version) {
    final w = ByteWriter();
    final modern = version == ProtocolVersion.v2019;
    w.u16(0);
    w.add(fixedGbk(manufacturer, modern ? 11 : 5));
    w.add(fixedGbk(model, modern ? 30 : 20));
    w.add(fixedGbk(terminalId, modern ? 30 : 7));
    w.bcd(iccid, 10);
    final hw = gbk.encode(hardware), sw = gbk.encode(software);
    w.u8(hw.length);
    w.add(hw);
    w.u8(sw.length);
    w.add(sw);
    w.u8(1);
    w.u8(1);
    return w.take();
  }

  Uint8List parametersBody(int replySequence) {
    Uint8List u16(int value) {
      final w = ByteWriter();
      w.u16(value);
      return w.take();
    }

    void mediaSettings(ByteWriter w) {
      w.u8(0);
      w.u8(5);
      w.u16(250);
      w.u8(25);
      w.u32(0);
    }

    final av = ByteWriter();
    mediaSettings(av);
    mediaSettings(av);
    av.u16(1);
    av.u8(0);
    final channels = ByteWriter();
    channels.u8(36);
    channels.u8(0);
    channels.u8(36);
    for (var i = 1; i <= 36; i++) {
      channels.add([i, i, 0, 0]);
    }
    final individual = ByteWriter();
    individual.u8(36);
    for (var i = 1; i <= 36; i++) {
      individual.u8(i);
      mediaSettings(individual);
      mediaSettings(individual);
      individual.u16(1);
    }
    final params = <int, List<int>>{
      0x81: u16(province),
      0x82: u16(city),
      0x83: gbk.encode(plate),
      0x84: [plateColor],
      0x75: av.take(),
      0x76: channels.take(),
      0x77: individual.take(),
    };
    final w = ByteWriter();
    w.u16(replySequence);
    w.u8(params.length);
    for (final entry in params.entries) {
      w.u32(entry.key);
      // Preserve the original CTS/JT808 2.7.8 BYTE narrowing of 0x0077's length.
      // This is a legacy compatibility behavior; see migration-status.md.
      w.u8(entry.value.length & 0xff);
      w.add(entry.value);
    }
    return w.take();
  }

  Uint8List registerBody(ProtocolVersion version) {
    final w = ByteWriter();
    w.u16(province);
    w.u16(city);
    final modern = version == ProtocolVersion.v2019;
    w.add(fixedGbk(manufacturer, modern ? 11 : 5));
    w.add(fixedGbk(model, modern ? 30 : 20));
    w.add(fixedGbk(terminalId, modern ? 30 : 7));
    w.u8(plateColor);
    w.add(gbk.encode(plate));
    return w.take();
  }

  Uint8List authBody(String code, ProtocolVersion version) {
    final w = ByteWriter();
    final bytes = gbk.encode(code);
    if (version == ProtocolVersion.v2019) w.u8(bytes.length);
    w.add(bytes);
    if (version == ProtocolVersion.v2019) {
      w.add(fixedGbk(imei, 15));
      w.add(fixedGbk(software, 20));
    }
    return w.take();
  }
}

Uint8List fixedGbk(String value, int width) {
  final bytes = gbk.encode(value);
  if (bytes.length > width) throw FormatException('字段 $value 超过 $width 字节');
  return Uint8List(width)..setRange(0, bytes.length, bytes);
}

class LocationReport {
  const LocationReport({
    required this.latitude,
    required this.longitude,
    this.altitude = 0,
    this.speed = 0,
    this.direction = 0,
    this.alarm = 0,
    this.status = 3,
    this.time,
    this.attachments = const {},
  });
  final double latitude, longitude, speed;
  final int altitude, direction, alarm, status;
  final DateTime? time;
  final Map<int, List<int>> attachments;
  Uint8List encode() {
    if (!latitude.isFinite ||
        latitude.abs() > 90 ||
        !longitude.isFinite ||
        longitude.abs() > 180 ||
        !speed.isFinite ||
        speed < 0 ||
        speed > 6553.5 ||
        direction < 0 ||
        direction > 359) {
      throw const FormatException('位置、速度或方向超出范围');
    }
    final w = ByteWriter();
    w.u32(alarm);
    w.u32((status & ~0xc) | (latitude < 0 ? 4 : 0) | (longitude < 0 ? 8 : 0));
    w.u32((latitude.abs() * 1e6).truncate());
    w.u32((longitude.abs() * 1e6).truncate());
    w.u16(altitude);
    w.u16((speed * 10).truncate());
    w.u16(direction);
    final t = time ?? DateTime.now();
    w.bcd(
      '${t.year % 100}'.padLeft(2, '0') +
          [
            t.month,
            t.day,
            t.hour,
            t.minute,
            t.second,
          ].map((v) => '$v'.padLeft(2, '0')).join(),
      6,
    );
    for (final entry in attachments.entries) {
      w.u8(entry.key);
      w.u8(entry.value.length);
      w.add(entry.value);
    }
    return w.take();
  }

  static LocationReport decode(List<int> body) {
    final r = ByteReader(body);
    final alarm = r.u32(), status = r.u32();
    final lat = r.u32() / 1e6 * (status & 4 != 0 ? -1 : 1),
        lng = r.u32() / 1e6 * (status & 8 != 0 ? -1 : 1);
    final altitude = r.u16(), speed = r.u16() / 10, direction = r.u16();
    final t = r.bcd(6);
    final time = DateTime(
      2000 + int.parse(t.substring(0, 2)),
      int.parse(t.substring(2, 4)),
      int.parse(t.substring(4, 6)),
      int.parse(t.substring(6, 8)),
      int.parse(t.substring(8, 10)),
      int.parse(t.substring(10, 12)),
    );
    final attachments = <int, List<int>>{};
    while (r.remaining > 0) {
      final id = r.u8(), length = r.u8();
      attachments[id] = r.read(length);
    }
    return LocationReport(
      latitude: lat,
      longitude: lng,
      altitude: altitude,
      speed: speed,
      direction: direction,
      status: status,
      alarm: alarm,
      time: time,
      attachments: attachments,
    );
  }
}

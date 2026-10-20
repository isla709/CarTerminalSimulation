import 'dart:math' as math;
import 'dart:typed_data';
import 'bytes.dart';

enum AvDataType { iFrame, pFrame, bFrame, audio, transparent }

class Jt1078Packet {
  const Jt1078Packet({
    required this.sim,
    required this.channel,
    required this.sequence,
    required this.body,
    this.type = AvDataType.iFrame,
    this.payloadType = 98,
    this.marker = true,
    this.fragment = 0,
    this.timestamp = 0,
    this.lastIFrameInterval = 0,
    this.lastFrameInterval = 0,
  });
  final String sim;
  final int channel,
      sequence,
      payloadType,
      fragment,
      timestamp,
      lastIFrameInterval,
      lastFrameInterval;
  final bool marker;
  final AvDataType type;
  final List<int> body;
  Uint8List encode() {
    if (channel < 1 ||
        channel > 36 ||
        payloadType < 0 ||
        payloadType > 127 ||
        fragment < 0 ||
        fragment > 3 ||
        body.length > 950) {
      throw const FormatException('JT1078 通道、分片或负载无效');
    }
    final w = ByteWriter();
    w.u32(0x30316364);
    w.u8(0x80);
    w.u8(payloadType | (marker ? 0x80 : 0));
    w.u16(sequence & 0xffff);
    w.bcd(sim, 6);
    w.u8(channel);
    w.u8(type.index << 4 | fragment);
    if (type != AvDataType.transparent) w.u64(timestamp);
    if (type.index <= 2) {
      w.u16(lastIFrameInterval.clamp(0, 65535));
      w.u16(lastFrameInterval.clamp(0, 65535));
    }
    w.u16(body.length);
    w.add(body);
    return w.take();
  }

  static List<Uint8List> videoFrame({
    required String sim,
    required int channel,
    required int sequence,
    required List<int> data,
    required bool keyFrame,
    required int timestamp,
    int lastIFrameInterval = 0,
    int lastFrameInterval = 0,
  }) {
    if (data.isEmpty) throw const FormatException('视频帧为空');
    final result = <Uint8List>[];
    for (var offset = 0; offset < data.length; offset += 950) {
      final length = math.min(950, data.length - offset),
          last = offset + length == data.length;
      final fragment = data.length <= 950
          ? 0
          : offset == 0
          ? 1
          : last
          ? 2
          : 3;
      result.add(
        Jt1078Packet(
          sim: sim,
          channel: channel,
          sequence: sequence++,
          body: data.sublist(offset, offset + length),
          marker: last,
          fragment: fragment,
          type: keyFrame ? AvDataType.iFrame : AvDataType.pFrame,
          timestamp: timestamp,
          lastIFrameInterval: lastIFrameInterval,
          lastFrameInterval: lastFrameInterval,
        ).encode(),
      );
    }
    return result;
  }
}

class H264Frame {
  H264Frame(this.data, this.keyFrame);
  final Uint8List data;
  final bool keyFrame;
}

/// Annex-B access-unit grouping follows the original CTS first-slice heuristic.
List<H264Frame> parseH264(Uint8List data) {
  final nalus = <Uint8List>[];
  var previous = -1, previousLength = 0, i = 0;
  void append(int end) {
    if (previous < 0) return;
    final start = previous + previousLength;
    if (end > start) {
      nalus.add(Uint8List.fromList([0, 0, 0, 1, ...data.sublist(start, end)]));
    }
  }

  while (i < data.length - 2) {
    if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1) {
      final four = i > 0 && data[i - 1] == 0, start = four ? i - 1 : i;
      append(start);
      previous = start;
      previousLength = four ? 4 : 3;
      i += 3;
    } else {
      i++;
    }
  }
  append(data.length);
  final frames = <H264Frame>[],
      metadata = <Uint8List>[],
      current = <Uint8List>[];
  Uint8List? sps, pps;
  var key = false;
  int type(Uint8List n) => n.length > 4 ? n[4] & 31 : 0;
  void flush() {
    if (current.isEmpty) return;
    final builder = BytesBuilder(copy: false);
    if (key) {
      if (![...metadata, ...current].any((n) => type(n) == 7) && sps != null) {
        builder.add(sps);
      }
      if (![...metadata, ...current].any((n) => type(n) == 8) && pps != null) {
        builder.add(pps);
      }
    }
    for (final n in [...metadata, ...current]) {
      builder.add(n);
    }
    frames.add(H264Frame(builder.takeBytes(), key));
    metadata.clear();
    current.clear();
    key = false;
  }

  for (final nalu in nalus) {
    final t = type(nalu),
        vcl = [1, 2, 3, 4, 5, 19].contains(t),
        first = vcl && nalu.length > 5 && nalu[5] & 0x80 != 0;
    if (current.isNotEmpty && (first || [6, 7, 8, 9].contains(t))) flush();
    if (t == 7) {
      sps = nalu;
      metadata.add(nalu);
    } else if (t == 8) {
      pps = nalu;
      metadata.add(nalu);
    } else if (vcl) {
      if (t == 5) key = true;
      current.add(nalu);
    } else {
      metadata.add(nalu);
    }
  }
  flush();
  if (frames.isEmpty) throw const FormatException('未找到 H.264 Annex-B 视频帧');
  return frames;
}

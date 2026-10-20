import 'dart:typed_data';

String hex(Iterable<int> bytes) => bytes
    .map((b) => b.toRadixString(16).padLeft(2, '0').toUpperCase())
    .join(' ');
Uint8List unhex(String value) {
  final text = value.replaceAll(RegExp(r'\s+'), '');
  if (text.length.isOdd || !RegExp(r'^[0-9a-fA-F]*$').hasMatch(text)) {
    throw const FormatException('请输入完整的十六进制字节');
  }
  return Uint8List.fromList([
    for (var i = 0; i < text.length; i += 2)
      int.parse(text.substring(i, i + 2), radix: 16),
  ]);
}

class ByteWriter {
  final _bytes = BytesBuilder(copy: false);
  void u8(int value) {
    if (value < 0 || value > 255) throw RangeError.range(value, 0, 255);
    _bytes.addByte(value);
  }

  void u16(int value) => _number(value, 2);
  void u32(int value) => _number(value, 4);
  void u64(int value) => _number(value, 8);
  void _number(int value, int width) {
    if (value < 0 || (width < 8 && value >= 1 << (width * 8))) {
      throw RangeError('数值超出 $width 字节范围');
    }
    for (var shift = (width - 1) * 8; shift >= 0; shift -= 8) {
      u8((value >> shift) & 255);
    }
  }

  void add(List<int> value) => _bytes.add(value);
  void bcd(String value, int width) {
    if (!RegExp(r'^\d+$').hasMatch(value) || value.length > width * 2) {
      throw const FormatException('BCD 字段必须是有效数字且不能超长');
    }
    final padded = value.padLeft(width * 2, '0');
    for (var i = 0; i < padded.length; i += 2) {
      u8(int.parse(padded.substring(i, i + 2), radix: 16));
    }
  }

  Uint8List take() => _bytes.takeBytes();
}

class ByteReader {
  ByteReader(List<int> bytes) : bytes = Uint8List.fromList(bytes);
  final Uint8List bytes;
  int offset = 0;
  int get remaining => bytes.length - offset;
  int u8() => read(1).first;
  int u16() => _number(2);
  int u32() => _number(4);
  int u64() => _number(8);
  int _number(int width) {
    var value = 0;
    for (final b in read(width)) {
      value = (value << 8) | b;
    }
    return value;
  }

  Uint8List read(int length) {
    if (length < 0 || remaining < length) throw const FormatException('报文长度不足');
    final result = Uint8List.sublistView(bytes, offset, offset + length);
    offset += length;
    return result;
  }

  String bcd(int width) {
    final value = read(width);
    if (value.any((b) => b >> 4 > 9 || (b & 15) > 9)) {
      throw const FormatException('无效 BCD');
    }
    return value.map((b) => b.toRadixString(16).padLeft(2, '0')).join();
  }
}

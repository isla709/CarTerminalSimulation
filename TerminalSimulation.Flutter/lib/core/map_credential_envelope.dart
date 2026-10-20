import 'dart:convert';
import 'dart:typed_data';
import 'package:cryptography/cryptography.dart';

/// Build-time wrapping, not a promise of secrecy from an application owner.
/// The map SDK must ultimately receive the plaintext credential at runtime.
abstract final class MapCredentialEnvelope {
  static final _header = utf8.encode('CTS-MAP-KEY/1\n');
  static final _cipher = AesGcm.with256bits();
  static bool isValid(String key) =>
      RegExp(r'^[A-Za-z0-9_-]{16,256}$').hasMatch(key);

  static Future<SecretKey> _wrappingKey() async => SecretKey(
    (await Sha256().hash(
      utf8.encode('CTS.Flutter.TencentMap.Key.Wrap.v1'),
    )).bytes,
  );

  static Future<Uint8List> seal(String key) async {
    if (!isValid(key)) throw const FormatException('地图凭据格式无效');
    final plaintext = Uint8List.fromList(utf8.encode(key));
    try {
      final box = await _cipher.encrypt(
        plaintext,
        secretKey: await _wrappingKey(),
        aad: _header,
      );
      return Uint8List.fromList([..._header, ...box.concatenation()]);
    } finally {
      plaintext.fillRange(0, plaintext.length, 0);
    }
  }

  static Future<String> open(List<int> envelope) async {
    if (envelope.length < _header.length + 12 + 16 + 16 ||
        envelope.length > 1024) {
      throw const FormatException('内置地图凭据格式无效');
    }
    for (var i = 0; i < _header.length; i++) {
      if (envelope[i] != _header[i]) {
        throw const FormatException('内置地图凭据版本无效');
      }
    }
    final box = SecretBox.fromConcatenation(
      envelope.sublist(_header.length),
      nonceLength: 12,
      macLength: 16,
    );
    final plaintext = await _cipher.decrypt(
      box,
      secretKey: await _wrappingKey(),
      aad: _header,
    );
    try {
      final key = utf8.decode(plaintext);
      if (!isValid(key)) throw const FormatException('内置地图凭据无效');
      return key;
    } finally {
      plaintext.fillRange(0, plaintext.length, 0);
    }
  }
}

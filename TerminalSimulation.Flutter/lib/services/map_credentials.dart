import 'package:flutter/services.dart';
import '../core/map_credential_envelope.dart';

/// Loaded once from the builder's encrypted asset; never saved in app settings.
class MapCredentials {
  const MapCredentials.unavailable() : _key = '';
  const MapCredentials._(this._key);
  static const assetPath = 'assets/credentials/map-credential.bin';
  final String _key;
  bool get available => _key.isNotEmpty;
  String get javascriptKey => _key;
  String get serviceKey => _key;

  static Future<MapCredentials> load() async {
    try {
      final data = await rootBundle.load(assetPath);
      return MapCredentials._(
        await MapCredentialEnvelope.open(
          data.buffer.asUint8List(data.offsetInBytes, data.lengthInBytes),
        ),
      );
    } catch (_) {
      // Avoid logging decoded values, ciphertext, URLs or cryptographic errors.
      throw StateError('内置地图凭据不可用，请使用构建脚本重新构建应用。');
    }
  }

  @override
  String toString() =>
      'MapCredentials(${available ? 'embedded' : 'unavailable'})';
}

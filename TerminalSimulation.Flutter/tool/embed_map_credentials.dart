import 'dart:io';
import 'dart:typed_data';
import 'dart:convert';
import 'package:terminal_simulation/core/map_credential_envelope.dart';

/// Reads the local file in-process: the key never enters command-line arguments,
/// generated Dart/JS source, dart-defines, console output or ordinary app settings.
Future<void> main(List<String> arguments) async {
  Uint8List? input;
  File? output;
  try {
    if (arguments.length != 2) {
      throw const FormatException('Expected input and output paths');
    }
    output = File(arguments[1]);
    // Do not permit a previous builder's credential to survive a failed build.
    if (await output.exists()) await output.delete();
    final file = File(arguments[0]);
    if (!await file.exists() || await file.length() > 4096) {
      throw const FormatException('Missing or oversized credential file');
    }
    input = await file.readAsBytes();
    final key = utf8.decode(input).replaceFirst(RegExp(r'^\uFEFF'), '').trim();
    if (!MapCredentialEnvelope.isValid(key)) {
      throw const FormatException('Invalid credential format');
    }
    final sealed = await MapCredentialEnvelope.seal(key);
    // Build invariant: validate authentication/decode before handing off to Flutter.
    if (await MapCredentialEnvelope.open(sealed) != key) {
      throw StateError('Credential wrapping failed');
    }
    await output.parent.create(recursive: true);
    await output.writeAsBytes(sealed, flush: true);
    stdout.writeln('Embedded map credential prepared.');
  } catch (_) {
    if (output != null && await output.exists()) await output.delete();
    stderr.writeln(
      'Map credential generation failed. Supply one valid key in repository docs/KEY.txt. No credential values were logged.',
    );
    exitCode = 1;
  } finally {
    input?.fillRange(0, input.length, 0);
  }
}

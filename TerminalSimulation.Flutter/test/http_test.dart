import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';
import 'package:flutter_test/flutter_test.dart';
import 'package:terminal_simulation/services/http_tool.dart';

void main() {
  test(
    'HTTP raw response bytes, request body and no implicit redirect',
    () async {
      final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0),
          client = HttpTool();
      final requests = <String>[];
      final sub = server.listen((request) async {
        requests.add(await utf8.decoder.bind(request).join());
        request.response.statusCode = 302;
        request.response.headers.set('Location', '/redirect');
        request.response.add([0, 128, 255]);
        await request.response.close();
      });
      try {
        final result = await client.send(
          method: 'POST',
          url: Uri.parse('http://127.0.0.1:${server.port}/'),
          body: utf8.encode('测试'),
        );
        expect(result.status, 302);
        expect(result.bytes, [0, 128, 255]);
        expect(requests, ['测试']);
      } finally {
        client.cancel();
        await sub.cancel();
        await server.close(force: true);
      }
    },
  );
  test('HTTP rejects known oversized responses', () async {
    final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0),
        client = HttpTool();
    final sub = server.listen((request) async {
      request.response.contentLength = 10 * 1024 * 1024 + 1;
      request.response.add(Uint8List(10 * 1024 * 1024 + 1));
      try {
        await request.response.close();
      } catch (_) {}
    });
    try {
      await expectLater(
        client.send(
          method: 'GET',
          url: Uri.parse('http://127.0.0.1:${server.port}/'),
        ),
        throwsFormatException,
      );
    } finally {
      client.cancel();
      await sub.cancel();
      await server.close(force: true);
    }
  });
  test('HTTP cancellation terminates pending request', () async {
    final server = await HttpServer.bind(InternetAddress.loopbackIPv4, 0),
        client = HttpTool(),
        arrived = Completer<void>();
    final sub = server.listen((request) {
      arrived.complete();
    });
    try {
      final request = client.send(
        method: 'GET',
        url: Uri.parse('http://127.0.0.1:${server.port}/'),
      );
      final failure = expectLater(request, throwsA(isA<Exception>()));
      await arrived.future.timeout(const Duration(seconds: 3));
      client.cancel();
      await failure;
    } finally {
      client.cancel();
      await sub.cancel();
      await server.close(force: true);
    }
  });
}

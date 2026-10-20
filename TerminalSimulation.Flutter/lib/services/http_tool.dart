import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

class HttpResult {
  HttpResult(this.status, this.headers, this.bytes, this.elapsed);
  final int status;
  final Map<String, List<String>> headers;
  final Uint8List bytes;
  final Duration elapsed;
  String get text => utf8.decode(bytes, allowMalformed: true);
}

class HttpTool {
  HttpClient? _client;
  int _generation = 0;
  void cancel() {
    ++_generation;
    _client?.close(force: true);
    _client = null;
  }

  Future<HttpResult> send({
    required String method,
    required Uri url,
    Map<String, String> headers = const {},
    List<int> body = const [],
    Duration timeout = const Duration(seconds: 30),
  }) async {
    if (!['http', 'https'].contains(url.scheme) || url.host.isEmpty) {
      throw const FormatException('仅支持 HTTP/HTTPS 地址');
    }
    cancel();
    final generation = _generation,
        client = HttpClient()..connectionTimeout = timeout;
    _client = client;
    final watch = Stopwatch()..start();
    Future<HttpResult> perform() async {
      final request = await client.openUrl(method.toUpperCase(), url);
      request.followRedirects = false;
      headers.forEach(request.headers.set);
      if (body.isNotEmpty) {
        request.contentLength = body.length;
        request.add(body);
      }
      final response = await request.close();
      if (response.contentLength > 10 * 1024 * 1024) {
        throw const FormatException('响应超过 10 MiB');
      }
      final builder = BytesBuilder(copy: false);
      var length = 0;
      await for (final part in response) {
        if (generation != _generation) throw StateError('请求已取消');
        length += part.length;
        if (length > 10 * 1024 * 1024) {
          throw const FormatException('响应超过 10 MiB');
        }
        builder.add(part);
      }
      final responseHeaders = <String, List<String>>{};
      response.headers.forEach((name, values) {
        responseHeaders[name] = values;
      });
      if (generation != _generation) throw StateError('请求已取消');
      return HttpResult(
        response.statusCode,
        responseHeaders,
        builder.takeBytes(),
        watch.elapsed,
      );
    }

    try {
      return await perform().timeout(timeout);
    } finally {
      client.close(force: true);
      if (generation == _generation) _client = null;
    }
  }
}

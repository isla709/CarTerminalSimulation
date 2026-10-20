import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:math';
import 'package:flutter/services.dart';

/// Private loopback asset origin, bound only for the lifetime of one map view.
/// It never forwards TCP, exposes user files, or accepts state-changing requests.
class MapAssets {
  MapAssets({this.javascriptKey = ''});
  final String javascriptKey;
  final String token = List.generate(
    24,
    (_) => Random.secure().nextInt(256).toRadixString(16).padLeft(2, '0'),
  ).join();
  HttpServer? _server;
  final Set<String> _allowed = {};
  bool _closed = false;
  Uri get url =>
      Uri.parse('http://127.0.0.1:${_server!.port}/$token/index.html');
  bool owns(String? value) {
    final uri = Uri.tryParse(value ?? '');
    return _server != null &&
        uri?.scheme == 'http' &&
        uri?.host == '127.0.0.1' &&
        uri?.port == _server!.port &&
        uri?.path == '/$token/index.html';
  }

  Future<Uri> start() async {
    final manifest = await AssetManifest.loadFromAssetBundle(rootBundle);
    final prefix = 'assets/map/';
    _allowed.addAll(
      manifest
          .listAssets()
          .where((v) => v.startsWith(prefix))
          .map((v) => v.substring(prefix.length)),
    );
    if (_closed) throw StateError('地图已关闭');
    final server = await HttpServer.bind(
      InternetAddress.loopbackIPv4,
      0,
      shared: false,
    );
    if (_closed) {
      await server.close(force: true);
      throw StateError('地图已关闭');
    }
    _server = server;
    server.listen((request) {
      unawaited(_serve(request));
    });
    return url;
  }

  Future<void> _serve(HttpRequest request) async {
    try {
      final prefix = '/$token/';
      final path = request.uri.path;
      final origin = '127.0.0.1:${_server?.port}';
      if (!['GET', 'HEAD'].contains(request.method) ||
          request.headers.value('host') != origin ||
          !path.startsWith(prefix) ||
          !_allowed.contains(path.substring(prefix.length))) {
        request.response.statusCode = HttpStatus.notFound;
        return;
      }
      final file = path.substring(prefix.length);
      request.response.headers.set('Cache-Control', 'no-store');
      request.response.headers.set('X-Content-Type-Options', 'nosniff');
      request.response.headers.set('Referrer-Policy', 'no-referrer');
      request.response.headers.set(
        'Content-Security-Policy',
        "default-src 'self' https: data: blob:; script-src 'self' 'unsafe-inline' 'unsafe-eval' https:; style-src 'self' 'unsafe-inline' https:; worker-src 'self' blob: https:; object-src 'none'; frame-src 'none'; base-uri 'none'; frame-ancestors 'none'",
      );
      request.response.headers.contentType = ContentType.parse(switch (file
          .split('.')
          .last) {
        'html' => 'text/html; charset=utf-8',
        'js' => 'application/javascript; charset=utf-8',
        'css' => 'text/css; charset=utf-8',
        'png' => 'image/png',
        'svg' => 'image/svg+xml',
        _ => 'application/octet-stream',
      });
      if (request.method == 'HEAD') return;
      if (file == 'index.html') {
        var html = await rootBundle.loadString('assets/map/$file');
        final config =
            '<script>window.__CTS_TENCENT_MAP_KEY=${jsonEncode(javascriptKey).replaceAll('<', r'\u003c')};'
            'window.__CTS_BRIDGE_TOKEN=${jsonEncode(token)};</script>';
        html = html.replaceFirst(
          '</head>',
          '$config<script src="host.js"></script><link rel="stylesheet" href="host.css"></head>',
        );
        request.response.write(html);
      } else {
        final bytes = await rootBundle.load('assets/map/$file');
        request.response.add(
          bytes.buffer.asUint8List(bytes.offsetInBytes, bytes.lengthInBytes),
        );
      }
    } catch (_) {
      // Do not expose asset paths, user data, or map keys through error pages.
      request.response.statusCode = HttpStatus.internalServerError;
    } finally {
      try {
        await request.response.close();
      } catch (_) {
        /* The view may have closed its socket. */
      }
    }
  }

  Future<void> close() async {
    _closed = true;
    await _server?.close(force: true);
    _server = null;
  }
}

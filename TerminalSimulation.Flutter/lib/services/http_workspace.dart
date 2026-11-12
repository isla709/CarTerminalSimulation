import 'dart:convert';
import 'package:flutter/foundation.dart';
import 'http_tool.dart';
import 'storage.dart';

class HttpPair {
  HttpPair({this.name = '', this.value = '', this.enabled = true});
  String name, value;
  bool enabled;
  Map<String, dynamic> toJson() => {
    'name': name,
    'value': value,
    'enabled': enabled,
  };
  factory HttpPair.fromJson(Map<String, dynamic> j) {
    final name = j['name'], value = j['value'];
    if (name is! String ||
        value is! String ||
        name.length > 8192 ||
        value.length > 65536) {
      throw const FormatException('参数名称或值无效');
    }
    return HttpPair(name: name, value: value, enabled: j['enabled'] != false);
  }
}

class HttpDraft {
  HttpDraft({
    String? id,
    this.name = '新请求',
    this.method = 'GET',
    this.url = 'http://127.0.0.1:8080/',
    this.body = '',
    this.contentType = 'application/json',
    this.timeoutSeconds = 30,
    List<HttpPair>? headers,
    List<HttpPair>? query,
  }) : id = id ?? DateTime.now().microsecondsSinceEpoch.toString(),
       headers = headers ?? [],
       query = query ?? [];
  final String id;
  String name, method, url, body, contentType;
  int timeoutSeconds;
  final List<HttpPair> headers, query;
  Map<String, dynamic> toJson() => {
    'id': id,
    'name': name,
    'method': method,
    'url': url,
    'body': body,
    'contentType': contentType,
    'timeoutSeconds': timeoutSeconds,
    'headers': headers.map((v) => v.toJson()).toList(),
    'query': query.map((v) => v.toJson()).toList(),
  };
  factory HttpDraft.fromJson(Map<String, dynamic> j) {
    List<HttpPair> pairs(String key) {
      final v = j[key];
      if (v is! List || v.length > 256) throw const FormatException('参数列表无效');
      return v
          .map((p) => HttpPair.fromJson(Map<String, dynamic>.from(p as Map)))
          .toList();
    }

    for (final key in ['id', 'name', 'method', 'url', 'body', 'contentType']) {
      if (j[key] is! String) throw const FormatException('请求字段无效');
    }
    if ((j['body'] as String).length > 262144 ||
        (j['url'] as String).length > 8192 ||
        (j['name'] as String).length > 120 ||
        !methods.contains(j['method'])) {
      throw const FormatException('请求超长或方法无效');
    }
    final timeout = j['timeoutSeconds'];
    if (timeout is! int || timeout < 1 || timeout > 300) {
      throw const FormatException('超时应为 1–300 秒');
    }
    return HttpDraft(
      id: j['id'],
      name: j['name'],
      method: j['method'],
      url: j['url'],
      body: j['body'],
      contentType: j['contentType'],
      timeoutSeconds: timeout,
      headers: pairs('headers'),
      query: pairs('query'),
    );
  }
  static const methods = [
    'GET',
    'POST',
    'PUT',
    'PATCH',
    'DELETE',
    'HEAD',
    'OPTIONS',
  ];
  Uri requestUri() {
    final uri = Uri.parse(url.trim()),
        additions = query.where((p) => p.enabled && p.name.trim().isNotEmpty);
    if (additions.isEmpty) return uri;
    final suffix = additions
        .map(
          (p) =>
              '${Uri.encodeQueryComponent(p.name)}=${Uri.encodeQueryComponent(p.value)}',
        )
        .join('&');
    return uri.replace(
      query: [
        if (uri.hasQuery && uri.query.isNotEmpty) uri.query,
        suffix,
      ].join('&'),
    );
  }
}

class HttpWorkspace {
  HttpWorkspace(this.storage, {this.onEvent});
  final void Function(String)? onEvent;
  final AppStorage? storage;
  List<HttpDraft> requests = [HttpDraft()];
  int selected = 0;
  HttpDraft get active => requests[selected];
  HttpResult? response;
  String responseText = '',
      responseRequestId = '',
      responseUrl = '',
      responseMethod = '';
  Future<void>? _loading;
  Future<void> load() => _loading ??= _load();
  Future<void> _load() async {
    final data = await storage?.read('http-workspace.json');
    if (data == null) return;
    requests = await compute(parseHttpWorkspace, jsonEncode(data));
    selected = 0;
  }

  Map<String, dynamic> toJson() => {
    'format': 'CTS-HTTP/1',
    'requests': requests.map((r) => r.toJson()).toList(),
  };
  Future<void> save() async {
    if (storage == null) throw StateError('应用存储不可用');
    final data = toJson();
    if (utf8.encode(jsonEncode(data)).length > 4 * 1024 * 1024) {
      throw const FormatException('工作空间超过 4 MiB');
    }
    await storage!.write('http-workspace.json', data);
  }
}

List<HttpDraft> parseHttpWorkspace(String text) {
  if (text.length > 4 * 1024 * 1024) {
    throw const FormatException('工作空间超过 4 MiB');
  }
  final data = jsonDecode(text);
  if (data is! Map ||
      data['format'] != 'CTS-HTTP/1' ||
      data['requests'] is! List) {
    throw const FormatException('不支持的 HTTP 工作空间格式');
  }
  final items = data['requests'] as List;
  if (items.isEmpty || items.length > 100) {
    throw const FormatException('工作空间应包含 1–100 个请求');
  }
  final result = items
      .map((e) => HttpDraft.fromJson(Map<String, dynamic>.from(e as Map)))
      .toList();
  if (result.map((r) => r.id).toSet().length != result.length) {
    throw const FormatException('请求标识重复');
  }
  return result;
}

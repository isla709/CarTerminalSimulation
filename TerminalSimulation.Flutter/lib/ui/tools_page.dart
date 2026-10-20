import 'dart:convert';
import 'package:fast_gbk/fast_gbk.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import '../core/bytes.dart';
import '../core/jt808.dart';
import '../services/app_model.dart';
import '../services/http_tool.dart';
import 'components.dart';
import 'video_page.dart';

class ToolsPage extends StatefulWidget {
  const ToolsPage({super.key, required this.model});
  final AppModel model;
  @override
  State<ToolsPage> createState() => _ToolsPageState();
}

class _ToolsPageState extends State<ToolsPage> {
  final analyzer = TextEditingController(),
      url = TextEditingController(text: 'http://127.0.0.1:8080/'),
      requestBody = TextEditingController(),
      headers = TextEditingController(),
      passthrough = TextEditingController();
  final http = HttpTool();
  String analysis = '',
      response = '',
      error = '',
      method = 'GET',
      encoding = 'UTF-8';
  int type = 0;
  bool sending = false;
  @override
  void dispose() {
    http.cancel();
    for (final c in [analyzer, url, requestBody, headers, passthrough]) {
      c.dispose();
    }
    super.dispose();
  }

  void act(void Function() action) {
    try {
      action();
      setState(() => error = '');
    } catch (e) {
      setState(() => error = '$e');
    }
  }

  @override
  Widget build(BuildContext context) => PageBody(
    title: '协议与调试',
    description: '报文分析、HTTP 请求及终端数据透传。',
    children: [
      if (error.isNotEmpty)
        Text(
          error,
          style: TextStyle(color: Theme.of(context).colorScheme.error),
        ),
      Section(
        '音视频',
        child: Align(
          alignment: Alignment.centerLeft,
          child: FilledButton.icon(
            onPressed: () => Navigator.of(context).push(
              MaterialPageRoute<void>(
                builder: (_) => VideoPage(model: widget.model),
              ),
            ),
            icon: const Icon(Icons.video_camera_back_outlined),
            label: const Text('JT1078 通道工作台'),
          ),
        ),
      ),
      Section(
        'JT808 报文分析',
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            TextField(
              controller: analyzer,
              minLines: 3,
              maxLines: 8,
              decoration: const InputDecoration(labelText: '包含 7E 边界的 HEX 报文'),
            ),
            const SizedBox(height: 12),
            FilledButton.icon(
              onPressed: () => act(() {
                final p = Jt808Codec.decode(unhex(analyzer.text));
                final result = p.toJson();
                if (p.id == 0x0200 && p.encryption == 0 && !p.fragmented) {
                  final location = LocationReport.decode(p.body);
                  result['位置'] = {
                    '纬度': location.latitude,
                    '经度': location.longitude,
                    '速度': location.speed,
                    '方向': location.direction,
                    '报警': location.alarm,
                    '状态': location.status,
                    '时间': location.time.toString(),
                    '附件': location.attachments.map(
                      (k, v) => MapEntry(k.toRadixString(16), hex(v)),
                    ),
                  };
                }
                analysis = const JsonEncoder.withIndent('  ').convert(result);
              }),
              icon: const Icon(Icons.manage_search),
              label: const Text('解析报文'),
            ),
            if (analysis.isNotEmpty) ...[
              const SizedBox(height: 16),
              SelectableText(
                analysis,
                style: const TextStyle(fontFamily: 'monospace', fontSize: 12),
              ),
              TextButton.icon(
                onPressed: () =>
                    Clipboard.setData(ClipboardData(text: analysis)),
                icon: const Icon(Icons.copy),
                label: const Text('复制结果'),
              ),
            ],
          ],
        ),
      ),
      Section(
        'HTTP 调试',
        subtitle: '保留原始响应字节，响应上限 10 MiB；HTTPS 使用系统证书验证。',
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            FieldGrid(
              children: [
                DropdownButtonFormField<String>(
                  isExpanded: true,
                  initialValue: method,
                  decoration: const InputDecoration(labelText: '方法'),
                  items:
                      [
                            'GET',
                            'POST',
                            'PUT',
                            'PATCH',
                            'DELETE',
                            'HEAD',
                            'OPTIONS',
                          ]
                          .map(
                            (v) => DropdownMenuItem(value: v, child: Text(v)),
                          )
                          .toList(),
                  onChanged: (v) => method = v!,
                ),
                TextField(
                  controller: url,
                  decoration: const InputDecoration(labelText: '请求地址'),
                ),
              ],
            ),
            const SizedBox(height: 16),
            TextField(
              controller: headers,
              minLines: 2,
              maxLines: 6,
              decoration: const InputDecoration(
                labelText: '请求头：每行 Name: Value',
              ),
            ),
            const SizedBox(height: 16),
            TextField(
              controller: requestBody,
              minLines: 3,
              maxLines: 10,
              decoration: const InputDecoration(labelText: '请求体（UTF-8）'),
            ),
            const SizedBox(height: 16),
            Wrap(
              spacing: 10,
              runSpacing: 10,
              children: [
                FilledButton.icon(
                  onPressed: sending ? null : _sendHttp,
                  icon: const Icon(Icons.send_outlined),
                  label: Text(sending ? '请求中…' : '发送请求'),
                ),
                OutlinedButton(
                  onPressed: sending ? http.cancel : null,
                  child: const Text('取消'),
                ),
                OutlinedButton(
                  onPressed: () => act(() {
                    requestBody.text = const JsonEncoder.withIndent(
                      '  ',
                    ).convert(jsonDecode(requestBody.text));
                  }),
                  child: const Text('格式化 JSON'),
                ),
              ],
            ),
            if (response.isNotEmpty) ...[
              const SizedBox(height: 16),
              SelectableText(
                response,
                style: const TextStyle(fontFamily: 'monospace', fontSize: 12),
              ),
            ],
          ],
        ),
      ),
      Section(
        '数据透传',
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            FieldGrid(
              children: [
                DropdownButtonFormField<String>(
                  initialValue: encoding,
                  isExpanded: true,
                  decoration: const InputDecoration(labelText: '编码'),
                  items: ['UTF-8', 'GBK', 'HEX']
                      .map((v) => DropdownMenuItem(value: v, child: Text(v)))
                      .toList(),
                  onChanged: (v) => encoding = v!,
                ),
                EditField(
                  '透传类型（0–255）',
                  value: '$type',
                  numeric: true,
                  onChanged: (v) => type = int.tryParse(v) ?? -1,
                ),
              ],
            ),
            const SizedBox(height: 16),
            TextField(
              controller: passthrough,
              minLines: 2,
              maxLines: 8,
              decoration: const InputDecoration(labelText: '发送内容'),
            ),
            const SizedBox(height: 16),
            FilledButton(
              onPressed: () => widget.model.guard(() async {
                final bytes = switch (encoding) {
                  'HEX' => unhex(passthrough.text),
                  'GBK' => gbk.encode(passthrough.text),
                  _ => utf8.encode(passthrough.text),
                };
                await widget.model.controller.passthrough(type, bytes);
              }),
              child: const Text('发送 0x0900'),
            ),
            if (widget.model.downlinks.isNotEmpty) ...[
              const SizedBox(height: 16),
              Text('最近文本下发：${widget.model.downlinks.first}'),
            ],
          ],
        ),
      ),
    ],
  );
  Future<void> _sendHttp() async {
    setState(() {
      sending = true;
      error = '';
    });
    try {
      final parsed = <String, String>{};
      for (final line
          in headers.text.split('\n').where((v) => v.trim().isNotEmpty)) {
        final i = line.indexOf(':');
        if (i < 1) throw const FormatException('请求头格式无效');
        parsed[line.substring(0, i).trim()] = line.substring(i + 1).trim();
      }
      final result = await http.send(
        method: method,
        url: Uri.parse(url.text.trim()),
        headers: parsed,
        body: utf8.encode(requestBody.text),
      );
      if (mounted) {
        setState(
          () => response =
              'HTTP ${result.status} · ${result.elapsed.inMilliseconds} ms · ${result.bytes.length} B\n${result.headers.entries.map((e) => '${e.key}: ${e.value.join(', ')}').join('\n')}\n\n${result.text}',
        );
      }
    } catch (e) {
      if (mounted) setState(() => error = '$e');
    } finally {
      if (mounted) setState(() => sending = false);
    }
  }
}

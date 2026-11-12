import 'dart:async';
import 'dart:convert';
import 'package:fast_gbk/fast_gbk.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import '../core/bytes.dart';
import '../services/document_files.dart';
import '../services/http_tool.dart';
import '../services/http_workspace.dart';
import 'components.dart';
import 'workbench.dart';

String decodeHttpResponse(
  ({Uint8List bytes, String encoding, bool pretty}) value,
) {
  final text = value.encoding == 'GBK'
      ? gbk.decode(value.bytes, allowMalformed: true)
      : utf8.decode(value.bytes, allowMalformed: true);
  if (!value.pretty) return text;
  if (value.bytes.length > 4 * 1024 * 1024) {
    throw const FormatException('JSON 格式化上限为 4 MiB，请保存原始响应');
  }
  return const JsonEncoder.withIndent('  ').convert(jsonDecode(text));
}

class HttpWorkbenchPage extends StatefulWidget {
  const HttpWorkbenchPage({super.key, required this.workspace});
  final HttpWorkspace workspace;
  @override
  State<HttpWorkbenchPage> createState() => _HttpWorkbenchPageState();
}

class _HttpWorkbenchPageState extends State<HttpWorkbenchPage> {
  final scaffold = GlobalKey<ScaffoldState>();
  final http = HttpTool();
  late final url = TextEditingController(text: w.active.url),
      name = TextEditingController(text: w.active.name),
      body = TextEditingController(text: w.active.body);
  HttpWorkspace get w => widget.workspace;
  String error = '', encoding = 'UTF-8';
  bool busy = false, loaded = false, showResponse = false;
  bool? requestSettingsExpanded;
  int epoch = 0, decodeEpoch = 0;
  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    requestSettingsExpanded ??= MediaQuery.sizeOf(context).width >= 720;
  }

  @override
  void initState() {
    super.initState();
    unawaited(load());
  }

  Future<void> load() async {
    try {
      await w.load();
      if (mounted) {
        sync();
        setState(() => loaded = true);
      }
    } catch (_) {
      if (mounted) {
        setState(() {
          loaded = true;
          error = '读取工作空间失败，可导入或新建请求后重新保存。';
        });
      }
    }
  }

  void sync() {
    url.text = w.active.url;
    name.text = w.active.name;
    body.text = w.active.body;
  }

  void cancel() {
    epoch++;
    http.cancel();
    if (mounted) setState(() => busy = false);
  }

  @override
  void dispose() {
    epoch++;
    decodeEpoch++;
    http.cancel();
    for (final c in [url, name, body]) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> act(Future<void> Function() work) async {
    try {
      await work();
      if (mounted) setState(() => error = '');
    } catch (e) {
      if (mounted) {
        setState(() => error = e is FormatException ? e.message : '操作失败：$e');
      }
    }
  }

  void select(int index) {
    if (busy) return;
    setState(() {
      w.selected = index;
      error = '';
      sync();
    });
    if (scaffold.currentState?.isEndDrawerOpen == true) {
      Navigator.of(context).pop();
    }
  }

  void add({bool duplicate = false}) {
    if (w.requests.length >= 100) {
      setState(() => error = '最多保存 100 个请求');
      return;
    }
    final draft = duplicate
        ? HttpDraft.fromJson({
            ...w.active.toJson(),
            'id': DateTime.now().microsecondsSinceEpoch.toString(),
            'name': '${w.active.name} 副本'.substring(
              0,
              ('${w.active.name} 副本').length.clamp(0, 120),
            ),
          })
        : HttpDraft();
    setState(() {
      w.requests.add(draft);
      w.selected = w.requests.length - 1;
      sync();
    });
  }

  Future<void> remove() async {
    if (!await confirmAction(
      context,
      title: '删除请求',
      message: '删除“${w.active.name}”？保存工作空间后此改动才会写入磁盘。',
      nativeMaterial: false,
    )) {
      return;
    }
    if (!mounted) return;
    setState(() {
      w.requests.removeAt(w.selected);
      if (w.requests.isEmpty) w.requests.add(HttpDraft());
      w.selected = w.selected.clamp(0, w.requests.length - 1);
      sync();
    });
  }

  Future<void> importWorkspace() async {
    final bytes = await DocumentFiles.open();
    if (bytes == null || !mounted) return;
    final values = await compute(parseHttpWorkspace, utf8.decode(bytes));
    if (!mounted) return;
    if (!await confirmAction(
      context,
      title: '导入工作空间',
      message: '使用文件中的 ${values.length} 个请求替换当前会话草稿？',
      nativeMaterial: false,
    )) {
      return;
    }
    if (mounted) {
      setState(() {
        w.requests = values;
        w.selected = 0;
        sync();
      });
    }
  }

  Future<void> send() async {
    if (busy || !loaded) return;
    final id = ++epoch;
    setState(() {
      busy = true;
      error = '';
    });
    try {
      final draft = HttpDraft.fromJson(w.active.toJson());
      final headers = <String, String>{};
      for (final p in draft.headers.where((p) => p.enabled)) {
        if (p.name.trim().isEmpty) continue;
        if (!RegExp(r"^[!#$%&'*+.^_`|~0-9a-zA-Z-]+$").hasMatch(p.name) ||
            p.value.contains(RegExp(r'[\r\n]'))) {
          throw const FormatException('请求头名称或值无效');
        }
        headers[p.name] = p.value;
      }
      if (draft.body.isNotEmpty &&
          !headers.keys.any((k) => k.toLowerCase() == 'content-type')) {
        headers['Content-Type'] = '${draft.contentType}; charset=utf-8';
      }
      final uri = draft.requestUri();
      final response = await http.send(
        method: draft.method,
        url: uri,
        headers: headers,
        body: utf8.encode(draft.body),
        timeout: Duration(seconds: draft.timeoutSeconds),
      );
      final text = await compute(decodeHttpResponse, (
        bytes: response.bytes,
        encoding: encoding,
        pretty: false,
      ));
      if (!mounted || id != epoch) return;
      w.onEvent?.call(
        '${draft.method} · HTTP ${response.status} · ${response.bytes.length} B · ${response.elapsed.inMilliseconds} ms',
      );
      setState(() {
        w.response = response;
        w.responseText = text;
        w.responseRequestId = draft.id;
        w.responseUrl = uri.toString();
        w.responseMethod = draft.method;
        showResponse = true;
      });
    } catch (e) {
      if (mounted && id == epoch) {
        setState(() => error = e is FormatException ? e.message : '请求失败：$e');
      }
    } finally {
      if (mounted && id == epoch) setState(() => busy = false);
    }
  }

  Future<void> decode({bool pretty = false}) async {
    final response = w.response;
    if (response == null) return;
    final id = ++decodeEpoch, selectedEncoding = encoding;
    final text = await compute(decodeHttpResponse, (
      bytes: response.bytes,
      encoding: selectedEncoding,
      pretty: pretty,
    ));
    if (mounted && id == decodeEpoch && identical(response, w.response)) {
      setState(() => w.responseText = text);
    }
  }

  Widget workspaceList() => Column(
    children: [
      Padding(
        padding: const EdgeInsets.all(12),
        child: Text(
          'HTTP 工作空间',
          style: Theme.of(context).textTheme.titleMedium,
        ),
      ),
      Wrap(
        spacing: 4,
        children: [
          IconButton(
            tooltip: '新建请求',
            onPressed: busy ? null : add,
            icon: const Icon(Icons.add),
          ),
          IconButton(
            tooltip: '复制请求',
            onPressed: busy ? null : () => add(duplicate: true),
            icon: const Icon(Icons.copy),
          ),
          IconButton(
            tooltip: '删除请求',
            onPressed: busy ? null : remove,
            icon: const Icon(Icons.delete_outline),
          ),
        ],
      ),
      Expanded(
        child: ListView.builder(
          primary: false,
          itemCount: w.requests.length,
          itemBuilder: (context, i) => ListTile(
            selected: i == w.selected,
            title: Text(
              w.requests[i].name,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
            ),
            subtitle: Text(
              '${w.requests[i].method} ${w.requests[i].url}',
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
            ),
            onTap: busy ? null : () => select(i),
          ),
        ),
      ),
      Padding(
        padding: const EdgeInsets.all(12),
        child: Wrap(
          spacing: 4,
          runSpacing: 4,
          children: [
            TextButton(
              onPressed: loaded ? () => act(w.save) : null,
              child: const Text('保存'),
            ),
            TextButton(
              onPressed: busy ? null : () => act(importWorkspace),
              child: const Text('导入'),
            ),
            TextButton(
              onPressed: () => act(() async {
                await DocumentFiles.save(
                  Uint8List.fromList(
                    utf8.encode(
                      const JsonEncoder.withIndent('  ').convert(w.toJson()),
                    ),
                  ),
                  'http-workspace.json',
                );
              }),
              child: const Text('导出'),
            ),
          ],
        ),
      ),
    ],
  );
  Widget pairs(List<HttpPair> values) {
    void add() => setState(() => values.add(HttpPair()));
    return Column(
      children: [
        if (values.isNotEmpty)
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
            child: Row(
              children: [
                Expanded(
                  child: Text(
                    '${values.length} 项',
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                ),
                TextButton.icon(
                  onPressed: values.length >= 256 ? null : add,
                  icon: const Icon(Icons.add, size: 18),
                  label: const Text('添加'),
                ),
              ],
            ),
          ),
        Expanded(
          child: values.isEmpty
              ? WorkbenchEmptyState(
                  icon: Icons.playlist_add,
                  title: '尚未添加',
                  message: '按需添加名称和值，可单独启用或停用。',
                  action: OutlinedButton.icon(
                    onPressed: add,
                    icon: const Icon(Icons.add),
                    label: const Text('添加一项'),
                  ),
                )
              : ListView.builder(
                  primary: false,
                  itemCount: values.length,
                  findChildIndexCallback: (key) {
                    if (key is! ObjectKey) return null;
                    final index = values.indexWhere(
                      (pair) => identical(pair, key.value),
                    );
                    return index < 0 ? null : index;
                  },
                  itemBuilder: (context, i) {
                    final p = values[i];
                    return Padding(
                      key: ObjectKey(p),
                      padding: const EdgeInsets.fromLTRB(12, 4, 12, 12),
                      child: DecoratedBox(
                        decoration: BoxDecoration(
                          color: Theme.of(
                            context,
                          ).colorScheme.surfaceContainerLow,
                          borderRadius: BorderRadius.circular(14),
                        ),
                        child: Padding(
                          padding: const EdgeInsets.fromLTRB(12, 0, 12, 12),
                          child: Column(
                            children: [
                              Row(
                                children: [
                                  Checkbox(
                                    value: p.enabled,
                                    onChanged: (v) =>
                                        setState(() => p.enabled = v!),
                                  ),
                                  Expanded(
                                    child: Text(
                                      '第 ${i + 1} 项 · ${p.enabled ? '已启用' : '已停用'}',
                                      style: Theme.of(
                                        context,
                                      ).textTheme.bodySmall,
                                    ),
                                  ),
                                  IconButton(
                                    tooltip: '移除此项',
                                    onPressed: () =>
                                        setState(() => values.removeAt(i)),
                                    icon: const Icon(Icons.close, size: 18),
                                  ),
                                ],
                              ),
                              FieldGrid(
                                minWidth: 200,
                                children: [
                                  AppTextField(
                                    key: const ValueKey('pair-name'),
                                    initialValue: p.name,
                                    maxLength: 8192,
                                    decoration: const InputDecoration(
                                      labelText: '名称',
                                      counterText: '',
                                      isDense: true,
                                    ),
                                    onChanged: (v) => p.name = v,
                                  ),
                                  AppTextField(
                                    key: const ValueKey('pair-value'),
                                    initialValue: p.value,
                                    maxLength: 65536,
                                    decoration: const InputDecoration(
                                      labelText: '值',
                                      counterText: '',
                                      isDense: true,
                                    ),
                                    onChanged: (v) => p.value = v,
                                  ),
                                ],
                              ),
                            ],
                          ),
                        ),
                      ),
                    );
                  },
                ),
        ),
      ],
    );
  }

  Widget requestHeader(bool wide, bool compact) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      AppTextField(
        key: const ValueKey('http-url'),
        controller: url,
        enabled: loaded && !busy,
        maxLength: 8192,
        keyboardType: TextInputType.url,
        textInputAction: TextInputAction.go,
        autocorrect: false,
        enableSuggestions: false,
        onChanged: (v) => w.active.url = v,
        onSubmitted: (_) => send(),
        decoration: const InputDecoration(
          labelText: '请求 URL',
          hintText: 'https://example.com/api',
          counterText: '',
        ),
      ),
      const SizedBox(height: 16),
      LayoutBuilder(
        builder: (context, row) {
          final font = MediaQuery.textScalerOf(context).scale(16);
          final methodWidth = (font * w.active.method.length * .7 + 64).clamp(
            112.0,
            row.maxWidth,
          );
          final sendMinimum =
              (font * 4 + 80).clamp(144.0, double.infinity) + (busy ? 56 : 0);
          final two = row.maxWidth >= methodWidth + sendMinimum + 12;
          final buttonWidth = !two
              ? row.maxWidth
              : compact
              ? row.maxWidth - methodWidth - 12
              : (row.maxWidth - methodWidth - 12).clamp(120.0, 220.0);
          final height = (font * 1.6 + 32).clamp(56.0, double.infinity);
          return Wrap(
            spacing: 12,
            runSpacing: 16,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              SizedBox(
                key: const ValueKey('http-method-slot'),
                width: two ? methodWidth : row.maxWidth,
                height: height,
                child: DropdownButtonFormField<String>(
                  initialValue: w.active.method,
                  key: ValueKey('${w.active.id}-${w.active.method}'),
                  isExpanded: true,
                  decoration: const InputDecoration(
                    labelText: '方法',
                    isDense: true,
                  ),
                  items: [
                    for (final method in HttpDraft.methods)
                      DropdownMenuItem(value: method, child: Text(method)),
                  ],
                  onChanged: busy
                      ? null
                      : (value) => setState(() => w.active.method = value!),
                ),
              ),
              SizedBox(
                key: const ValueKey('http-send-slot'),
                width: buttonWidth,
                height: height,
                child: Row(
                  children: [
                    Expanded(
                      child: FilledButton.icon(
                        style: FilledButton.styleFrom(
                          minimumSize: Size(double.infinity, height),
                        ),
                        onPressed: loaded && !busy ? send : null,
                        icon: const Icon(Icons.send_outlined, size: 20),
                        label: Text(busy ? '请求中…' : '发送请求'),
                      ),
                    ),
                    if (busy)
                      Padding(
                        padding: const EdgeInsets.only(left: 8),
                        child: IconButton.filledTonal(
                          tooltip: '取消请求',
                          onPressed: cancel,
                          icon: const Icon(Icons.close),
                        ),
                      ),
                  ],
                ),
              ),
            ],
          );
        },
      ),
      const SizedBox(height: 4),
      ExpansionTile(
        key: const PageStorageKey('http-request-settings'),
        maintainState: true,
        initiallyExpanded: requestSettingsExpanded!,
        minTileHeight: 40,
        tilePadding: EdgeInsets.zero,
        title: Row(
          children: [
            const Expanded(child: Text('请求设置')),
            Text(
              '${w.active.timeoutSeconds} 秒',
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ],
        ),
        onExpansionChanged: (expanded) {
          if (!expanded) FocusManager.instance.primaryFocus?.unfocus();
          setState(() => requestSettingsExpanded = expanded);
        },
        children: [
          Padding(
            padding: const EdgeInsets.only(top: 8, bottom: 16),
            child: LayoutBuilder(
              builder: (context, settings) {
                final scale = MediaQuery.textScalerOf(context).scale(14) / 14;
                final two = settings.maxWidth >= 360 * scale;
                final timeoutWidth = two ? 120 * scale : settings.maxWidth;
                return Wrap(
                  spacing: 12,
                  runSpacing: 16,
                  children: [
                    SizedBox(
                      key: const ValueKey('http-name-slot'),
                      width: two
                          ? settings.maxWidth - timeoutWidth - 12
                          : settings.maxWidth,
                      child: AppTextField(
                        controller: name,
                        enabled: loaded && !busy,
                        maxLength: 120,
                        onChanged: (v) => w.active.name = v,
                        decoration: const InputDecoration(
                          labelText: '请求名称',
                          counterText: '',
                          isDense: true,
                        ),
                      ),
                    ),
                    SizedBox(
                      key: const ValueKey('http-timeout-slot'),
                      width: timeoutWidth,
                      child: AppTextField(
                        key: ValueKey(w.active.id),
                        initialValue: '${w.active.timeoutSeconds}',
                        enabled: !busy,
                        keyboardType: TextInputType.number,
                        decoration: const InputDecoration(
                          labelText: '超时（秒）',
                          isDense: true,
                        ),
                        onChanged: (v) =>
                            w.active.timeoutSeconds = int.tryParse(v) ?? 0,
                      ),
                    ),
                  ],
                );
              },
            ),
          ),
        ],
      ),
      Offstage(
        offstage: wide,
        child: Padding(
          padding: const EdgeInsets.only(top: 4, bottom: 4),
          child: SegmentedButton<bool>(
            showSelectedIcon: false,
            expandedInsets: EdgeInsets.zero,
            segments: const [
              ButtonSegment(value: false, label: Text('请求')),
              ButtonSegment(value: true, label: Text('响应')),
            ],
            selected: {showResponse},
            onSelectionChanged: (value) {
              FocusManager.instance.primaryFocus?.unfocus();
              setState(() => showResponse = value.first);
            },
          ),
        ),
      ),
      if (error.isNotEmpty)
        Padding(
          padding: const EdgeInsets.only(top: 8),
          child: SelectableText(
            error,
            style: TextStyle(color: Theme.of(context).colorScheme.error),
          ),
        ),
    ],
  );
  Widget requestPane() => DefaultTabController(
    length: 3,
    child: Card(
      margin: EdgeInsets.zero,
      child: Column(
        children: [
          WorkbenchTabs(
            onTap: (_) => FocusManager.instance.primaryFocus?.unfocus(),
            labels: const ['查询参数', '请求头', '正文'],
            compactLabels: const ['参数', '请求头', '正文'],
          ),
          Expanded(
            child: TabBarView(
              children: [
                pairs(w.active.query),
                pairs(w.active.headers),
                Column(
                  children: [
                    Padding(
                      padding: const EdgeInsets.all(8),
                      child: Row(
                        children: [
                          Expanded(
                            child: DropdownButtonFormField<String>(
                              key: ValueKey(w.active.contentType),
                              isExpanded: true,
                              decoration: const InputDecoration(
                                labelText: 'Content-Type',
                                isDense: true,
                              ),
                              initialValue:
                                  [
                                    'application/json',
                                    'text/plain',
                                    'application/xml',
                                    'application/x-www-form-urlencoded',
                                  ].contains(w.active.contentType)
                                  ? w.active.contentType
                                  : null,
                              hint: Text(w.active.contentType),
                              items:
                                  [
                                        'application/json',
                                        'text/plain',
                                        'application/xml',
                                        'application/x-www-form-urlencoded',
                                      ]
                                      .map(
                                        (v) => DropdownMenuItem(
                                          value: v,
                                          child: Text(
                                            v,
                                            overflow: TextOverflow.ellipsis,
                                          ),
                                        ),
                                      )
                                      .toList(),
                              onChanged: (v) =>
                                  setState(() => w.active.contentType = v!),
                            ),
                          ),
                          IconButton(
                            tooltip: '格式化 JSON',
                            onPressed: () => act(() async {
                              final value = await compute(
                                formatHttpJson,
                                body.text,
                              );
                              if (mounted) {
                                body.text = value;
                                w.active.body = value;
                              }
                            }),
                            icon: const Icon(Icons.data_object),
                          ),
                        ],
                      ),
                    ),
                    Expanded(
                      child: Padding(
                        padding: const EdgeInsets.fromLTRB(12, 4, 12, 12),
                        child: AppTextField(
                          controller: body,
                          textAlignVertical: TextAlignVertical.top,
                          enabled: loaded && !busy,
                          maxLength: 262144,
                          maxLines: null,
                          minLines: null,
                          expands: true,
                          autocorrect: false,
                          enableSuggestions: false,
                          onChanged: (v) => w.active.body = v,
                          style: const TextStyle(
                            fontFamily: 'Consolas',
                            fontSize: 13,
                          ),
                          decoration: const InputDecoration(
                            labelText: '请求正文（UTF-8）',
                            alignLabelWithHint: true,
                            counterText: '',
                          ),
                        ),
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ],
      ),
    ),
  );
  Widget responsePane() {
    final r = w.response,
        valid = r != null && w.responseRequestId == w.active.id;
    return DefaultTabController(
      length: 3,
      child: Card(
        margin: EdgeInsets.zero,
        child: Column(
          children: [
            const WorkbenchTabs(
              labels: ['响应正文', '响应头', '原始 HEX'],
              compactLabels: ['正文', '响应头', 'HEX'],
            ),
            Padding(
              padding: const EdgeInsets.all(8),
              child: Wrap(
                spacing: 8,
                crossAxisAlignment: WrapCrossAlignment.center,
                children: [
                  Text(
                    valid
                        ? 'HTTP ${r.status} · ${r.elapsed.inMilliseconds} ms · ${r.bytes.length} B'
                        : '尚未发送当前请求',
                  ),
                  DropdownButton<String>(
                    value: encoding,
                    items: ['UTF-8', 'GBK']
                        .map((v) => DropdownMenuItem(value: v, child: Text(v)))
                        .toList(),
                    onChanged: valid
                        ? (v) {
                            setState(() => encoding = v!);
                            unawaited(act(() => decode()));
                          }
                        : null,
                  ),
                  TextButton(
                    onPressed: valid
                        ? () => act(() => decode(pretty: true))
                        : null,
                    child: const Text('格式化 JSON'),
                  ),
                  IconButton(
                    tooltip: '复制完整响应正文',
                    onPressed: valid
                        ? () => copyWorkbench(context, w.responseText)
                        : null,
                    icon: const Icon(Icons.copy),
                  ),
                  IconButton(
                    tooltip: '保存原始响应',
                    onPressed: valid
                        ? () => act(() async {
                            await DocumentFiles.save(r.bytes, 'response.bin');
                          })
                        : null,
                    icon: const Icon(Icons.save_alt),
                  ),
                ],
              ),
            ),
            Expanded(
              child: TabBarView(
                children: [
                  TextPreview(valid ? w.responseText : ''),
                  TextPreview(
                    valid
                        ? r.headers.entries
                              .map((e) => '${e.key}: ${e.value.join(', ')}')
                              .join('\n')
                        : '',
                  ),
                  TextPreview(
                    valid ? hex(r.bytes.take(16384)) : '',
                    empty: '暂无内容',
                  ),
                ],
              ),
            ),
            if (valid && r.bytes.length > 16384)
              const Padding(
                padding: EdgeInsets.all(4),
                child: Text('HEX 预览最多 16 KiB；保存原始响应可取得全部字节'),
              ),
          ],
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) => CallbackShortcuts(
    bindings: {
      const SingleActivator(LogicalKeyboardKey.enter, control: true): () =>
          unawaited(send()),
    },
    child: Scaffold(
      key: scaffold,
      appBar: AppBar(
        title: const Text('HTTP 调试'),
        actions: [
          IconButton(
            tooltip: '请求工作空间',
            onPressed: () => scaffold.currentState!.openEndDrawer(),
            icon: const Icon(Icons.folder_outlined),
          ),
        ],
      ),
      endDrawer: Drawer(width: 300, child: SafeArea(child: workspaceList())),
      body: SafeArea(
        child: WorkbenchViewport(
          minHeight:
              requestSettingsExpanded == true &&
                  MediaQuery.sizeOf(context).width < 720
              ? 760
              : 560,
          builder: (context, box) {
            final wide = box.maxWidth >= 1000;
            final content = Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Padding(
                  padding: const EdgeInsets.fromLTRB(12, 12, 12, 8),
                  child: requestHeader(wide, box.maxWidth < 720),
                ),
                Expanded(
                  child: Padding(
                    padding: const EdgeInsets.fromLTRB(12, 0, 12, 12),
                    child: LayoutBuilder(
                      builder: (context, panes) {
                        final paneWidth = wide
                            ? (panes.maxWidth - 12) / 2
                            : panes.maxWidth;
                        return Stack(
                          children: [
                            Positioned(
                              left: 0,
                              top: 0,
                              bottom: 0,
                              width: paneWidth,
                              child: Offstage(
                                offstage: !wide && showResponse,
                                child: AbsorbPointer(
                                  absorbing: busy || !loaded,
                                  child: requestPane(),
                                ),
                              ),
                            ),
                            Positioned(
                              right: 0,
                              top: 0,
                              bottom: 0,
                              width: paneWidth,
                              child: Offstage(
                                offstage: !wide && !showResponse,
                                child: responsePane(),
                              ),
                            ),
                          ],
                        );
                      },
                    ),
                  ),
                ),
              ],
            );
            return Row(
              children: [
                Offstage(
                  offstage: box.maxWidth < 1400,
                  child: SizedBox(width: 230, child: workspaceList()),
                ),
                Offstage(
                  offstage: box.maxWidth < 1400,
                  child: const VerticalDivider(width: 1),
                ),
                Expanded(child: content),
              ],
            );
          },
        ),
      ),
    ),
  );
}

String formatHttpJson(String text) =>
    const JsonEncoder.withIndent('  ').convert(jsonDecode(text));

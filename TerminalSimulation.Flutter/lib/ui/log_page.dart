import 'dart:async';
import 'dart:convert';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import '../core/bytes.dart';
import '../core/message_analysis.dart';
import '../services/app_model.dart';
import '../services/document_files.dart';
import 'components.dart';
import 'message_analyzer_page.dart';
import 'workbench.dart';

(MessageAnalysis, String) analyzeLogPacket(String raw) {
  final analysis = analyzeMessage(raw);
  return (analysis, analysis.json);
}

class LogAnalysisCache {
  final _values = <LogEntry, Future<(MessageAnalysis, String)>>{};
  Future<void> _tail = Future.value();
  Future<(MessageAnalysis, String)> get(LogEntry entry) {
    final old = _values[entry];
    if (old != null) return old;
    final value = _tail.then(
      (_) => compute(
        analyzeLogPacket,
        hex(entry.raw!),
        debugLabel: 'Log packet analysis',
      ),
    );
    _tail = value.then<void>((_) {}, onError: (Object _, StackTrace _) {});
    _values[entry] = value;
    while (_values.length > 32) {
      _values.remove(_values.keys.first);
    }
    return value;
  }
}

class LogPage extends StatefulWidget {
  const LogPage({super.key, required this.model});
  final AppModel model;
  @override
  State<LogPage> createState() => _LogPageState();
}

class _LogPageState extends State<LogPage> {
  final search = TextEditingController(),
      scroll = ScrollController(),
      focus = FocusNode();
  final cache = LogAnalysisCache();
  final selection = <LogEntry>{};
  LogEntry? selected;
  List<LogEntry>? paused;
  List<LogEntry> visible = [];
  bool communication = true, system = true, autoScroll = true;
  String source = '全部', error = '';
  int seen = -1;
  @override
  void initState() {
    super.initState();
    search.addListener(refilter);
  }

  void refilter() {
    setState(() => seen = -1);
  }

  @override
  void dispose() {
    search.dispose();
    scroll.dispose();
    focus.dispose();
    super.dispose();
  }

  void select(LogEntry entry) {
    setState(() {
      final keys = HardwareKeyboard.instance;
      if (keys.isShiftPressed &&
          selected != null &&
          visible.contains(selected)) {
        final a = visible.indexOf(selected!), b = visible.indexOf(entry);
        selection.addAll(visible.sublist(a < b ? a : b, (a > b ? a : b) + 1));
      } else if (keys.isControlPressed) {
        if (!selection.add(entry)) selection.remove(entry);
      } else {
        selection
          ..clear()
          ..add(entry);
      }
      selected = entry;
    });
    focus.requestFocus();
  }

  Future<void> clear() async {
    if (!await confirmAction(
      context,
      title: '清空全部日志',
      message: '将清空所有分类的会话日志，包括当前筛选未显示的记录。',
      nativeMaterial: false,
    )) {
      return;
    }
    if (!mounted) return;
    widget.model.clearLogs();
    setState(() {
      paused = null;
      selected = null;
      selection.clear();
      seen = -1;
    });
  }

  Future<void> export() async {
    try {
      final bytes = await compute(encodeLogExport, List<LogEntry>.of(visible));
      await DocumentFiles.save(bytes, 'cts-logs.txt');
    } catch (e) {
      if (mounted) setState(() => error = '导出失败：$e');
    }
  }

  Future<void> analyze(LogEntry entry) => Navigator.of(context).push(
    MaterialPageRoute<void>(
      builder: (_) => MessageAnalyzerPage(initialInput: hex(entry.raw!)),
    ),
  );
  KeyEventResult key(FocusNode node, KeyEvent event) {
    if (event is! KeyDownEvent || !HardwareKeyboard.instance.isControlPressed) {
      return KeyEventResult.ignored;
    }
    if (event.logicalKey == LogicalKeyboardKey.keyA) {
      setState(() => selection.addAll(visible));
      return KeyEventResult.handled;
    }
    if (event.logicalKey == LogicalKeyboardKey.keyC) {
      unawaited(
        copyWorkbench(
          context,
          visible.where(selection.contains).map((e) => e.full).join('\n\n'),
        ),
      );
      return KeyEventResult.handled;
    }
    return KeyEventResult.ignored;
  }

  @override
  Widget build(BuildContext context) => ModelSlice<int>(
    listenable: widget.model,
    select: () => widget.model.logRevision,
    builder: (context, _) => WorkbenchViewport(
      builder: (context, box) {
        final revision = widget.model.logRevision;
        if (seen != revision) {
          final query = search.text.trim().toLowerCase();
          visible = (paused ?? widget.model.logs)
              .where(
                (e) =>
                    (e.communication ? communication : system) &&
                    (source == '全部' || e.category == source) &&
                    (query.isEmpty ||
                        '${e.category} ${e.message}'.toLowerCase().contains(
                          query,
                        )),
              )
              .toList()
              .reversed
              .toList();
          seen = revision;
          if (autoScroll && paused == null) {
            WidgetsBinding.instance.addPostFrameCallback((_) {
              if (mounted && scroll.hasClients) {
                scroll.jumpTo(scroll.position.maxScrollExtent);
              }
            });
          }
        }
        final wide = box.maxWidth >= 1000;
        Widget list() => Focus(
          focusNode: focus,
          onKeyEvent: key,
          child: NotificationListener<UserScrollNotification>(
            onNotification: (n) {
              if (n.direction == ScrollDirection.forward && autoScroll) {
                setState(() => autoScroll = false);
              }
              return false;
            },
            child: Scrollbar(
              controller: scroll,
              child: ListView.builder(
                controller: scroll,
                itemCount: visible.length,
                itemExtent: 100,
                itemBuilder: (context, i) {
                  final e = visible[i], raw = e.raw;
                  return Material(
                    key: ValueKey(e.sequence),
                    color: selection.contains(e)
                        ? Theme.of(context).colorScheme.secondaryContainer
                        : i.isEven
                        ? Theme.of(context).colorScheme.surface
                        : Theme.of(context).colorScheme.surfaceContainerLow,
                    child: InkWell(
                      onTap: () {
                        select(e);
                        if (!wide) {
                          Navigator.of(context).push(
                            MaterialPageRoute<void>(
                              builder: (_) =>
                                  LogEntryPage(entry: e, cache: cache),
                            ),
                          );
                        }
                      },
                      onDoubleTap: raw == null ? null : () => analyze(e),
                      child: Padding(
                        padding: const EdgeInsets.fromLTRB(12, 8, 4, 8),
                        child: Row(
                          children: [
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(
                                    '${e.time.toIso8601String().substring(11, 23)}  [${e.communication ? '通信' : '系统'}]  ${e.category}',
                                    style: TextStyle(
                                      fontFamily: 'Consolas',
                                      fontSize: 12,
                                      color: e.category == '错误'
                                          ? Theme.of(context).colorScheme.error
                                          : Theme.of(
                                              context,
                                            ).colorScheme.primary,
                                    ),
                                  ),
                                  const SizedBox(height: 5),
                                  Text(
                                    e.message,
                                    maxLines: 2,
                                    overflow: TextOverflow.ellipsis,
                                    style: const TextStyle(
                                      fontFamily: 'Consolas',
                                      fontSize: 13,
                                    ),
                                  ),
                                ],
                              ),
                            ),
                            PopupMenuButton<String>(
                              tooltip: '日志操作',
                              onSelected: (v) {
                                if (v == 'raw') {
                                  unawaited(copyWorkbench(context, hex(raw!)));
                                } else if (v == 'analyze') {
                                  unawaited(analyze(e));
                                } else {
                                  unawaited(copyWorkbench(context, e.full));
                                }
                              },
                              itemBuilder: (_) => [
                                const PopupMenuItem(
                                  value: 'full',
                                  child: Text('复制整条日志'),
                                ),
                                if (raw != null) ...[
                                  const PopupMenuItem(
                                    value: 'raw',
                                    child: Text('复制原始 HEX'),
                                  ),
                                  const PopupMenuItem(
                                    value: 'analyze',
                                    child: Text('打开报文分析'),
                                  ),
                                ],
                              ],
                            ),
                            if (raw != null)
                              IconButton(
                                tooltip: '解析报文',
                                onPressed: () => analyze(e),
                                icon: const Icon(Icons.manage_search),
                              ),
                          ],
                        ),
                      ),
                    ),
                  );
                },
              ),
            ),
          ),
        );
        return Padding(
          padding: EdgeInsets.fromLTRB(
            12,
            12 + MediaQuery.paddingOf(context).top,
            12,
            12 + MediaQuery.paddingOf(context).bottom,
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Wrap(
                spacing: 8,
                runSpacing: 8,
                crossAxisAlignment: WrapCrossAlignment.center,
                children: [
                  Text(
                    '通信与系统日志',
                    style: Theme.of(context).textTheme.titleLarge,
                  ),
                  FilterChip(
                    label: const Text('通信'),
                    selected: communication,
                    onSelected: (v) => setState(() {
                      communication = v;
                      seen = -1;
                    }),
                  ),
                  FilterChip(
                    label: const Text('系统'),
                    selected: system,
                    onSelected: (v) => setState(() {
                      system = v;
                      seen = -1;
                    }),
                  ),
                  TextButton(
                    onPressed: () => setState(() {
                      communication = system = true;
                      source = '全部';
                      search.clear();
                      seen = -1;
                    }),
                    child: const Text('全部'),
                  ),
                  DropdownButton<String>(
                    value: source,
                    items:
                        [
                              '全部',
                              '发送',
                              '接收',
                              '错误',
                              '终端',
                              '文本',
                              '透传',
                              '媒体',
                              '地图',
                              'HTTP',
                              '配置',
                              '模拟',
                            ]
                            .map(
                              (v) => DropdownMenuItem(value: v, child: Text(v)),
                            )
                            .toList(),
                    onChanged: (v) => setState(() {
                      source = v!;
                      seen = -1;
                    }),
                  ),
                  FilterChip(
                    label: const Text('自动滚动'),
                    selected: autoScroll,
                    onSelected: (v) => setState(() {
                      autoScroll = v;
                      seen = -1;
                    }),
                  ),
                  OutlinedButton.icon(
                    onPressed: () => setState(() {
                      paused = paused == null
                          ? List.of(widget.model.logs)
                          : null;
                      seen = -1;
                    }),
                    icon: Icon(paused == null ? Icons.pause : Icons.play_arrow),
                    label: Text(paused == null ? '暂停显示' : '继续显示'),
                  ),
                  IconButton(
                    tooltip: '导出筛选结果',
                    onPressed: visible.isEmpty ? null : export,
                    icon: const Icon(Icons.save_alt),
                  ),
                  IconButton(
                    tooltip: '清空所有日志',
                    onPressed: clear,
                    icon: const Icon(Icons.delete_outline),
                  ),
                ],
              ),
              const SizedBox(height: 8),
              AppTextField(
                controller: search,
                decoration: const InputDecoration(
                  isDense: true,
                  prefixIcon: Icon(Icons.search),
                  hintText: '筛选消息 ID、来源或内容',
                ),
              ),
              if (error.isNotEmpty)
                Text(
                  error,
                  style: TextStyle(color: Theme.of(context).colorScheme.error),
                ),
              const SizedBox(height: 8),
              Expanded(
                child: Row(
                  children: [
                    Expanded(
                      child: visible.isEmpty
                          ? const Center(child: Text('没有符合筛选条件的日志'))
                          : list(),
                    ),
                    if (wide && selected != null) ...[
                      const VerticalDivider(width: 16),
                      SizedBox(
                        width: 400,
                        child: LogEntryDetails(entry: selected!, cache: cache),
                      ),
                    ],
                  ],
                ),
              ),
              const SizedBox(height: 6),
              Text(
                '显示 ${visible.length} / ${widget.model.logs.length} 条 · 最多 2000 条 / 8 MiB · 已淘汰 ${widget.model.droppedLogs} 条 · Ctrl/Shift 多选，Ctrl+C 复制${paused == null ? '' : ' · 显示已暂停，接收仍继续'}',
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ],
          ),
        );
      },
    ),
  );
}

Uint8List encodeLogExport(List<LogEntry> entries) =>
    Uint8List.fromList(utf8.encode(entries.map((e) => e.full).join('\n\n')));

class LogEntryPage extends StatelessWidget {
  const LogEntryPage({super.key, required this.entry, required this.cache});
  final LogEntry entry;
  final LogAnalysisCache cache;
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('日志详情')),
    body: SafeArea(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: LogEntryDetails(entry: entry, cache: cache),
      ),
    ),
  );
}

class LogEntryDetails extends StatelessWidget {
  const LogEntryDetails({super.key, required this.entry, required this.cache});
  final LogEntry entry;
  final LogAnalysisCache cache;
  @override
  Widget build(BuildContext context) {
    final raw = entry.raw;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          '${entry.time.toIso8601String()} · ${entry.category}',
          style: Theme.of(context).textTheme.titleSmall,
        ),
        Wrap(
          spacing: 8,
          children: [
            TextButton(
              onPressed: () => copyWorkbench(context, entry.full),
              child: const Text('复制整条'),
            ),
            if (raw != null) ...[
              TextButton(
                onPressed: () => copyWorkbench(context, hex(raw)),
                child: const Text('复制 RAW'),
              ),
              TextButton(
                onPressed: () => Navigator.of(context).push(
                  MaterialPageRoute<void>(
                    builder: (_) => MessageAnalyzerPage(initialInput: hex(raw)),
                  ),
                ),
                child: const Text('完整分析工作台'),
              ),
            ],
          ],
        ),
        Expanded(
          child: raw == null
              ? TextPreview(entry.message)
              : DefaultTabController(
                  length: 2,
                  child: Column(
                    children: [
                      const TabBar(
                        tabs: [
                          Tab(text: '原始报文'),
                          Tab(text: '自动解析'),
                        ],
                      ),
                      Expanded(
                        child: TabBarView(
                          children: [
                            TextPreview('${entry.message}\n\n${hex(raw)}'),
                            FutureBuilder<(MessageAnalysis, String)>(
                              future: cache.get(entry),
                              builder: (context, state) {
                                if (state.hasError) {
                                  return TextPreview(
                                    '无法解析此帧：${state.error}\n\n原始报文仍可复制或送入分析工作台。',
                                  );
                                }
                                if (!state.hasData) {
                                  return const Center(
                                    child: CircularProgressIndicator(),
                                  );
                                }
                                return TextPreview(state.data!.$2);
                              },
                            ),
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
        ),
      ],
    );
  }
}

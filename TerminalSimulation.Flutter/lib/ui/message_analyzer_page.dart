import 'dart:async';
import 'dart:math' as math;
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import '../core/bytes.dart';
import '../core/jt808.dart';
import '../core/message_analysis.dart';
import 'components.dart';

class MessageAnalyzerSession {
  String input = '', filter = '', view = 'table';
  MessageAnalysis? result;
}

class MessageAnalyzerPage extends StatefulWidget {
  const MessageAnalyzerPage({super.key, this.session, this.initialInput});
  final String? initialInput;
  final MessageAnalyzerSession? session;
  @override
  State<MessageAnalyzerPage> createState() => _MessageAnalyzerPageState();
}

class _MessageAnalyzerPageState extends State<MessageAnalyzerPage> {
  final input = TextEditingController(), filter = TextEditingController();
  final inputFocus = FocusNode(),
      filterFocus = FocusNode(),
      resultFocus = FocusNode();
  final vertical = ScrollController(), horizontal = ScrollController();
  final collapsed = <String>{};
  MessageAnalysis? result;
  List<AnalysisField> visible = [];
  AnalysisField? selected;
  String error = '', view = 'table';
  bool busy = false;
  int generation = 0;
  Future<void> _workerTail = Future.value();
  final selection = <AnalysisField>{};
  @override
  void initState() {
    super.initState();
    final saved = widget.session;
    if (saved != null) {
      input.text = saved.input;
      filter.text = saved.filter;
      view = saved.view;
      result = saved.result;
      final query = filter.text.trim().toLowerCase();
      visible =
          result?.fields
              .where(
                (f) =>
                    query.isEmpty ||
                    '${f.group} ${f.cells.join(' ')}'.toLowerCase().contains(
                      query,
                    ),
              )
              .toList() ??
          [];
    }
    if (widget.initialInput != null) {
      input.text = widget.initialInput!;
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) unawaited(analyze());
      });
    }
    filter.addListener(applyFilter);
    input.addListener(inputChanged);
  }

  void inputChanged() {
    // Invalidates a slow worker before it can replace newer input/results.
    generation++;
    if (result != null || busy || error.isNotEmpty) {
      setState(() {
        result = null;
        visible = [];
        selected = null;
        selection.clear();
        busy = false;
        error = '';
      });
    }
  }

  void applyFilter() {
    final query = filter.text.trim().toLowerCase();
    setState(
      () => visible =
          result?.fields
              .where(
                (f) =>
                    query.isEmpty ||
                    '${f.group} ${f.cells.join(' ')}'.toLowerCase().contains(
                      query,
                    ),
              )
              .toList() ??
          [],
    );
    if (vertical.hasClients) vertical.jumpTo(0);
  }

  Future<void> analyze() async {
    if (busy) return;
    final text = input.text, epoch = ++generation;
    setState(() {
      busy = true;
      error = '';
      result = null;
      visible = [];
      selected = null;
      selection.clear();
    });
    try {
      final task = _workerTail.then((_) async {
        if (!mounted || epoch != generation) return null;
        return compute(
          analyzeMessage,
          text,
          debugLabel: 'JT808 message analysis',
        );
      });
      _workerTail = task.then<void>(
        (_) {},
        onError: (Object _, StackTrace _) {},
      );
      final value = await task;
      if (value == null) return;
      if (!mounted || epoch != generation) return;
      setState(() {
        result = value;
        busy = false;
        collapsed.clear();
      });
      applyFilter();
    } catch (e) {
      if (mounted && epoch == generation) {
        setState(() {
          busy = false;
          error = e is FormatException ? e.message : '报文解析失败，请检查输入格式和长度。';
        });
      }
    }
  }

  void cancel() {
    generation++;
    setState(() => busy = false);
  }

  void clear() {
    generation++;
    setState(() {
      busy = false;
      result = null;
      visible = [];
      selected = null;
      selection.clear();
      error = '';
    });
    input.clear();
    filter.clear();
    inputFocus.requestFocus();
  }

  Future<void> copy(String text) async {
    try {
      await Clipboard.setData(ClipboardData(text: text));
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('已复制'), duration: Duration(seconds: 1)),
        );
      }
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text('无法写入剪贴板，请重试')));
      }
    }
  }

  void sample() {
    input.text = hex(
      Jt808Codec.encode(
        Jt808Packet(
          id: 0x0200,
          phone: '013800000001',
          sequence: 1,
          body: LocationReport(
            latitude: 39.9042,
            longitude: 116.4074,
            speed: 60,
            direction: 90,
            time: DateTime(2026, 10, 20, 12),
          ).encode(),
        ),
      ),
    );
    unawaited(analyze());
  }

  @override
  void dispose() {
    generation++;
    final saved = widget.session;
    if (saved != null) {
      saved.input = input.text;
      saved.filter = filter.text;
      saved.view = view;
      saved.result = result;
    }
    for (final c in [input, filter]) {
      c.dispose();
    }
    for (final f in [inputFocus, filterFocus, resultFocus]) {
      f.dispose();
    }
    vertical.dispose();
    horizontal.dispose();
    super.dispose();
  }

  void choose(AnalysisField field) {
    setState(() {
      final keys = HardwareKeyboard.instance;
      if (keys.isShiftPressed &&
          selected != null &&
          visible.contains(selected)) {
        final a = visible.indexOf(selected!), b = visible.indexOf(field);
        selection.addAll(visible.sublist(math.min(a, b), math.max(a, b) + 1));
      } else if (keys.isControlPressed) {
        if (!selection.add(field)) selection.remove(field);
      } else {
        selection
          ..clear()
          ..add(field);
      }
      selected = field;
    });
    resultFocus.requestFocus();
  }

  String selectedRows() => visible
      .where(selection.contains)
      .map((f) => f.cells.join('\t'))
      .join('\n');

  KeyEventResult resultKey(FocusNode node, KeyEvent event) {
    if (event is! KeyDownEvent) return KeyEventResult.ignored;
    final control = HardwareKeyboard.instance.isControlPressed;
    if (control &&
        event.logicalKey == LogicalKeyboardKey.keyC &&
        selected != null) {
      unawaited(copy(selectedRows()));
      return KeyEventResult.handled;
    }
    if (control && event.logicalKey == LogicalKeyboardKey.keyA) {
      setState(() {
        selection.addAll(visible);
        selected ??= visible.firstOrNull;
      });
      return KeyEventResult.handled;
    }
    if (event.logicalKey == LogicalKeyboardKey.arrowDown ||
        event.logicalKey == LogicalKeyboardKey.arrowUp) {
      if (visible.isEmpty) return KeyEventResult.ignored;
      final index = selected == null ? -1 : visible.indexOf(selected!);
      final next =
          (index + (event.logicalKey == LogicalKeyboardKey.arrowDown ? 1 : -1))
              .clamp(0, visible.length - 1);
      choose(visible[next]);
      if (view == 'table' && vertical.hasClients) {
        vertical.jumpTo(
          (next * 56.0).clamp(0, vertical.position.maxScrollExtent),
        );
      }
      return KeyEventResult.handled;
    }
    return KeyEventResult.ignored;
  }

  @override
  Widget build(BuildContext context) => CallbackShortcuts(
    bindings: {
      const SingleActivator(LogicalKeyboardKey.enter, control: true): () =>
          unawaited(analyze()),
      const SingleActivator(LogicalKeyboardKey.keyF, control: true):
          filterFocus.requestFocus,
      const SingleActivator(LogicalKeyboardKey.keyL, control: true):
          inputFocus.requestFocus,
      const SingleActivator(LogicalKeyboardKey.escape): () {
        if (busy) {
          cancel();
        } else {
          FocusScope.of(context).unfocus();
        }
      },
    },
    child: Scaffold(
      appBar: AppBar(
        title: const Text('JT808 报文分析'),
        actions: [
          IconButton(
            tooltip: '填入示例位置报文',
            onPressed: busy ? null : sample,
            icon: const Icon(Icons.science_outlined),
          ),
        ],
      ),
      body: SafeArea(
        child: LayoutBuilder(
          builder: (context, box) {
            final compact = box.maxWidth < 760 || box.maxHeight < 540;
            final content = Padding(
              padding: EdgeInsets.all(compact ? 12 : 20),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  SizedBox(
                    height: compact ? 94 : 116,
                    child: AppTextField(
                      controller: input,
                      focusNode: inputFocus,
                      expands: true,
                      minLines: null,
                      maxLines: null,
                      maxLength: 262144,
                      maxLengthEnforcement: MaxLengthEnforcement.enforced,
                      style: const TextStyle(
                        fontFamily: 'Consolas',
                        fontSize: 14,
                      ),
                      autocorrect: false,
                      enableSuggestions: false,
                      decoration: const InputDecoration(
                        labelText: '原始 HEX 报文',
                        hintText: '粘贴包含 7E 边界的单帧报文，支持空格和换行',
                        alignLabelWithHint: true,
                        counterText: '',
                      ),
                    ),
                  ),
                  const SizedBox(height: 12),
                  Wrap(
                    spacing: 8,
                    runSpacing: 8,
                    crossAxisAlignment: WrapCrossAlignment.center,
                    children: [
                      FilledButton.icon(
                        onPressed: busy ? null : analyze,
                        icon: busy
                            ? const SizedBox.square(
                                dimension: 18,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                ),
                              )
                            : const Icon(Icons.manage_search),
                        label: Text(busy ? '解析中…' : '解析报文'),
                      ),
                      if (busy)
                        TextButton(onPressed: cancel, child: const Text('取消')),
                      OutlinedButton(onPressed: clear, child: const Text('清空')),
                      SegmentedButton<String>(
                        showSelectedIcon: false,
                        segments: const [
                          ButtonSegment(
                            value: 'table',
                            icon: Icon(Icons.table_rows_outlined),
                            label: Text('表格'),
                          ),
                          ButtonSegment(
                            value: 'tree',
                            icon: Icon(Icons.account_tree_outlined),
                            label: Text('树状'),
                          ),
                        ],
                        selected: {view},
                        onSelectionChanged: (v) =>
                            setState(() => view = v.first),
                      ),
                      PopupMenuButton<String>(
                        tooltip: '复制分析结果',
                        enabled: result != null,
                        onSelected: (v) => unawaited(
                          copy(v == 'json' ? result!.json : result!.table),
                        ),
                        itemBuilder: (_) => const [
                          PopupMenuItem(
                            value: 'table',
                            child: Text('复制完整表格（TSV）'),
                          ),
                          PopupMenuItem(value: 'json', child: Text('复制 JSON')),
                        ],
                        child: const Padding(
                          padding: EdgeInsets.all(12),
                          child: Row(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              Icon(Icons.copy, size: 18),
                              SizedBox(width: 6),
                              Text('复制结果'),
                            ],
                          ),
                        ),
                      ),
                      if (!compact)
                        Text(
                          'Ctrl+Enter 解析 · Ctrl+F 筛选',
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                    ],
                  ),
                  if (error.isNotEmpty)
                    Padding(
                      padding: const EdgeInsets.symmetric(vertical: 10),
                      child: SelectableText(
                        error,
                        style: TextStyle(
                          color: Theme.of(context).colorScheme.error,
                        ),
                      ),
                    ),
                  const SizedBox(height: 12),
                  if (result != null) ...[
                    Wrap(
                      spacing: 16,
                      runSpacing: 8,
                      alignment: WrapAlignment.spaceBetween,
                      crossAxisAlignment: WrapCrossAlignment.center,
                      children: [
                        Text(
                          result!.title,
                          style: Theme.of(context).textTheme.titleSmall,
                        ),
                        SizedBox(
                          width: math.min(
                            320,
                            math.max(120, box.maxWidth - 48),
                          ),
                          child: AppTextField(
                            controller: filter,
                            focusNode: filterFocus,
                            decoration: const InputDecoration(
                              isDense: true,
                              hintText: '筛选字段、HEX 或结果',
                              prefixIcon: Icon(Icons.search),
                            ),
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 8),
                    if (result!.warnings.isNotEmpty)
                      ConstrainedBox(
                        constraints: const BoxConstraints(maxHeight: 70),
                        child: SingleChildScrollView(
                          child: Text(
                            result!.warnings.join('\n'),
                            style: TextStyle(
                              color: Theme.of(context).colorScheme.error,
                            ),
                          ),
                        ),
                      ),
                  ],
                  Expanded(
                    child: result == null
                        ? Center(
                            child: Text(
                              busy ? '正在后台解析报文…' : '输入报文后解析，或使用右上角示例',
                              textAlign: TextAlign.center,
                            ),
                          )
                        : Focus(
                            focusNode: resultFocus,
                            onKeyEvent: resultKey,
                            child: RepaintBoundary(
                              child: view == 'table' ? table() : tree(),
                            ),
                          ),
                  ),
                  if (selected != null) ...[
                    const Divider(),
                    SizedBox(
                      height: compact ? 82 : 114,
                      child: SingleChildScrollView(
                        child: SelectionArea(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Wrap(
                                spacing: 8,
                                crossAxisAlignment: WrapCrossAlignment.center,
                                children: [
                                  Text(
                                    selected!.name,
                                    style: const TextStyle(
                                      fontWeight: FontWeight.bold,
                                    ),
                                  ),
                                  Text(
                                    '偏移 ${selected!.offset} · ${selected!.length} B · ${selected!.type}',
                                  ),
                                  TextButton(
                                    onPressed: () => copy(selected!.raw),
                                    child: const Text('复制 HEX'),
                                  ),
                                ],
                              ),
                              Text(
                                selected!.raw,
                                style: const TextStyle(
                                  fontFamily: 'Consolas',
                                  fontSize: 12,
                                ),
                              ),
                              Text(selected!.value),
                            ],
                          ),
                        ),
                      ),
                    ),
                  ],
                  if (result != null)
                    Text(
                      '${visible.length} / ${result!.fields.length} 字段 · 偏移以反转义帧首个 7E 为 0 · Ctrl/Shift 多选 · Ctrl+C 复制',
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                ],
              ),
            );
            final scale = MediaQuery.textScalerOf(context).scale(14) / 14;
            final minimum = 560.0 * scale.clamp(1.0, 2.0);
            return SingleChildScrollView(
              primary: false,
              child: SizedBox(
                height: math.max(minimum, box.maxHeight),
                child: content,
              ),
            );
          },
        ),
      ),
    ),
  );
  Widget table() => LayoutBuilder(
    builder: (context, box) {
      final width = math.max(1060.0, box.maxWidth),
          scheme = Theme.of(context).colorScheme;
      const widths = [190.0, 250.0, 100.0, 80.0, 90.0];
      Widget cells(List<String> values, {bool heading = false}) => Row(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          for (var i = 0; i < values.length; i++)
            SizedBox(
              width: i < 5 ? widths[i] : width - 710,
              child: Padding(
                padding: const EdgeInsets.symmetric(
                  horizontal: 12,
                  vertical: 10,
                ),
                child: Align(
                  alignment: Alignment.topLeft,
                  child: Text(
                    values[i],
                    maxLines: heading ? 1 : 2,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      fontFamily: heading ? null : 'Consolas',
                      fontSize: 13,
                      fontWeight: heading ? FontWeight.w600 : null,
                      color: i == 1 ? scheme.primary : null,
                    ),
                  ),
                ),
              ),
            ),
        ],
      );
      return Scrollbar(
        controller: horizontal,
        thumbVisibility: true,
        notificationPredicate: (n) => n.metrics.axis == Axis.horizontal,
        child: SingleChildScrollView(
          controller: horizontal,
          scrollDirection: Axis.horizontal,
          child: SizedBox(
            width: width,
            height: box.maxHeight,
            child: Column(
              children: [
                SizedBox(
                  height: 44,
                  child: ColoredBox(
                    color: scheme.surfaceContainerHigh,
                    child: cells([
                      '字段',
                      '报文原数据',
                      '数据类型',
                      '起始位',
                      '数据长度',
                      '分析结果',
                    ], heading: true),
                  ),
                ),
                Expanded(
                  child: Scrollbar(
                    controller: vertical,
                    thumbVisibility: true,
                    child: ListView.builder(
                      controller: vertical,
                      itemCount: visible.length,
                      itemExtent: 56,
                      itemBuilder: (context, i) {
                        final f = visible[i];
                        return Material(
                          color: selection.contains(f)
                              ? scheme.secondaryContainer
                              : i.isEven
                              ? scheme.surface
                              : scheme.surfaceContainerLow,
                          child: InkWell(
                            onTap: () => choose(f),
                            onDoubleTap: () => copy(f.cells.join('\t')),
                            child: cells(f.cells),
                          ),
                        );
                      },
                    ),
                  ),
                ),
                const SizedBox(height: 12),
              ],
            ),
          ),
        ),
      );
    },
  );
  Widget tree() {
    final groups = <String, List<AnalysisField>>{};
    for (final f in visible) {
      groups.putIfAbsent(f.group, () => []).add(f);
    }
    final rows = <(String, AnalysisField?)>[];
    for (final e in groups.entries) {
      rows.add((e.key, null));
      if (!collapsed.contains(e.key)) {
        rows.addAll(e.value.map((f) => (e.key, f)));
      }
    }
    return ListView.builder(
      primary: false,
      itemCount: rows.length,
      itemBuilder: (context, i) {
        final (group, f) = rows[i];
        if (f == null) {
          return ListTile(
            leading: Icon(
              collapsed.contains(group)
                  ? Icons.chevron_right
                  : Icons.expand_more,
            ),
            title: Text('$group · ${groups[group]!.length} 字段'),
            onTap: () => setState(() {
              if (!collapsed.add(group)) collapsed.remove(group);
            }),
          );
        }
        return Padding(
          padding: const EdgeInsets.only(left: 24),
          child: ListTile(
            selected: selection.contains(f),
            title: Text(f.name),
            subtitle: Text(
              '${f.value}\n${f.raw}',
              maxLines: 3,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(fontFamily: 'Consolas'),
            ),
            onTap: () => choose(f),
          ),
        );
      },
    );
  }
}

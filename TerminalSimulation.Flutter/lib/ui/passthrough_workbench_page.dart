import 'dart:async';
import 'dart:convert';
import 'dart:math' as math;
import 'package:fast_gbk/fast_gbk.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import '../core/bytes.dart';
import '../services/app_model.dart';
import '../services/terminal_session.dart' as terminal;
import '../services/document_files.dart';
import 'components.dart';
import 'workbench.dart';

class PassthroughDraft {
  String text = '', type = '00', sendEncoding = 'GBK', displayEncoding = 'GBK';
}

class PassthroughWorkbenchPage extends StatefulWidget {
  const PassthroughWorkbenchPage({
    super.key,
    required this.model,
    required this.draft,
  });
  final AppModel model;
  final PassthroughDraft draft;
  @override
  State<PassthroughWorkbenchPage> createState() =>
      _PassthroughWorkbenchPageState();
}

class _PassthroughWorkbenchPageState extends State<PassthroughWorkbenchPage> {
  late final input = TextEditingController(text: d.text),
      type = TextEditingController(text: d.type);
  PassthroughDraft get d => widget.draft;
  final scroll = ScrollController();
  bool sending = false, autoScroll = true;
  String error = '';
  PassthroughEntry? lastHead;
  String content(PassthroughEntry e) => switch (d.displayEncoding) {
    'HEX' => hex(e.data),
    'UTF-8' => utf8.decode(e.data, allowMalformed: true),
    _ => gbk.decode(e.data, allowMalformed: true),
  };
  @override
  void dispose() {
    input.dispose();
    type.dispose();
    scroll.dispose();
    super.dispose();
  }

  Future<void> send() async {
    if (sending) return;
    try {
      final rawType = d.type.trim().replaceFirst(RegExp(r'^0[xX]'), '');
      if (!RegExp(r'^[0-9a-fA-F]{1,2}$').hasMatch(rawType)) {
        throw const FormatException('透传类型应为 00–FF 十六进制字节');
      }
      final data = switch (d.sendEncoding) {
        'HEX' => unhex(d.text),
        'UTF-8' => utf8.encode(d.text),
        _ => gbk.encode(d.text),
      };
      if (data.length > 1022) throw const FormatException('单帧透传内容最多 1022 字节');
      final epoch = widget.model.session.epoch;
      setState(() {
        sending = true;
        error = '';
      });
      await widget.model.controller.passthrough(
        int.parse(rawType, radix: 16),
        data,
      );
      if (mounted && epoch == widget.model.session.epoch) {
        setState(() => error = '');
      }
    } catch (e) {
      if (mounted) {
        setState(() => error = e is FormatException ? e.message : '发送失败：$e');
      }
    } finally {
      if (mounted) setState(() => sending = false);
    }
  }

  Future<void> export() async {
    try {
      final text = widget.model.passthroughMessages.reversed
          .map(
            (e) =>
                '${e.time.toIso8601String()} ${e.outbound ? '发送' : '接收'} ${e.textMessage ? '文本标志' : '透传类型'} 0x${e.type.toRadixString(16).padLeft(2, '0')}\n${content(e)}\nHEX: ${hex(e.data)}',
          )
          .join('\n\n');
      await DocumentFiles.save(
        Uint8List.fromList(utf8.encode(text)),
        'passthrough.txt',
      );
    } catch (e) {
      if (mounted) setState(() => error = '导出失败：$e');
    }
  }

  bool showRecords = false;

  Future<void> recordOption(String value) async {
    if (value == 'clear') {
      if (await confirmAction(
            context,
            title: '清空透传记录',
            message: '清空本次会话的全部透传与文本记录？',
            nativeMaterial: false,
          ) &&
          mounted) {
        widget.model.clearPassthrough();
      }
    } else {
      setState(() {
        if (value == 'scroll') {
          autoScroll = !autoScroll;
        } else {
          d.displayEncoding = value;
        }
      });
    }
  }

  Widget records(List<PassthroughEntry> entries, double width) =>
      entries.isEmpty
      ? const WorkbenchEmptyState(
          icon: Icons.forum_outlined,
          title: '暂无透传记录',
          message: '发送和接收的透传、文本消息会显示在这里。',
        )
      : Scrollbar(
          controller: scroll,
          child: ListView.builder(
            controller: scroll,
            reverse: true,
            itemCount: entries.length,
            itemBuilder: (context, index) {
              final e = entries[index];
              return Align(
                alignment: e.outbound
                    ? Alignment.centerRight
                    : Alignment.centerLeft,
                child: ConstrainedBox(
                  constraints: BoxConstraints(maxWidth: width * .9),
                  child: Card(
                    color: e.outbound
                        ? Theme.of(context).colorScheme.primaryContainer
                        : null,
                    child: Padding(
                      padding: const EdgeInsets.all(12),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            '${e.time.toIso8601String().substring(11, 23)} · ${e.outbound ? '发送' : '接收'} · ${e.textMessage ? '文本标志' : '类型'} 0x${e.type.toRadixString(16).padLeft(2, '0').toUpperCase()} · ${e.data.length} B',
                            style: Theme.of(context).textTheme.labelSmall,
                          ),
                          const SizedBox(height: 6),
                          SelectableText(
                            content(e),
                            style: const TextStyle(fontFamily: 'Consolas'),
                          ),
                          Wrap(
                            spacing: 4,
                            children: [
                              TextButton(
                                onPressed: () =>
                                    copyWorkbench(context, hex(e.data)),
                                child: const Text('复制 HEX'),
                              ),
                              TextButton(
                                onPressed: () =>
                                    copyWorkbench(context, content(e)),
                                child: const Text('复制内容'),
                              ),
                              if (!e.textMessage)
                                TextButton(
                                  onPressed: () => setState(() {
                                    d.type = e.type
                                        .toRadixString(16)
                                        .padLeft(2, '0');
                                    type.text = d.type;
                                    d.sendEncoding = 'HEX';
                                    d.text = hex(e.data);
                                    input.text = d.text;
                                    showRecords = false;
                                  }),
                                  child: const Text('填入编辑器'),
                                ),
                            ],
                          ),
                        ],
                      ),
                    ),
                  ),
                ),
              );
            },
          ),
        );

  Widget byteStatus(bool compact) => ValueListenableBuilder<TextEditingValue>(
    valueListenable: input,
    builder: (context, value, _) {
      int? count;
      try {
        count = switch (d.sendEncoding) {
          'HEX' => unhex(value.text).length,
          'UTF-8' => utf8.encode(value.text).length,
          _ => gbk.encode(value.text).length,
        };
      } catch (_) {}
      return Wrap(
        spacing: 12,
        runSpacing: 4,
        children: [
          Text(
            count == null ? 'HEX 字节尚未完整' : '$count / 1022 B',
            style: Theme.of(context).textTheme.bodySmall?.copyWith(
              color: count != null && count > 1022
                  ? Theme.of(context).colorScheme.error
                  : null,
            ),
          ),
          if (!compact)
            Text('Ctrl+Enter 发送', style: Theme.of(context).textTheme.bodySmall),
        ],
      );
    },
  );

  Widget composer(bool compact) => Card(
    margin: EdgeInsets.zero,
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          FieldGrid(
            minWidth:
                112 *
                (MediaQuery.textScalerOf(context).scale(14) / 14).clamp(1, 2),
            children: [
              AppTextField(
                key: const ValueKey('passthrough-type'),
                controller: type,
                enabled: !sending,
                maxLength: 4,
                decoration: const InputDecoration(
                  labelText: '透传类型（HEX）',
                  counterText: '',
                  isDense: true,
                ),
                onChanged: (v) => d.type = v,
              ),
              DropdownButtonFormField<String>(
                initialValue: d.sendEncoding,
                key: ValueKey(d.sendEncoding),
                isExpanded: true,
                decoration: const InputDecoration(
                  labelText: '发送编码',
                  isDense: true,
                ),
                items: [
                  for (final v in ['GBK', 'UTF-8', 'HEX'])
                    DropdownMenuItem(value: v, child: Text(v)),
                ],
                onChanged: sending
                    ? null
                    : (v) => setState(() => d.sendEncoding = v!),
              ),
            ],
          ),
          const SizedBox(height: 16),
          Expanded(
            child: AppTextField(
              key: const ValueKey('passthrough-content'),
              controller: input,
              textAlignVertical: TextAlignVertical.top,
              enabled: !sending,
              expands: true,
              maxLines: null,
              minLines: null,
              maxLength: 8192,
              enableSuggestions: false,
              autocorrect: false,
              onChanged: (v) => d.text = v,
              style: const TextStyle(fontFamily: 'Consolas'),
              decoration: const InputDecoration(
                labelText: '透传内容',
                alignLabelWithHint: true,
                hintText: '输入要发送的数据',
                counterText: '',
              ),
            ),
          ),
          const SizedBox(height: 8),
          byteStatus(compact),
          if (error.isNotEmpty)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxHeight: 72),
                child: SingleChildScrollView(
                  child: SelectableText(
                    error,
                    style: TextStyle(
                      color: Theme.of(context).colorScheme.error,
                    ),
                  ),
                ),
              ),
            ),
          const SizedBox(height: 12),
          FilledButton.icon(
            onPressed:
                sending ||
                    widget.model.session.state !=
                        terminal.ConnectionState.connected
                ? null
                : send,
            icon: const Icon(Icons.send_outlined),
            label: Text(sending ? '发送中…' : '发送 0x0900'),
          ),
        ],
      ),
    ),
  );

  @override
  Widget build(BuildContext context) => CallbackShortcuts(
    bindings: {
      const SingleActivator(LogicalKeyboardKey.enter, control: true): () =>
          unawaited(send()),
    },
    child: Scaffold(
      appBar: AppBar(
        title: const Text('数据透传'),
        actions: [
          IconButton(
            tooltip: '导出当前记录',
            onPressed: export,
            icon: const Icon(Icons.save_alt),
          ),
        ],
      ),
      body: SafeArea(
        child: ModelSlice<(int, terminal.ConnectionState)>(
          listenable: widget.model,
          select: () => (widget.model.logRevision, widget.model.session.state),
          builder: (context, _) => WorkbenchViewport(
            minHeight: 560,
            builder: (context, box) {
              final compact = box.maxWidth < 720;
              final entries = widget.model.passthroughMessages;
              if (entries.firstOrNull != lastHead) {
                lastHead = entries.firstOrNull;
                if (autoScroll) {
                  WidgetsBinding.instance.addPostFrameCallback((_) {
                    if (mounted && scroll.hasClients) scroll.jumpTo(0);
                  });
                }
              }
              final connected =
                  widget.model.session.state ==
                  terminal.ConnectionState.connected;
              final colors = Theme.of(context).colorScheme;
              final scale = MediaQuery.textScalerOf(context).scale(14) / 14;
              return Padding(
                padding: const EdgeInsets.all(12),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Row(
                      children: [
                        Icon(
                          connected ? Icons.link : Icons.link_off,
                          size: 18,
                          color: connected
                              ? colors.primary
                              : colors.onSurfaceVariant,
                        ),
                        const SizedBox(width: 6),
                        Expanded(
                          child: Text(
                            connected ? '已连接' : '未连接',
                            style: Theme.of(context).textTheme.bodySmall,
                          ),
                        ),
                        Text(
                          '${entries.length} 条',
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                        PopupMenuButton<String>(
                          tooltip: '记录选项',
                          onSelected: recordOption,
                          itemBuilder: (_) => [
                            const PopupMenuItem(
                              enabled: false,
                              child: Text('记录显示编码'),
                            ),
                            for (final v in ['GBK', 'UTF-8', 'HEX'])
                              CheckedPopupMenuItem(
                                value: v,
                                checked: d.displayEncoding == v,
                                child: Text(v),
                              ),
                            const PopupMenuDivider(),
                            CheckedPopupMenuItem(
                              value: 'scroll',
                              checked: autoScroll,
                              child: const Text('自动滚动'),
                            ),
                            PopupMenuItem(
                              value: 'clear',
                              enabled: entries.isNotEmpty,
                              child: const Text('清空记录'),
                            ),
                          ],
                        ),
                      ],
                    ),
                    Offstage(
                      offstage: !compact,
                      child: Padding(
                        padding: const EdgeInsets.only(bottom: 12),
                        child: SegmentedButton<bool>(
                          showSelectedIcon: false,
                          expandedInsets: EdgeInsets.zero,
                          segments: const [
                            ButtonSegment(value: false, label: Text('发送数据')),
                            ButtonSegment(value: true, label: Text('消息记录')),
                          ],
                          selected: {showRecords},
                          onSelectionChanged: (value) {
                            FocusManager.instance.primaryFocus?.unfocus();
                            setState(() => showRecords = value.first);
                          },
                        ),
                      ),
                    ),
                    Expanded(
                      child: LayoutBuilder(
                        builder: (context, area) {
                          final composerHeight = compact
                              ? area.maxHeight
                              : math
                                    .min(
                                      280 * scale.clamp(1, 2),
                                      math.max(0, area.maxHeight - 96),
                                    )
                                    .toDouble();
                          return Stack(
                            children: [
                              Positioned(
                                left: 0,
                                right: 0,
                                top: 0,
                                bottom: compact ? 0 : composerHeight + 12,
                                child: Offstage(
                                  offstage: compact && !showRecords,
                                  child: records(entries, area.maxWidth),
                                ),
                              ),
                              Positioned(
                                left: 0,
                                right: 0,
                                bottom: 0,
                                height: composerHeight,
                                child: Offstage(
                                  offstage: compact && showRecords,
                                  child: composer(compact),
                                ),
                              ),
                            ],
                          );
                        },
                      ),
                    ),
                  ],
                ),
              );
            },
          ),
        ),
      ),
    ),
  );
}

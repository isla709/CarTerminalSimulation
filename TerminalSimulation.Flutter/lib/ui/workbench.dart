import 'dart:math' as math;
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

Future<void> copyWorkbench(BuildContext context, String value) async {
  try {
    await Clipboard.setData(ClipboardData(text: value));
    if (context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('已复制'), duration: Duration(seconds: 1)),
      );
    }
  } catch (_) {
    if (context.mounted) {
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(const SnackBar(content: Text('剪贴板不可用，请重试')));
    }
  }
}

class WorkbenchViewport extends StatelessWidget {
  const WorkbenchViewport({
    super.key,
    required this.builder,
    this.minHeight = 540,
  });
  final Widget Function(BuildContext, BoxConstraints) builder;
  final double minHeight;
  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, box) {
      final minimum =
          minHeight *
          (MediaQuery.textScalerOf(context).scale(14) / 14).clamp(1.0, 2.0);
      return SingleChildScrollView(
        primary: false,
        child: SizedBox(
          height: math.max(minimum, box.maxHeight),
          child: LayoutBuilder(builder: builder),
        ),
      );
    },
  );
}

class WorkbenchTabs extends StatelessWidget {
  const WorkbenchTabs({
    super.key,
    required this.labels,
    this.compactLabels,
    this.onTap,
  });
  final List<String> labels;
  final List<String>? compactLabels;
  final ValueChanged<int>? onTap;
  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, box) {
      final compact = box.maxWidth < 600;
      final scale = MediaQuery.textScalerOf(context).scale(14) / 14;
      final titles = compact ? compactLabels ?? labels : labels;
      return TabBar(
        onTap: onTap,
        isScrollable: !compact,
        tabAlignment: compact ? TabAlignment.fill : TabAlignment.start,
        labelPadding: EdgeInsets.symmetric(horizontal: compact ? 4 : 16),
        tabs: [
          for (final title in titles)
            Tab(
              height: 48 * scale.clamp(1.0, 1.7),
              child: Text(title, textAlign: TextAlign.center, maxLines: 2),
            ),
        ],
      );
    },
  );
}

class WorkbenchEmptyState extends StatelessWidget {
  const WorkbenchEmptyState({
    super.key,
    required this.icon,
    required this.title,
    required this.message,
    this.action,
  });
  final IconData icon;
  final String title, message;
  final Widget? action;
  @override
  Widget build(BuildContext context) => Center(
    child: SingleChildScrollView(
      padding: const EdgeInsets.all(20),
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 320),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, size: 36, color: Theme.of(context).colorScheme.outline),
            const SizedBox(height: 12),
            Text(
              title,
              textAlign: TextAlign.center,
              style: Theme.of(context).textTheme.titleSmall,
            ),
            const SizedBox(height: 6),
            Text(
              message,
              textAlign: TextAlign.center,
              style: Theme.of(context).textTheme.bodySmall?.copyWith(
                color: Theme.of(context).colorScheme.onSurfaceVariant,
              ),
            ),
            if (action != null)
              Padding(padding: const EdgeInsets.only(top: 12), child: action),
          ],
        ),
      ),
    ),
  );
}

/// Bound layout work while preserving complete data for copy/export actions.
class TextPreview extends StatelessWidget {
  const TextPreview(
    this.text, {
    super.key,
    this.limit = 65536,
    this.empty = '暂无内容',
  });
  final String text, empty;
  final int limit;
  @override
  Widget build(BuildContext context) {
    if (text.isEmpty) return Center(child: Text(empty));
    final preview = text.substring(0, math.min(limit, text.length));
    final lines = <String>[];
    for (final line in preview.split('\n')) {
      if (line.isEmpty) {
        lines.add('');
        continue;
      }
      for (var i = 0; i < line.length; i += 2048) {
        lines.add(line.substring(i, math.min(i + 2048, line.length)));
      }
    }
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (text.length > limit)
          Padding(
            padding: const EdgeInsets.all(8),
            child: Text(
              '预览前 $limit 个字符；复制或保存可取得完整内容',
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ),
        Expanded(
          child: SelectionArea(
            child: ListView.builder(
              primary: false,
              padding: const EdgeInsets.all(12),
              itemCount: lines.length,
              itemBuilder: (context, i) => Text(
                lines[i].isEmpty ? ' ' : lines[i],
                style: const TextStyle(fontFamily: 'Consolas', fontSize: 13),
              ),
            ),
          ),
        ),
      ],
    );
  }
}

import 'dart:ui';
import 'dart:math' as math;
import 'package:flutter/material.dart';
import '../services/native_material.dart';

Future<bool> confirmAction(
  BuildContext context, {
  required String title,
  required String message,
  bool nativeMaterial = true,
}) async {
  if (nativeMaterial) {
    final result = await NativeMaterial.showConfirmation(title, message);
    if (result != null) return result;
  }
  if (!context.mounted) return false;
  return await showDialog<bool>(
        context: context,
        builder: (context) => AlertDialog(
          title: Text(title),
          content: Text(message),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('取消'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('确定'),
            ),
          ],
        ),
      ) ??
      false;
}

class GlassPanel extends StatelessWidget {
  const GlassPanel({
    super.key,
    required this.child,
    this.enabled = true,
    this.padding = const EdgeInsets.all(16),
  });
  final Widget child;
  final bool enabled;
  final EdgeInsets padding;
  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final box = DecoratedBox(
      decoration: BoxDecoration(
        color: scheme.surfaceContainer.withValues(alpha: enabled ? 0.86 : 1),
        borderRadius: BorderRadius.circular(24),
        border: Border.all(color: scheme.outlineVariant.withValues(alpha: .55)),
      ),
      child: Padding(padding: padding, child: child),
    );
    return ClipRRect(
      borderRadius: BorderRadius.circular(24),
      child: enabled
          ? BackdropFilter(
              filter: ImageFilter.blur(sigmaX: 10, sigmaY: 10),
              child: box,
            )
          : box,
    );
  }
}

class ScrollableRail extends StatelessWidget {
  const ScrollableRail({super.key, required this.child, required this.width});
  final Widget child;
  final double width;
  @override
  Widget build(BuildContext context) => SizedBox(
    width: width,
    child: LayoutBuilder(
      builder: (context, constraints) {
        final scale = MediaQuery.textScalerOf(context).scale(14) / 14;
        return SingleChildScrollView(
          child: SizedBox(
            height: math.max(constraints.maxHeight, 400 * scale),
            child: child,
          ),
        );
      },
    ),
  );
}

class Section extends StatelessWidget {
  const Section(
    this.title, {
    super.key,
    this.subtitle,
    this.trailing,
    required this.child,
  });
  final String title;
  final String? subtitle;
  final Widget? trailing;
  final Widget child;
  @override
  Widget build(BuildContext context) => Card(
    margin: EdgeInsets.zero,
    child: Padding(
      padding: const EdgeInsets.all(20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  title,
                  style: Theme.of(context).textTheme.titleMedium,
                ),
              ),
              ?trailing,
            ],
          ),
          if (subtitle != null)
            Padding(
              padding: const EdgeInsets.only(top: 6),
              child: Text(
                subtitle!,
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ),
          const SizedBox(height: 18),
          child,
        ],
      ),
    ),
  );
}

class FieldGrid extends StatelessWidget {
  const FieldGrid({super.key, required this.children, this.minWidth = 220});
  final List<Widget> children;
  final double minWidth;
  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, constraints) {
      final columns = (constraints.maxWidth / (minWidth + 12)).floor().clamp(
            1,
            4,
          ),
          width = (constraints.maxWidth - 12 * (columns - 1)) / columns;
      return Wrap(
        spacing: 12,
        runSpacing: 16,
        children: children
            .map((child) => SizedBox(width: width, child: child))
            .toList(),
      );
    },
  );
}

class PageBody extends StatelessWidget {
  const PageBody({
    super.key,
    required this.title,
    required this.description,
    required this.children,
  });
  final String title, description;
  final List<Widget> children;
  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, c) {
      final padding = c.maxWidth < 600 ? 16.0 : 28.0;
      return SingleChildScrollView(
        padding: EdgeInsets.all(padding),
        child: Align(
          alignment: Alignment.topCenter,
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 1560),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  title,
                  style: Theme.of(context).textTheme.headlineMedium?.copyWith(
                    fontWeight: FontWeight.w700,
                  ),
                ),
                const SizedBox(height: 8),
                Text(
                  description,
                  style: TextStyle(
                    color: Theme.of(context).colorScheme.onSurfaceVariant,
                  ),
                ),
                const SizedBox(height: 24),
                for (final child in children) ...[
                  child,
                  const SizedBox(height: 18),
                ],
              ],
            ),
          ),
        ),
      );
    },
  );
}

class EditField extends StatefulWidget {
  const EditField(
    this.label, {
    super.key,
    required this.value,
    required this.onChanged,
    this.numeric = false,
    this.lines = 1,
  });
  final String label, value;
  final ValueChanged<String> onChanged;
  final bool numeric;
  final int lines;
  @override
  State<EditField> createState() => _EditFieldState();
}

class _EditFieldState extends State<EditField> {
  late final controller = TextEditingController(text: widget.value);
  final focus = FocusNode();
  @override
  void didUpdateWidget(covariant EditField oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!focus.hasFocus && controller.text != widget.value) {
      controller.text = widget.value;
    }
  }

  @override
  void dispose() {
    controller.dispose();
    focus.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => TextFormField(
    controller: controller,
    focusNode: focus,
    decoration: InputDecoration(
      labelText: widget.label,
      alignLabelWithHint: widget.lines > 1,
    ),
    onChanged: widget.onChanged,
    maxLines: widget.lines,
    keyboardType: widget.numeric
        ? const TextInputType.numberWithOptions(decimal: true, signed: true)
        : TextInputType.text,
  );
}

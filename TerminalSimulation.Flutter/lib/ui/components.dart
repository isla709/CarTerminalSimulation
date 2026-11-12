import 'dart:ui';
import 'dart:math' as math;
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import '../services/native_material.dart';
import 'studio_geometry.dart';

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
        borderRadius: StudioGeometry.floatingCorners,
        border: Border.all(color: scheme.outlineVariant.withValues(alpha: .55)),
      ),
      child: Padding(padding: padding, child: child),
    );
    return ClipRRect(
      borderRadius: StudioGeometry.floatingCorners,
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

class ActionGrid extends StatelessWidget {
  const ActionGrid({super.key, required this.children});
  final List<Widget> children;

  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, constraints) {
      const gap = 12.0;
      final scale = math.max(
        1.0,
        MediaQuery.textScalerOf(context).scale(14) / 14,
      );
      final minWidth = 176 * scale;
      final columns = constraints.maxWidth >= minWidth * 4 + gap * 3
          ? 4
          : constraints.maxWidth >= minWidth * 2 + gap
          ? 2
          : 1;
      final theme = Theme.of(context);
      final style = ButtonStyle(
        minimumSize: WidgetStatePropertyAll(
          Size(0, math.max(48, 24 + 21 * scale)),
        ),
        padding: const WidgetStatePropertyAll(
          EdgeInsets.symmetric(horizontal: 16, vertical: 12),
        ),
        shape: const WidgetStatePropertyAll(StadiumBorder()),
        iconSize: const WidgetStatePropertyAll(20),
        tapTargetSize: MaterialTapTargetSize.shrinkWrap,
        visualDensity: VisualDensity.standard,
      );
      return Theme(
        data: theme.copyWith(
          outlinedButtonTheme: OutlinedButtonThemeData(
            style: theme.outlinedButtonTheme.style?.merge(style) ?? style,
          ),
          filledButtonTheme: FilledButtonThemeData(
            style: theme.filledButtonTheme.style?.merge(style) ?? style,
          ),
        ),
        child: Column(
          children: [
            for (var start = 0; start < children.length; start += columns) ...[
              if (start > 0) const SizedBox(height: gap),
              IntrinsicHeight(
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    for (var column = 0; column < columns; column++) ...[
                      if (column > 0) const SizedBox(width: gap),
                      Expanded(
                        child: start + column < children.length
                            ? children[start + column]
                            : const SizedBox.shrink(),
                      ),
                    ],
                  ],
                ),
              ),
            ],
          ],
        ),
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
        padding: EdgeInsets.fromLTRB(
          padding,
          padding + MediaQuery.paddingOf(context).top,
          padding,
          padding + MediaQuery.paddingOf(context).bottom,
        ),
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

/// Own implicit editing resources locally; never inherit a container's
/// PageStorage identifier for the text viewport (an ExpansionTile stores bools).
class AppTextField extends StatefulWidget {
  const AppTextField({
    super.key,
    this.controller,
    this.initialValue,
    this.focusNode,
    this.enabled,
    this.readOnly = false,
    this.obscureText = false,
    this.autofocus = false,
    this.autocorrect = true,
    this.enableSuggestions = true,
    this.decoration = const InputDecoration(),
    this.style,
    this.textAlignVertical,
    this.keyboardType,
    this.textInputAction,
    this.maxLines = 1,
    this.minLines,
    this.expands = false,
    this.maxLength,
    this.maxLengthEnforcement,
    this.onChanged,
    this.onSubmitted,
    this.validator,
    this.autovalidateMode,
  }) : assert(controller == null || initialValue == null);
  final TextEditingController? controller;
  final String? initialValue;
  final FocusNode? focusNode;
  final bool? enabled;
  final bool readOnly,
      obscureText,
      autofocus,
      autocorrect,
      enableSuggestions,
      expands;
  final InputDecoration decoration;
  final TextStyle? style;
  final TextAlignVertical? textAlignVertical;
  final TextInputType? keyboardType;
  final TextInputAction? textInputAction;
  final int? maxLines, minLines, maxLength;
  final MaxLengthEnforcement? maxLengthEnforcement;
  final ValueChanged<String>? onChanged, onSubmitted;
  final FormFieldValidator<String>? validator;
  final AutovalidateMode? autovalidateMode;
  @override
  State<AppTextField> createState() => _AppTextFieldState();
}

class _AppTextFieldState extends State<AppTextField> {
  TextEditingController? _controller;
  FocusNode? _focus;
  final _scroll = ScrollController(keepScrollOffset: false);
  TextEditingController get controller =>
      widget.controller ??
      (_controller ??= TextEditingController(text: widget.initialValue));
  @override
  void didUpdateWidget(covariant AppTextField old) {
    super.didUpdateWidget(old);
    if (old.controller != null && widget.controller == null) {
      _controller ??= TextEditingController();
      _controller!.value = old.controller!.value;
    }
  }

  @override
  void dispose() {
    _controller?.dispose();
    _focus?.dispose();
    _scroll.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => TextFormField(
    controller: controller,
    focusNode: widget.focusNode ?? (_focus ??= FocusNode()),
    scrollController: _scroll,
    enabled: widget.enabled,
    readOnly: widget.readOnly,
    obscureText: widget.obscureText,
    autofocus: widget.autofocus,
    autocorrect: widget.autocorrect,
    enableSuggestions: widget.enableSuggestions,
    decoration: widget.decoration,
    style: widget.style,
    textAlignVertical: widget.textAlignVertical,
    keyboardType: widget.keyboardType,
    textInputAction: widget.textInputAction,
    maxLines: widget.maxLines,
    minLines: widget.minLines,
    expands: widget.expands,
    maxLength: widget.maxLength,
    maxLengthEnforcement: widget.maxLengthEnforcement,
    onChanged: widget.onChanged,
    onFieldSubmitted: widget.onSubmitted,
    validator: widget.validator,
    autovalidateMode: widget.autovalidateMode,
  );
}

Future<String?> promptText(BuildContext context, String title, String value) =>
    showDialog<String>(
      context: context,
      builder: (_) => _TextPrompt(title, value),
    );

class _TextPrompt extends StatefulWidget {
  const _TextPrompt(this.title, this.value);
  final String title, value;
  @override
  State<_TextPrompt> createState() => _TextPromptState();
}

class _TextPromptState extends State<_TextPrompt> {
  late final controller = TextEditingController(text: widget.value);
  void submit() {
    final value = controller.text.trim();
    if (value.isNotEmpty) Navigator.pop(context, value);
  }

  @override
  void dispose() {
    controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: Text(widget.title),
    content: AppTextField(
      controller: controller,
      autofocus: true,
      maxLength: 80,
      textInputAction: TextInputAction.done,
      onSubmitted: (_) => submit(),
      decoration: const InputDecoration(labelText: '名称'),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('取消'),
      ),
      FilledButton(onPressed: submit, child: const Text('保存')),
    ],
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
    this.enabled = true,
    this.helperText,
    this.validator,
  });
  final String label, value;
  final ValueChanged<String> onChanged;
  final bool numeric;
  final int lines;
  final bool enabled;
  final String? helperText;
  final FormFieldValidator<String>? validator;
  @override
  State<EditField> createState() => _EditFieldState();
}

class _EditFieldState extends State<EditField> {
  late final controller = TextEditingController(text: widget.value);
  final focus = FocusNode();
  // A keyed ExpansionTile stores a bool in PageStorage. An unkeyed text
  // viewport inherits that same storage identifier and tries to restore it
  // as a double. Field scroll offsets are local to this editor's lifetime.
  final scroll = ScrollController(keepScrollOffset: false);
  @override
  void didUpdateWidget(covariant EditField oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!focus.hasFocus &&
        controller.value.composing.isCollapsed &&
        oldWidget.value != widget.value &&
        controller.text != widget.value) {
      controller.value = TextEditingValue(
        text: widget.value,
        selection: TextSelection.collapsed(offset: widget.value.length),
      );
    }
  }

  @override
  void dispose() {
    controller.dispose();
    focus.dispose();
    scroll.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => TextFormField(
    controller: controller,
    focusNode: focus,
    scrollController: scroll,
    enabled: widget.enabled,
    validator: widget.validator,
    autovalidateMode: AutovalidateMode.onUserInteraction,
    decoration: InputDecoration(
      labelText: widget.label,
      alignLabelWithHint: widget.lines > 1,
      helperText: widget.helperText,
      helperMaxLines: 3,
      errorMaxLines: 3,
    ),
    onChanged: widget.onChanged,
    maxLines: widget.lines,
    keyboardType: widget.numeric
        ? const TextInputType.numberWithOptions(decimal: true, signed: true)
        : widget.lines > 1
        ? TextInputType.multiline
        : TextInputType.text,
  );
}

/// Rebuild a selected value only when it changes, rather than on telemetry ticks.
class ModelSlice<T> extends StatefulWidget {
  const ModelSlice({
    super.key,
    required this.listenable,
    required this.select,
    required this.builder,
  });
  final Listenable listenable;
  final T Function() select;
  final Widget Function(BuildContext, Widget?) builder;
  @override
  State<ModelSlice<T>> createState() => _ModelSliceState<T>();
}

class _ModelSliceState<T> extends State<ModelSlice<T>> {
  late T value;
  @override
  void initState() {
    super.initState();
    value = widget.select();
    widget.listenable.addListener(update);
  }

  void update() {
    final next = widget.select();
    if (next != value && mounted) setState(() => value = next);
  }

  @override
  void didUpdateWidget(covariant ModelSlice<T> old) {
    super.didUpdateWidget(old);
    if (old.listenable != widget.listenable) {
      old.listenable.removeListener(update);
      widget.listenable.addListener(update);
    }
    value = widget.select();
  }

  @override
  void dispose() {
    widget.listenable.removeListener(update);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => widget.builder(context, null);
}

/// Hidden tabs and covered routes retain state without rebuilding on ticks.
class ActiveModelView extends StatefulWidget {
  const ActiveModelView({
    super.key,
    required this.listenable,
    required this.active,
    required this.builder,
    this.select,
  });
  final Object? Function()? select;
  final Listenable listenable;
  final bool active;
  final WidgetBuilder builder;
  @override
  State<ActiveModelView> createState() => _ActiveModelViewState();
}

class _ActiveModelViewState extends State<ActiveModelView> {
  bool current = true;
  Object? lastValue;
  @override
  void initState() {
    super.initState();
    lastValue = widget.select?.call();
    widget.listenable.addListener(update);
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    current = ModalRoute.isCurrentOf(context) ?? true;
  }

  void update() {
    final next = widget.select?.call();
    if (widget.select != null && next == lastValue) return;
    lastValue = next;
    if (widget.active && current && mounted) setState(() {});
  }

  @override
  void didUpdateWidget(covariant ActiveModelView old) {
    super.didUpdateWidget(old);
    if (old.listenable != widget.listenable) {
      old.listenable.removeListener(update);
      widget.listenable.addListener(update);
    }
  }

  @override
  void dispose() {
    widget.listenable.removeListener(update);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => widget.builder(context);
}

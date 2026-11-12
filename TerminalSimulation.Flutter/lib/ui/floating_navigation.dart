import 'dart:math' as math;
import 'dart:ui';
import 'package:flutter/material.dart';
import 'studio_geometry.dart';

const studioDestinations = [
  ('终端', Icons.directions_car_outlined),
  ('轨迹', Icons.route_outlined),
  ('工具', Icons.data_object),
  ('日志', Icons.receipt_long_outlined),
  ('设置', Icons.tune),
];

class FloatingStudioNavigation extends StatefulWidget {
  const FloatingStudioNavigation({
    super.key,
    required this.selected,
    required this.onSelected,
    required this.glass,
    this.vertical = false,
    this.extended = false,
  });
  final int selected;
  final ValueChanged<int> onSelected;
  final bool glass, vertical, extended;

  static double height(BuildContext context) =>
      72 + math.max(0, MediaQuery.textScalerOf(context).scale(12) - 12);

  @override
  State<FloatingStudioNavigation> createState() =>
      _FloatingStudioNavigationState();
}

class _FloatingStudioNavigationState extends State<FloatingStudioNavigation> {
  @override
  Widget build(BuildContext context) {
    final media = MediaQuery.of(context);
    final scheme = Theme.of(context).colorScheme;
    final duration = media.disableAnimations
        ? Duration.zero
        : const Duration(milliseconds: 220);
    final glass = widget.glass && !media.highContrast;
    return LayoutBuilder(
      builder: (context, box) {
        final scale = media.textScaler.scale(12) / 12;
        final iconOnly = !widget.vertical && box.maxWidth / 5 < 32 + 24 * scale;
        final items = [
          for (var i = 0; i < studioDestinations.length; i++)
            Semantics(
              selected: widget.selected == i,
              button: true,
              label: studioDestinations[i].$1,
              excludeSemantics: true,
              onTap: () => widget.onSelected(i),
              child: Tooltip(
                message: studioDestinations[i].$1,
                child: Material(
                  color: Colors.transparent,
                  child: InkWell(
                    borderRadius: StudioGeometry.navigationItemCorners,
                    onTap: () => widget.onSelected(i),
                    child: AnimatedContainer(
                      duration: duration,
                      curve: Curves.easeOutCubic,
                      constraints: const BoxConstraints(
                        minHeight: 48,
                        minWidth: 40,
                      ),
                      padding: EdgeInsets.symmetric(
                        horizontal: widget.extended ? 16 : 4,
                        vertical: 8,
                      ),
                      decoration: BoxDecoration(
                        color: widget.selected == i
                            ? scheme.primaryContainer.withValues(
                                alpha: glass ? .8 : 1,
                              )
                            : Colors.transparent,
                        borderRadius: StudioGeometry.navigationItemCorners,
                      ),
                      child: widget.extended
                          ? Row(
                              children: [
                                Icon(
                                  studioDestinations[i].$2,
                                  color: widget.selected == i
                                      ? scheme.onPrimaryContainer
                                      : scheme.onSurfaceVariant,
                                ),
                                const SizedBox(width: 12),
                                Expanded(child: Text(studioDestinations[i].$1)),
                              ],
                            )
                          : Column(
                              mainAxisSize: MainAxisSize.min,
                              children: [
                                Icon(
                                  studioDestinations[i].$2,
                                  size: 24,
                                  color: widget.selected == i
                                      ? scheme.onPrimaryContainer
                                      : scheme.onSurfaceVariant,
                                ),
                                if (!iconOnly) ...[
                                  const SizedBox(height: 4),
                                  Text(
                                    studioDestinations[i].$1,
                                    maxLines: 1,
                                    style: Theme.of(context)
                                        .textTheme
                                        .labelSmall
                                        ?.copyWith(
                                          color: widget.selected == i
                                              ? scheme.onPrimaryContainer
                                              : scheme.onSurfaceVariant,
                                          fontWeight: widget.selected == i
                                              ? FontWeight.w700
                                              : FontWeight.w500,
                                        ),
                                  ),
                                ],
                              ],
                            ),
                    ),
                  ),
                ),
              ),
            ),
        ];
        final content = Padding(
          padding: const EdgeInsets.all(StudioGeometry.navigationInset),
          child: widget.vertical
              ? SingleChildScrollView(
                  primary: false,
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      for (var i = 0; i < items.length; i++) ...[
                        if (i > 0) const SizedBox(height: 6),
                        SizedBox(width: double.infinity, child: items[i]),
                      ],
                    ],
                  ),
                )
              : Row(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [for (final item in items) Expanded(child: item)],
                ),
        );
        final fallback = DecoratedBox(
          decoration: BoxDecoration(
            borderRadius: StudioGeometry.floatingCorners,
            boxShadow: [
              BoxShadow(
                color: scheme.shadow.withValues(alpha: .14),
                blurRadius: 22,
                offset: const Offset(0, 6),
              ),
            ],
          ),
          child: ClipRRect(
            borderRadius: StudioGeometry.floatingCorners,
            child: BackdropFilter(
              enabled: glass,
              filter: ImageFilter.blur(sigmaX: 18, sigmaY: 18),
              child: DecoratedBox(
                decoration: BoxDecoration(
                  color: scheme.surfaceContainer.withValues(
                    alpha: glass ? .84 : 1,
                  ),
                  borderRadius: StudioGeometry.floatingCorners,
                  border: Border.all(
                    color: scheme.outlineVariant.withValues(
                      alpha: glass ? .55 : 1,
                    ),
                  ),
                ),
                child: content,
              ),
            ),
          ),
        );
        return fallback;
      },
    );
  }
}

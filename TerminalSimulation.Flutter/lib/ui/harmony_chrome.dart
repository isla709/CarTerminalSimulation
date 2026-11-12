import 'package:flutter/widgets.dart';

// The OHOS build selects the host-layer adapter, without PlatformViews.
class HarmonyChromeBridge extends StatelessWidget {
  const HarmonyChromeBridge({
    super.key,
    required this.headerKey,
    required this.bottomKey,
    required this.enabled,
    required this.bottomVisible,
    required this.compact,
    required this.selected,
    required this.status,
    required this.connected,
    required this.onSelected,
    required this.onReady,
  });
  final GlobalKey headerKey, bottomKey;
  final bool enabled, bottomVisible, compact, connected;
  final int selected;
  final String status;
  final ValueChanged<int> onSelected;
  // Native ownership, including temporary hiding during a child route.
  final ValueChanged<bool> onReady;
  @override
  Widget build(BuildContext context) => const SizedBox.shrink();
}

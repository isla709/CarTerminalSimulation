import 'dart:math' as math;
import 'package:flutter/material.dart';
import '../services/app_model.dart';
import '../services/terminal_session.dart' as terminal;
import 'components.dart';
import 'floating_navigation.dart';
import 'harmony_chrome.dart';
import 'log_page.dart';
import 'terminal_page.dart';
import 'tools_page.dart';
import 'route_page.dart';
import 'settings_page.dart';

class TerminalApp extends StatelessWidget {
  const TerminalApp({super.key, required this.model});
  final AppModel model;
  @override
  Widget build(BuildContext context) => ModelSlice<(String, bool, bool)>(
    listenable: model,
    select: () => (model.theme, model.glass, model.reduceMotion),
    builder: (context, _) => MaterialApp(
      debugShowCheckedModeBanner: false,
      themeAnimationDuration: model.reduceMotion
          ? Duration.zero
          : const Duration(milliseconds: 220),
      title: 'CTS · Terminal Studio',
      theme: _theme(Brightness.light),
      darkTheme: _theme(Brightness.dark),
      themeMode: switch (model.theme) {
        'light' => ThemeMode.light,
        'dark' => ThemeMode.dark,
        _ => ThemeMode.system,
      },
      builder: (context, child) {
        final media = MediaQuery.of(context);
        return MediaQuery(
          data: media.copyWith(
            disableAnimations: media.disableAnimations || model.reduceMotion,
          ),
          child: child!,
        );
      },
      home: StudioShell(model: model),
    ),
  );
  ThemeData _theme(Brightness brightness) {
    final colors = ColorScheme.fromSeed(
      seedColor: const Color(0xff087f8c),
      brightness: brightness,
    );
    return ThemeData(
      useMaterial3: true,
      fontFamily: 'Microsoft YaHei',
      colorScheme: colors,
      scaffoldBackgroundColor: brightness == Brightness.dark
          ? const Color(0xff10171c)
          : const Color(0xfff3f5f5),
      fontFamilyFallback: const [
        'Microsoft YaHei',
        'Noto Sans CJK SC',
        'sans-serif',
      ],
      cardTheme: CardThemeData(
        elevation: 0,
        color: colors.surface,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(22),
          side: BorderSide(color: colors.outlineVariant.withValues(alpha: .5)),
        ),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: colors.surfaceContainerLow,
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(12),
          borderSide: BorderSide.none,
        ),
        contentPadding: const EdgeInsets.symmetric(
          horizontal: 14,
          vertical: 16,
        ),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(minimumSize: const Size(48, 48)),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(minimumSize: const Size(48, 48)),
      ),
      visualDensity: VisualDensity.standard,
    );
  }
}

class StudioShell extends StatefulWidget {
  const StudioShell({super.key, required this.model});
  final AppModel model;
  @override
  State<StudioShell> createState() => _StudioShellState();
}

class _StudioShellState extends State<StudioShell>
    with SingleTickerProviderStateMixin {
  late final transition = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 220),
    value: 1,
  );
  late final opacity = Tween<double>(
    begin: .75,
    end: 1,
  ).animate(CurvedAnimation(parent: transition, curve: Curves.easeOutCubic));
  late final position = Tween<Offset>(
    begin: const Offset(0, .012),
    end: Offset.zero,
  ).animate(CurvedAnimation(parent: transition, curve: Curves.easeOutCubic));
  void select(int value) {
    settingsMenu.close();
    if (value == selected) return;
    FocusManager.instance.primaryFocus?.unfocus();
    setState(() => selected = value);
    if (MediaQuery.disableAnimationsOf(context)) {
      transition.value = 1;
    } else {
      transition.forward(from: 0);
    }
  }

  @override
  void dispose() {
    transition.dispose();
    super.dispose();
  }

  int selected = 0;
  bool nativeChromeReady = false;
  final headerKey = GlobalKey(), bottomKey = GlobalKey();
  final settingsMenu = MenuController();
  List<Widget> get pages => <Widget>[
    TerminalPage(model: widget.model),
    RoutePage(model: widget.model),
    ToolsPage(model: widget.model),
    LogPage(model: widget.model),
    SettingsPage(model: widget.model, themeMenu: settingsMenu),
  ];
  String get connectionLabel => switch (widget.model.session.state) {
    terminal.ConnectionState.connected => '已连接',
    terminal.ConnectionState.connecting => '连接中',
    _ => '未连接',
  };
  @override
  Widget build(BuildContext context) {
    final model = widget.model, scheme = Theme.of(context).colorScheme;
    return LayoutBuilder(
      builder: (context, constraints) {
        final compact = constraints.maxWidth < 600,
            wide = constraints.maxWidth >= 1024;
        final keyboard = MediaQuery.viewInsetsOf(context).bottom > 0;
        final headerHeight = math.max(
          56.0,
          MediaQuery.textScalerOf(context).scale(16) * 1.4 + 20,
        );
        final topInset = headerHeight + 24;
        final bottomVisible = compact && !keyboard;
        final bottomInset = bottomVisible
            ? FloatingStudioNavigation.height(context) + 24
            : 0.0;
        final railWidth = wide ? 180.0 : 84.0;
        final glass = model.glass && !MediaQuery.highContrastOf(context);
        return Scaffold(
          body: SafeArea(
            child: Stack(
              children: [
                Positioned.fill(
                  left: compact ? 0 : railWidth + 24,
                  child: MediaQuery(
                    data: MediaQuery.of(context).copyWith(
                      padding: EdgeInsets.only(
                        top: topInset,
                        bottom: bottomInset,
                      ),
                    ),
                    child: FadeTransition(
                      opacity: opacity,
                      child: SlideTransition(
                        position: position,
                        child: IndexedStack(
                          index: selected,
                          children: [
                            for (var i = 0; i < pages.length; i++)
                              ActiveModelView(
                                listenable: model,
                                active: selected == i,
                                select: i == 3
                                    ? () => model.logRevision
                                    : i == 2
                                    ? () => model.session.state
                                    : null,
                                builder: (_) => pages[i],
                              ),
                          ],
                        ),
                      ),
                    ),
                  ),
                ),
                Positioned(
                  left: 16,
                  right: 16,
                  top: 12,
                  child: SizedBox(
                    key: headerKey,
                    height: headerHeight,
                    child: IgnorePointer(
                      ignoring: nativeChromeReady,
                      child: Opacity(
                        opacity: nativeChromeReady ? 0 : 1,
                        child: GlassPanel(
                          enabled: glass,
                          padding: const EdgeInsets.symmetric(
                            horizontal: 16,
                            vertical: 10,
                          ),
                          child: ModelSlice<terminal.ConnectionState>(
                            listenable: model,
                            select: () => model.session.state,
                            builder: (context, _) => Row(
                              children: [
                                Image.asset(
                                  'assets/brand/app-icon.png',
                                  width: 36,
                                  height: 36,
                                  semanticLabel: 'CTS',
                                ),
                                const SizedBox(width: 12),
                                Expanded(
                                  child: Text(
                                    compact ? 'CTS' : 'CTS / Terminal Studio',
                                    style: const TextStyle(
                                      fontWeight: FontWeight.w700,
                                    ),
                                    overflow: TextOverflow.ellipsis,
                                  ),
                                ),
                                Icon(
                                  model.session.state ==
                                          terminal.ConnectionState.connected
                                      ? Icons.link
                                      : Icons.link_off,
                                  size: 18,
                                  color:
                                      model.session.state ==
                                          terminal.ConnectionState.connected
                                      ? scheme.primary
                                      : scheme.outline,
                                ),
                                const SizedBox(width: 8),
                                Flexible(
                                  child: Text(
                                    connectionLabel,
                                    overflow: TextOverflow.ellipsis,
                                    style: Theme.of(
                                      context,
                                    ).textTheme.labelMedium,
                                  ),
                                ),
                              ],
                            ),
                          ),
                        ),
                      ),
                    ),
                  ),
                ),
                Positioned(
                  left: 12,
                  top: topInset,
                  bottom: 12,
                  width: railWidth,
                  child: Offstage(
                    offstage: compact,
                    child: Align(
                      alignment: Alignment.topCenter,
                      child: FloatingStudioNavigation(
                        selected: selected,
                        onSelected: select,
                        glass: glass,
                        vertical: true,
                        extended: wide,
                      ),
                    ),
                  ),
                ),
                Positioned(
                  left: 12,
                  right: 12,
                  bottom: 12,
                  child: Offstage(
                    offstage: !bottomVisible,
                    child: Center(
                      child: ConstrainedBox(
                        constraints: const BoxConstraints(maxWidth: 560),
                        child: SizedBox(
                          key: bottomKey,
                          height: FloatingStudioNavigation.height(context),
                          child: IgnorePointer(
                            ignoring: nativeChromeReady,
                            child: Opacity(
                              opacity: nativeChromeReady ? 0 : 1,
                              child: FloatingStudioNavigation(
                                selected: selected,
                                onSelected: select,
                                glass: glass,
                              ),
                            ),
                          ),
                        ),
                      ),
                    ),
                  ),
                ),
                Positioned(
                  left: 0,
                  top: 0,
                  child: ModelSlice<terminal.ConnectionState>(
                    listenable: model,
                    select: () => model.session.state,
                    builder: (context, _) => HarmonyChromeBridge(
                      headerKey: headerKey,
                      bottomKey: bottomKey,
                      enabled: glass,
                      bottomVisible: bottomVisible,
                      compact: compact,
                      selected: selected,
                      status: connectionLabel,
                      connected:
                          model.session.state ==
                          terminal.ConnectionState.connected,
                      onSelected: select,
                      onReady: (value) {
                        if (mounted && value != nativeChromeReady) {
                          setState(() => nativeChromeReady = value);
                        }
                      },
                    ),
                  ),
                ),
              ],
            ),
          ),
        );
      },
    );
  }
}

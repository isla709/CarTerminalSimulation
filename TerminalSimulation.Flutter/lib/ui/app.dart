import 'package:flutter/material.dart';
import '../services/app_model.dart';
import '../services/terminal_session.dart' as terminal;
import 'components.dart';
import 'terminal_page.dart';
import 'tools_page.dart';
import 'route_page.dart';
import 'settings_page.dart';

class TerminalApp extends StatelessWidget {
  const TerminalApp({super.key, required this.model});
  final AppModel model;
  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: model,
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
    if (value == selected) return;
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
  static const destinations = [
    ('终端', Icons.directions_car_outlined),
    ('轨迹', Icons.route_outlined),
    ('工具', Icons.data_object),
    ('日志', Icons.receipt_long_outlined),
    ('设置', Icons.tune),
  ];
  List<Widget> get pages => <Widget>[
    TerminalPage(model: widget.model),
    RoutePage(model: widget.model),
    ToolsPage(model: widget.model),
    LogPage(model: widget.model),
    SettingsPage(model: widget.model),
  ];
  @override
  Widget build(BuildContext context) {
    final model = widget.model, scheme = Theme.of(context).colorScheme;
    return LayoutBuilder(
      builder: (context, constraints) {
        final compact = constraints.maxWidth < 600,
            wide = constraints.maxWidth >= 1024;
        return Scaffold(
          body: SafeArea(
            child: Column(
              children: [
                Padding(
                  padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
                  child: GlassPanel(
                    enabled: model.glass,
                    padding: const EdgeInsets.symmetric(
                      horizontal: 16,
                      vertical: 10,
                    ),
                    child: Row(
                      children: [
                        Container(
                          width: 36,
                          height: 36,
                          decoration: BoxDecoration(
                            color: scheme.primary,
                            borderRadius: BorderRadius.circular(12),
                          ),
                          child: Icon(
                            Icons.terminal,
                            color: scheme.onPrimary,
                            size: 21,
                          ),
                        ),
                        const SizedBox(width: 12),
                        Expanded(
                          child: Text(
                            compact ? 'CTS' : 'CTS / Terminal Studio',
                            style: const TextStyle(fontWeight: FontWeight.w700),
                            overflow: TextOverflow.ellipsis,
                          ),
                        ),
                        Icon(
                          model.session.state ==
                                  terminal.ConnectionState.connected
                              ? Icons.wifi
                              : Icons.wifi_off,
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
                            switch (model.session.state) {
                              terminal.ConnectionState.connected => '已连接',
                              terminal.ConnectionState.connecting => '连接中',
                              _ => '未连接',
                            },
                            overflow: TextOverflow.ellipsis,
                            style: Theme.of(context).textTheme.labelMedium,
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
                Expanded(
                  child: Row(
                    children: [
                      if (!compact)
                        ScrollableRail(
                          width: wide ? 180 : 80,
                          child: NavigationRail(
                            extended: wide,
                            minExtendedWidth: 180,
                            selectedIndex: selected,
                            onDestinationSelected: select,
                            labelType: wide
                                ? null
                                : NavigationRailLabelType.all,
                            destinations: destinations
                                .map(
                                  (d) => NavigationRailDestination(
                                    icon: Icon(d.$2),
                                    label: Text(d.$1),
                                  ),
                                )
                                .toList(),
                          ),
                        ),
                      Expanded(
                        child: FadeTransition(
                          opacity: opacity,
                          child: SlideTransition(
                            position: position,
                            child: IndexedStack(
                              index: selected,
                              children: pages,
                            ),
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
          bottomNavigationBar: compact
              ? NavigationBar(
                  selectedIndex: selected,
                  onDestinationSelected: select,
                  animationDuration: MediaQuery.disableAnimationsOf(context)
                      ? Duration.zero
                      : const Duration(milliseconds: 220),
                  destinations: destinations
                      .map(
                        (d) => NavigationDestination(
                          icon: Icon(d.$2),
                          label: d.$1,
                        ),
                      )
                      .toList(),
                )
              : null,
        );
      },
    );
  }
}

class LogPage extends StatefulWidget {
  const LogPage({super.key, required this.model});
  final AppModel model;
  @override
  State<LogPage> createState() => _LogPageState();
}

class _LogPageState extends State<LogPage> {
  String filter = '全部';
  @override
  Widget build(BuildContext context) {
    final logs = widget.model.logs
        .where((e) => filter == '全部' || e.category == filter)
        .toList();
    return Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Wrap(
            spacing: 12,
            runSpacing: 8,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              Text('通信与事件', style: Theme.of(context).textTheme.headlineSmall),
              DropdownButton<String>(
                value: filter,
                items: ['全部', '发送', '接收', '错误', '文本', '透传', '配置']
                    .map((e) => DropdownMenuItem(value: e, child: Text(e)))
                    .toList(),
                onChanged: (value) => setState(() => filter = value!),
              ),
              TextButton.icon(
                onPressed: () {
                  widget.model.logs.clear();
                  widget.model.changed();
                },
                icon: const Icon(Icons.clear_all),
                label: const Text('清空全部'),
              ),
            ],
          ),
          const SizedBox(height: 12),
          Expanded(
            child: logs.isEmpty
                ? const Center(child: Text('连接、发送和下行事件将在这里显示'))
                : ListView.builder(
                    itemCount: logs.length,
                    itemBuilder: (context, index) {
                      final entry = logs[index];
                      return Padding(
                        padding: const EdgeInsets.symmetric(vertical: 7),
                        child: SelectableText(
                          '${entry.time.toIso8601String().substring(11, 23)}  ${entry.category}\n${entry.message}',
                          style: TextStyle(
                            fontFamily: 'monospace',
                            fontSize: 12,
                            color: entry.category == '错误'
                                ? Theme.of(context).colorScheme.error
                                : null,
                          ),
                        ),
                      );
                    },
                  ),
          ),
        ],
      ),
    );
  }
}

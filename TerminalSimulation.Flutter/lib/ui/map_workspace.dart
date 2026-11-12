import 'dart:async';
import 'dart:io';
import 'dart:math' as math;
import 'package:flutter/material.dart';
import 'package:flutter/foundation.dart';
import 'package:webview_cef/webview_cef.dart' as cef;
import 'package:webview_flutter/webview_flutter.dart' as mobile;
import '../core/route.dart';
import '../services/app_model.dart';
import '../services/map_assets.dart';
import '../services/map_workspace_controller.dart';
import 'map_control_panel.dart';
import 'map_webview.dart';

class MapWorkspace extends StatefulWidget {
  const MapWorkspace({
    super.key,
    required this.model,
    required this.openRoute,
    required this.saveRoute,
  });
  final AppModel model;
  final Future<RouteDocument?> Function() openRoute;
  final Future<bool> Function(RouteDocument) saveRoute;
  @override
  State<MapWorkspace> createState() => _MapWorkspaceState();
}

class _MapWorkspaceState extends State<MapWorkspace> {
  static Future<void>? _desktopReady;
  late final assets = MapAssets(
    javascriptKey: widget.model.mapCredentials.javascriptKey,
  );
  late final bridge = MapWorkspaceController(
    model: widget.model,
    token: assets.token,
    execute: execute,
  );
  cef.WebViewController? desktop;
  mobile.WebViewController? handheld;
  bool trusted = false, loaded = false;
  String? error;
  String _appearance = '';
  Timer? _loadingTimeout;
  bool _timedOut = false;

  void armLoadingTimeout() {
    _loadingTimeout?.cancel();
    _timedOut = false;
    _loadingTimeout = Timer(const Duration(seconds: 40), () {
      if (!mounted || bridge.ready || bridge.renderError != null) return;
      setState(() {
        _timedOut = true;
        error = '地图加载超时，请确认设备已联网后重新载入。';
      });
    });
  }

  void mapProgressChanged() {
    if (bridge.renderError != null) _loadingTimeout?.cancel();
    if (!bridge.ready) return;
    _loadingTimeout?.cancel();
    if (_timedOut && mounted) {
      setState(() {
        _timedOut = false;
        error = null;
      });
    }
  }

  void trace(String stage) {
    if (kDebugMode && const bool.fromEnvironment('CTS_MAP_WEB_DEBUG')) {
      debugPrint('CTS_MAP_STAGE $stage');
    }
  }

  @override
  void initState() {
    super.initState();
    bridge.addListener(mapProgressChanged);
    armLoadingTimeout();
    unawaited(bridge.initialize());
    unawaited(initialize());
  }

  Future<void> execute(String script) async {
    if (!mounted || !trusted) return;
    if (desktop != null) {
      await desktop!.executeJavaScript(script);
    } else {
      await handheld?.runJavaScript(script);
    }
  }

  Future<void> appearance() async {
    if (!mounted || !loaded) return;
    await bridge.appearance(
      reduceMotion: MediaQuery.disableAnimationsOf(context),
      dark: Theme.of(context).brightness == Brightness.dark,
    );
  }

  Future<void> initialize() async {
    try {
      final url = await assets.start();
      if (!mounted) return;
      if (Platform.isWindows || Platform.isLinux) {
        _desktopReady ??= cef.WebviewManager().initialize(
          userAgent: 'CTS/Flutter-0.1.0',
        );
        try {
          await _desktopReady;
        } catch (_) {
          _desktopReady = null;
          rethrow;
        }
        if (!mounted) return;
        final controller = cef.WebviewManager().createWebView(
          loading: const Center(child: CircularProgressIndicator()),
        );
        desktop = controller;
        controller.setWebviewListener(
          cef.WebviewEventsListener(
            onLoadStart: (_, url) {
              if (assets.owns(url)) {
                armLoadingTimeout();
                bridge.documentLoading();
              }
            },
            onUrlChanged: (value) {
              trusted = assets.owns(value);
              if (!trusted && value != 'about:blank') {
                unawaited(controller.loadUrl('about:blank'));
              }
            },
            onLoadEnd: (_, value) {
              if (!assets.owns(value) || !mounted) return;
              trusted = true;
              unawaited(
                controller
                    .setJavaScriptChannels({
                      cef.JavascriptChannel(
                        name: 'CtsMap',
                        onMessageReceived: (message) {
                          if (trusted) {
                            unawaited(bridge.receive(message.message));
                          }
                          unawaited(
                            controller
                                .sendJavaScriptChannelCallBack(
                                  false,
                                  'null',
                                  message.callbackId,
                                  message.frameId,
                                )
                                .catchError((Object _) {}),
                          );
                        },
                      ),
                    })
                    .then((_) async {
                      if (!mounted) return;
                      setState(() => loaded = true);
                      await appearance();
                    })
                    .catchError((Object _) {
                      fail();
                    }),
              );
            },
          ),
        );
        await controller.initialize('about:blank');
        if (!mounted) return;
        await controller.loadUrl(url.toString());
      } else {
        final controller = mobile.WebViewController();
        await configureMapWebView(controller);
        trace('platform-configured');
        await controller.setJavaScriptMode(mobile.JavaScriptMode.unrestricted);
        trace('javascript-configured');
        await controller.addJavaScriptChannel(
          'CtsMap',
          onMessageReceived: (message) {
            if (trusted) unawaited(bridge.receive(message.message));
          },
        );
        await controller.setNavigationDelegate(
          mobile.NavigationDelegate(
            onNavigationRequest: (request) =>
                !request.isMainFrame || assets.owns(request.url)
                ? mobile.NavigationDecision.navigate
                : mobile.NavigationDecision.prevent,
            onPageStarted: (value) {
              trace('page-started');
              armLoadingTimeout();
              trusted = assets.owns(value);
              bridge.documentLoading();
            },
            onPageFinished: (value) {
              trace('page-finished');
              if (!mounted || !assets.owns(value)) return;
              trusted = true;
              setState(() => loaded = true);
              unawaited(appearance());
            },
            onWebResourceError: (value) {
              if (value.isForMainFrame == true) fail();
            },
          ),
        );
        if (!mounted) return;
        // ArkWeb reads JavaScript settings when its PlatformView is created.
        // Configure first, then attach the view before awaiting loadRequest.
        setState(() => handheld = controller);
        trace('controller-published');
        trace('request-started');
        await controller.loadRequest(url);
        trace('request-dispatched');
      }
      if (mounted) setState(() {});
    } catch (_) {
      fail();
    }
  }

  void fail() {
    _loadingTimeout?.cancel();
    if (mounted) setState(() => error = '地图加载失败，请检查网络、WebView 运行环境和地图凭据。');
  }

  @override
  void dispose() {
    trusted = false;
    _loadingTimeout?.cancel();
    bridge.removeListener(mapProgressChanged);
    bridge.dispose();
    unawaited(assets.close());
    if (desktop != null) {
      unawaited(desktop!.dispose().catchError((Object _) {}));
    }
    super.dispose();
  }

  final _canvasKey = GlobalKey(), _panelKey = GlobalKey();
  double panelFraction = .50;
  bool collapsed = false;
  bool draggingPanel = false;
  void openPanel() {
    setState(() {
      collapsed = false;
      panelFraction = math.max(.5, panelFraction);
    });
  }

  void togglePanel() {
    if (collapsed) {
      openPanel();
    } else {
      FocusManager.instance.primaryFocus?.unfocus();
      setState(() => collapsed = true);
    }
  }

  void reload() {
    armLoadingTimeout();
    bridge.documentLoading();
    setState(() {
      loaded = false;
      error = null;
    });
    if (desktop != null) {
      unawaited(desktop!.reload());
    } else if (handheld != null) {
      unawaited(handheld!.reload());
    } else {
      unawaited(initialize());
    }
  }

  Widget canvas() => KeyedSubtree(
    key: _canvasKey,
    child: Stack(
      fit: StackFit.expand,
      children: [
        if (desktop != null)
          ValueListenableBuilder<bool>(
            valueListenable: desktop!,
            builder: (_, ready, _) =>
                ready ? desktop!.webviewWidget : desktop!.loadingWidget,
          )
        else if (handheld != null)
          mapWebView(handheld!),
        if (error != null || (!bridge.ready && bridge.renderError != null))
          Center(
            child: SingleChildScrollView(
              padding: const EdgeInsets.all(12),
              child: Card(
                child: Padding(
                  padding: const EdgeInsets.all(16),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      const Text('地图暂不可用'),
                      if (error != null || bridge.renderError != null) ...[
                        const SizedBox(height: 8),
                        Text(
                          error ?? bridge.renderError!,
                          textAlign: TextAlign.center,
                        ),
                      ],
                      const SizedBox(height: 8),
                      FilledButton.icon(
                        onPressed: reload,
                        icon: const Icon(Icons.refresh),
                        label: const Text('重新载入'),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          )
        else if (!loaded || !bridge.ready)
          const Center(child: CircularProgressIndicator()),
        if (bridge.ready) ...[
          Positioned(
            top: 12,
            left: 12,
            child: Material(
              elevation: 2,
              borderRadius: BorderRadius.circular(16),
              color: Theme.of(context).colorScheme.surfaceContainerHigh,
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  if (collapsed)
                    IconButton(
                      tooltip: '展开操作面板',
                      onPressed: openPanel,
                      icon: const Icon(Icons.tune),
                    ),
                  IconButton(
                    tooltip: widget.model.realLocation ? '设备当前位置' : '网络近似定位',
                    onPressed: bridge.locating ? null : () => bridge.locate(),
                    icon: const Icon(Icons.my_location),
                  ),
                  IconButton(
                    tooltip: widget.model.realLocation ? '跟随设备位置' : '恢复车辆跟随',
                    onPressed: bridge.resumeFollow,
                    icon: const Icon(Icons.navigation_outlined),
                  ),
                  IconButton(
                    tooltip: '查看整条路线',
                    onPressed:
                        widget.model.realLocation || bridge.points.isEmpty
                        ? null
                        : bridge.fit,
                    icon: const Icon(Icons.fit_screen),
                  ),
                ],
              ),
            ),
          ),
        ],
      ],
    ),
  );

  // Keep app controls outside the SDK viewport so its compass/zoom controls
  // remain reachable at every map height and device pixel ratio.
  Widget viewToggle() => AnimatedBuilder(
    animation: bridge,
    builder: (context, _) => Padding(
      padding: const EdgeInsets.symmetric(horizontal: 4),
      child: Material(
        color: Theme.of(context).colorScheme.surfaceContainerHigh,
        borderRadius: BorderRadius.circular(16),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            for (final use3d in [false, true])
              IconButton(
                tooltip: use3d ? '三维视图' : '二维视图',
                isSelected: bridge.use3d == use3d,
                style: IconButton.styleFrom(
                  minimumSize: const Size(48, 48),
                  backgroundColor: bridge.use3d == use3d
                      ? Theme.of(context).colorScheme.primaryContainer
                      : null,
                ),
                onPressed: !bridge.ready
                    ? null
                    : () {
                        bridge.use3d = use3d;
                        bridge.changed(geometry: false);
                      },
                icon: Text(use3d ? '3D' : '2D'),
              ),
          ],
        ),
      ),
    ),
  );

  @override
  Widget build(BuildContext context) {
    final style =
        '${MediaQuery.disableAnimationsOf(context)}:${Theme.of(context).brightness}';
    if (style != _appearance) {
      _appearance = style;
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) unawaited(appearance());
      });
    }
    return Scaffold(
      backgroundColor: Theme.of(context).colorScheme.surface,
      appBar: AppBar(
        title: const Text(
          '地图工作台',
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
        ),
        actions: [
          viewToggle(),
          IconButton(
            tooltip: '重新载入',
            onPressed: desktop != null || handheld != null || error != null
                ? reload
                : null,
            icon: const Icon(Icons.refresh),
          ),
        ],
      ),
      body: SafeArea(
        child: AnimatedBuilder(
          animation: bridge,
          builder: (context, _) => LayoutBuilder(
            builder: (context, box) {
              final screen = MediaQuery.sizeOf(context);
              final vertical =
                  box.maxWidth < 600 ||
                  box.maxWidth < 900 && screen.height > screen.width;
              final panel = MapControlPanel(
                key: _panelKey,
                controller: bridge,
                openRoute: widget.openRoute,
                saveRoute: widget.saveRoute,
                collapsed: collapsed,
                onToggle: togglePanel,
                onDrag: vertical
                    ? (delta) {
                        setState(() {
                          draggingPanel = true;
                          collapsed = false;
                          panelFraction =
                              (panelFraction - delta / box.maxHeight).clamp(
                                .22,
                                .82,
                              );
                        });
                      }
                    : null,
                onDragEnd: vertical
                    ? () {
                        setState(() {
                          draggingPanel = false;
                          final sizes = [.22, .50, .82];
                          sizes.sort(
                            (a, b) => (panelFraction - a).abs().compareTo(
                              (panelFraction - b).abs(),
                            ),
                          );
                          panelFraction = sizes.first;
                          collapsed = panelFraction == .22;
                        });
                      }
                    : null,
              );
              final keyboard = MediaQuery.viewInsetsOf(context).bottom > 0;
              final scale = MediaQuery.textScalerOf(context).scale(14) / 14;
              final minimum = 144.0 + 24 * (scale - 1).clamp(0.0, 2.0);
              final coverMap = vertical && (keyboard || box.maxHeight < 280);
              final desired = collapsed
                  ? minimum
                  : math.max(minimum, box.maxHeight * panelFraction);
              final panelHeight = vertical && !coverMap
                  ? math.max(
                      0.0,
                      math.min(math.max(0.0, box.maxHeight - 48), desired),
                    )
                  : box.maxHeight;
              final panelWidth = vertical
                  ? box.maxWidth
                  : math.min(400.0, math.max(320.0, box.maxWidth * .36));
              final duration =
                  keyboard ||
                      draggingPanel ||
                      MediaQuery.disableAnimationsOf(context)
                  ? Duration.zero
                  : const Duration(milliseconds: 220);
              // Keep both editor and WebView at stable positions in the tree.
              // IME insets and responsive breakpoints only change geometry.
              return Stack(
                children: [
                  AnimatedPositioned(
                    duration: duration,
                    curve: Curves.easeOutCubic,
                    left: 0,
                    top: 0,
                    right: !vertical && !collapsed ? panelWidth : 0,
                    bottom: vertical && !coverMap ? panelHeight : 0,
                    child: canvas(),
                  ),
                  AnimatedPositioned(
                    duration: duration,
                    curve: Curves.easeOutCubic,
                    right: 0,
                    bottom: 0,
                    width: panelWidth,
                    height: panelHeight,
                    child: Offstage(
                      offstage: !vertical && collapsed,
                      child: DecoratedBox(
                        decoration: BoxDecoration(
                          border: Border(
                            left: BorderSide(
                              color: vertical
                                  ? Colors.transparent
                                  : Theme.of(context).dividerColor,
                            ),
                          ),
                        ),
                        child: Listener(
                          behavior: HitTestBehavior.opaque,
                          child: panel,
                        ),
                      ),
                    ),
                  ),
                ],
              );
            },
          ),
        ),
      ),
    );
  }
}

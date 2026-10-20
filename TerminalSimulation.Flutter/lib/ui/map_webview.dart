import 'package:flutter/widgets.dart';
import 'package:webview_flutter/webview_flutter.dart';

Future<void> configureMapWebView(WebViewController controller) async {}

Widget mapWebView(WebViewController controller) =>
    WebViewWidget(controller: controller);

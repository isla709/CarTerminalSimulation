import 'dart:async';
import 'package:flutter/foundation.dart';
import 'package:flutter/scheduler.dart';

/// Opt-in local diagnostics. No URLs, protocol data or credentials are recorded.
class FrameDiagnostics {
  static void start() {
    if (!kDebugMode || !const bool.fromEnvironment('CTS_FRAME_TIMINGS')) return;
    final build = <int>[], raster = <int>[], lag = <int>[];
    final clock = Stopwatch()..start();
    var expected = 20000;
    Timer.periodic(const Duration(milliseconds: 20), (_) {
      final now = clock.elapsedMicroseconds;
      lag.add((now - expected).clamp(0, 1 << 62));
      expected = now + 20000;
    });
    SchedulerBinding.instance.addTimingsCallback((frames) {
      for (final frame in frames) {
        build.add(frame.buildDuration.inMicroseconds);
        raster.add(frame.rasterDuration.inMicroseconds);
      }
    });
    double percentile(List<int> samples, double p) {
      if (samples.isEmpty) return 0;
      final sorted = List<int>.of(samples)..sort();
      return sorted[((sorted.length - 1) * p).round()] / 1000;
    }

    Timer.periodic(const Duration(seconds: 5), (_) {
      debugPrint(
        'CTS_FRAME frames=${build.length} buildP95Ms=${percentile(build, .95).toStringAsFixed(2)} rasterP95Ms=${percentile(raster, .95).toStringAsFixed(2)} loopLagP95Ms=${percentile(lag, .95).toStringAsFixed(2)} loopLagMaxMs=${percentile(lag, 1).toStringAsFixed(2)}',
      );
      build.clear();
      raster.clear();
      lag.clear();
    });
  }
}

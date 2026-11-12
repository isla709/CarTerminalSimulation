import 'dart:async';
import 'dart:io';
import 'package:flutter/services.dart';

enum DeviceLocationSource {
  gnss,
  rtk,
  network,
  indoor,
  fused,
  unknown;

  bool get satellite => this == gnss || this == rtk;
  String get label => switch (this) {
    gnss => 'GNSS 卫星定位',
    rtk => 'GNSS RTK 定位',
    network => '网络定位',
    indoor => '室内定位',
    fused => '系统融合定位',
    unknown => '系统定位（来源未提供）',
  };
  String get detail => switch (this) {
    gnss || rtk => '系统确认当前位置来自卫星定位。',
    network => '可能使用 Wi-Fi、基站 LBS 等，系统未细分本次来源。',
    indoor => '系统标记为室内定位，未提供具体定位技术。',
    fused => '由系统综合可用信号，未公开本次 Wi-Fi、LBS 或 GNSS 的参与情况。',
    unknown => '系统未提供定位来源，不根据精度或网络连接状态推断。',
  };
}

class DeviceLocationFix {
  DeviceLocationFix(Map<Object?, Object?> data)
    : latitude = (data['latitude'] as num).toDouble(),
      longitude = (data['longitude'] as num).toDouble(),
      altitude = (data['altitude'] as num?)?.toDouble(),
      speedMps = (data['speed'] as num?)?.toDouble(),
      bearing = (data['bearing'] as num?)?.toDouble(),
      accuracy = (data['accuracy'] as num).toDouble(),
      time = DateTime.fromMillisecondsSinceEpoch((data['time'] as num).toInt()),
      initialAge = (data['ageMs'] as num?)?.toInt() ?? 0,
      source = DeviceLocationSource.values.firstWhere(
        (source) => source.name == data['source'],
        orElse: () => DeviceLocationSource.unknown,
      ),
      mock = data['mock'] == true,
      _age = (Stopwatch()..start());
  final double latitude, longitude, accuracy;
  final double? altitude, speedMps, bearing;
  final DateTime time;
  final int initialAge;
  final DeviceLocationSource source;
  final bool mock;
  final Stopwatch _age;
  int get ageMilliseconds => initialAge + _age.elapsedMilliseconds;
  bool get valid => rejectionReason == null;
  String? get rejectionReason {
    if (mock) return '系统返回模拟位置，等待真实设备位置';
    if (initialAge < 0 ||
        ageMilliseconds > 30000 ||
        DateTime.now().difference(time).inMilliseconds.abs() > 60000) {
      return '定位数据已过期，等待新位置';
    }
    if (!latitude.isFinite ||
        !longitude.isFinite ||
        latitude.abs() > 90 ||
        longitude.abs() > 180 ||
        !accuracy.isFinite ||
        accuracy < 0 ||
        (altitude != null && !altitude!.isFinite) ||
        (speedMps != null && (!speedMps!.isFinite || speedMps! < 0)) ||
        (bearing != null && !bearing!.isFinite) ||
        time.year < 2020) {
      return '系统返回的位置数据无效，等待新位置';
    }
    return null;
  }

  String get quality => accuracy == 0
      ? '精度未提供'
      : accuracy <= 50
      ? '精度较好'
      : accuracy <= 200
      ? '精度一般'
      : '粗略位置';
}

/// A process-wide channel: independent from routes, map WebViews and widgets.
class MobileRuntime {
  static bool get supported =>
      Platform.isAndroid || Platform.operatingSystem == 'ohos';
  static bool get backgroundSettingsSupported => Platform.isAndroid;
  final _channel = const MethodChannel('cts/runtime');
  void Function(Map<Object?, Object?>)? onEvent;
  Future<void> _tail = Future.value();
  bool _closed = false;
  MobileRuntime() {
    if (!supported) return;
    _channel.setMethodCallHandler((call) async {
      if (!_closed && call.method == 'event' && call.arguments is Map) {
        onEvent?.call(Map<Object?, Object?>.from(call.arguments as Map));
      }
    });
    // A debug hot restart must not inherit native tasks from the prior isolate.
    unawaited(
      configure(connection: false, location: false).catchError((Object _) {}),
    );
  }
  Future<void> configure({required bool connection, required bool location}) {
    if (!supported || _closed) return Future.value();
    final work = _tail.then((_) async {
      if (_closed) return;
      await _channel.invokeMethod<void>('configure', {
        'connection': connection,
        'location': location,
      });
    });
    _tail = work.then<void>((_) {}, onError: (Object _, StackTrace _) {});
    return work;
  }

  Future<void> dispose() async {
    if (supported) {
      await configure(connection: false, location: false);
      _channel.setMethodCallHandler(null);
    }
    _closed = true;
  }

  Future<void> openBackgroundSettings() =>
      _channel.invokeMethod<void>('backgroundSettings');
}

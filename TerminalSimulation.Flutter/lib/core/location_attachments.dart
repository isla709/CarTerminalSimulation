import 'dart:math' as math;
import 'dart:typed_data';
import 'bytes.dart';

class LocationAttachmentSpec {
  const LocationAttachmentSpec(
    this.id,
    this.name,
    this.key,
    this.width,
    this.unit,
    this.initial, {
    this.enabled = false,
  });
  final int id, width, initial;
  final String name, key, unit;
  final bool enabled;
  String get hexId => id.toRadixString(16).toUpperCase().padLeft(2, '0');
  int get maximum => (1 << (width * 8)) - 1;
}

const locationAttachmentSpecs = [
  LocationAttachmentSpec(
    0x01,
    '里程',
    'Mileage0x01',
    4,
    '0.1 km',
    0,
    enabled: true,
  ),
  LocationAttachmentSpec(0x02, '油量', 'Oil0x02', 2, '0.1 L', 0),
  LocationAttachmentSpec(
    0x03,
    '行驶记录速度',
    'Speed0x03',
    2,
    '0.1 km/h',
    0,
    enabled: true,
  ),
  LocationAttachmentSpec(0x04, '报警事件 ID', 'AlarmEventId0x04', 2, '整数', 0),
  LocationAttachmentSpec(
    0x25,
    '扩展车辆信号',
    'ExtVehicleSignal0x25',
    4,
    '位标志 · 十进制',
    0,
  ),
  LocationAttachmentSpec(0x2a, 'IO 状态', 'IOStatus0x2A', 2, '位标志 · 十进制', 0),
  LocationAttachmentSpec(
    0x2b,
    '模拟量',
    'Analog',
    4,
    'AD1 高 16 位 / AD0 低 16 位',
    0,
  ),
  LocationAttachmentSpec(
    0x30,
    '网络信号强度',
    'NetworkSignal0x30',
    1,
    '0–255；波动时 0–31',
    31,
    enabled: true,
  ),
  LocationAttachmentSpec(
    0x31,
    'GNSS 卫星数',
    'GNSSCount0x31',
    1,
    '0–255；波动时 0–30',
    15,
    enabled: true,
  ),
];

const vehicleSignalNames = [
  '近光灯',
  '远光灯',
  '右转向灯',
  '左转向灯',
  '制动',
  '倒挡',
  '雾灯',
  '示廓灯',
  '喇叭',
  '空调',
  '空挡',
  '缓速器工作',
  'ABS 工作',
  '加热器工作',
  '离合器状态',
];
final ioStatusNames = ['深度休眠', '休眠', for (var i = 2; i < 16; i++) 'GPIO$i'];

/// Deliberately keeps three independent HEX fields, including an explicit length.
/// WPF RAW injection permits duplicate IDs and deliberately inconsistent TLVs.
class RawLocationAttachment {
  RawLocationAttachment({this.id = '', this.length = '', this.data = ''});
  String id, length, data;
  Map<String, dynamic> toJson() => {
    'AttachId': id,
    'AttachLength': length,
    'AttachData': data,
  };
  Uint8List encode() => Uint8List.fromList([
    ..._rawHex(id),
    ..._rawHex(length),
    ..._rawHex(data),
  ]);
  static Uint8List _rawHex(String value) {
    if (value.length > 4096) throw const FormatException('单个 RAW 字段过长');
    return unhex(value.replaceAll(RegExp(r'[\s|\-]'), ''));
  }
}

/// WPF-compatible standard attachment settings and per-second simulation.
/// Draft numbers stay as text so an incomplete edit never silently becomes zero.
class LocationAttachments {
  final enabled = {for (final s in locationAttachmentSpecs) s.id: s.enabled};
  final values = {
    for (final s in locationAttachmentSpecs) s.key: '${s.initial}',
    'AnalogAD0': '0',
    'AnalogAD1': '0',
  };
  final List<RawLocationAttachment> custom = [];
  bool mileageSimulation = false,
      syncSpeed = true,
      oilConsumption = false,
      networkFluctuation = false,
      gnssFluctuation = false;
  String consumptionRate = '8';
  double _mileageRemainder = 0, _oilRemainder = 0;
  int get enabledCount => enabled.values.where((v) => v).length;
  bool get simulating =>
      mileageSimulation ||
      oilConsumption ||
      syncSpeed ||
      networkFluctuation ||
      gnssFluctuation;

  static int number(String text, int maximum, String label) {
    final n = int.tryParse(text.trim());
    if (n == null || n < 0 || n > maximum) {
      throw FormatException('$label 必须为 0–$maximum 的整数');
    }
    return n;
  }

  double _rate() {
    final rate = double.tryParse(consumptionRate);
    if (rate == null || !rate.isFinite || rate < 0 || rate > 1000) {
      throw const FormatException('百公里油耗必须为 0–1000 L/100km');
    }
    return rate;
  }

  Uint8List encode(
    double mainSpeed, {
    bool systemLocation = false,
    int? gnssCount,
  }) {
    final w = ByteWriter();
    var length = 0;
    for (final s in locationAttachmentSpecs) {
      if (enabled[s.id] != true) continue;
      if (s.id == 0x31 && systemLocation && gnssCount == null) continue;
      length += 2 + s.width;
      w.u8(s.id);
      w.u8(s.width);
      if (s.id == 0x2b) {
        // Preserve WPF ordering: AD1 first, then AD0, both big endian.
        w.u16(number(values['AnalogAD1']!, 65535, 'AD1'));
        w.u16(number(values['AnalogAD0']!, 65535, 'AD0'));
      } else {
        final n = s.id == 0x31 && systemLocation
            ? gnssCount!.clamp(0, 255)
            : s.id == 3 && syncSpeed
            ? _speed(mainSpeed)
            : number(values[s.key]!, s.maximum, s.name);
        switch (s.width) {
          case 4:
            w.u32(n);
          case 2:
            w.u16(n);
          default:
            w.u8(n);
        }
      }
    }
    if (custom.length > 128) {
      throw const FormatException('自定义附加项最多 128 项');
    }
    for (var i = 0; i < custom.length; i++) {
      try {
        final bytes = custom[i].encode();
        length += bytes.length;
        if (length > 995) {
          throw const FormatException('附加数据超过 995 字节，位置消息体最大 1023 字节');
        }
        w.add(bytes);
      } on FormatException catch (e) {
        throw FormatException('自定义附加项 ${i + 1}：${e.message}');
      }
    }
    final result = w.take();
    if (result.length > 995) {
      throw const FormatException('附加数据超过 995 字节，位置消息体最大 1023 字节');
    }
    return result;
  }

  static int _speed(double speed) {
    if (!speed.isFinite || speed < 0 || speed > 6553.5) {
      throw const FormatException('主速度必须为 0–6553.5 km/h');
    }
    return (speed * 10).truncate();
  }

  /// Exactly one logical second per callback, matching WPF (no catch-up bursts).
  bool tick(double speed, math.Random random) {
    var changed = false;
    void update(String key, int n) {
      if (values[key] != '$n') {
        values[key] = '$n';
        changed = true;
      }
    }

    if (speed.isFinite && speed >= 0 && speed <= 6553.5) {
      if (syncSpeed) update('Speed0x03', _speed(speed));
      final mileage = int.tryParse(values['Mileage0x01']!);
      if (mileageSimulation &&
          mileage != null &&
          mileage >= 0 &&
          mileage <= 0xffffffff) {
        _mileageRemainder += speed / 3600 * 10;
        final added = _mileageRemainder.floor();
        if (added > 0) {
          update('Mileage0x01', math.min(0xffffffff, mileage + added));
          _mileageRemainder -= added;
        }
      }
      final oil = int.tryParse(values['Oil0x02']!),
          rate = double.tryParse(consumptionRate);
      if (oilConsumption &&
          oil != null &&
          oil >= 0 &&
          oil <= 65535 &&
          rate != null &&
          rate.isFinite &&
          rate >= 0 &&
          rate <= 1000) {
        _oilRemainder += speed * rate / 100 / 3600 * 10;
        final consumed = _oilRemainder.floor();
        if (consumed > 0) {
          update('Oil0x02', math.max(0, oil - consumed));
          _oilRemainder -= consumed;
        }
      }
    }
    void fluctuate(String key, int maximum, bool active) {
      final n = int.tryParse(values[key]!);
      if (active &&
          n != null &&
          n >= 0 &&
          n <= 255 &&
          random.nextDouble() < .3) {
        update(key, (n + random.nextInt(3) - 1).clamp(0, maximum));
      }
    }

    fluctuate('NetworkSignal0x30', 31, networkFluctuation);
    fluctuate('GNSSCount0x31', 30, gnssFluctuation);
    return changed;
  }

  Map<String, dynamic> toJson() {
    // Save all values (including disabled items) with WPF property names.
    final result = <String, dynamic>{};
    for (final s in locationAttachmentSpecs) {
      result['Enable0x${s.hexId}'] = enabled[s.id];
      if (s.id != 0x2b) {
        result[s.key] = number(values[s.key]!, s.maximum, s.name);
      }
    }
    result.addAll({
      'AnalogAD0': number(values['AnalogAD0']!, 65535, 'AD0'),
      'AnalogAD1': number(values['AnalogAD1']!, 65535, 'AD1'),
      'EnableMileageSimulation': mileageSimulation,
      'Sync0x03SpeedWithMainSpeed': syncSpeed,
      'EnableOilConsumption': oilConsumption,
      'OilConsumptionRate': _rate(),
      'EnableNetworkSignalFluctuation': networkFluctuation,
      'EnableGNSSFluctuation': gnssFluctuation,
      'CustomAttachItems': custom.map((v) => v.toJson()).toList(),
    });
    return result;
  }

  static LocationAttachments fromJson(Map<String, dynamic> input) {
    final a = LocationAttachments();
    final data = {for (final e in input.entries) e.key.toLowerCase(): e.value};
    bool flag(String key, bool fallback) {
      final value = data[key.toLowerCase()];
      if (value == null) return fallback;
      if (value is! bool) throw FormatException('$key 必须为布尔值');
      return value;
    }

    for (final s in locationAttachmentSpecs) {
      a.enabled[s.id] = flag('Enable0x${s.hexId}', s.enabled);
      if (data.containsKey(s.key.toLowerCase())) {
        a.values[s.key] = '${data[s.key.toLowerCase()]}';
      }
    }
    for (final key in ['AnalogAD0', 'AnalogAD1']) {
      if (data.containsKey(key.toLowerCase())) {
        a.values[key] = '${data[key.toLowerCase()]}';
      }
    }
    a.mileageSimulation = flag('EnableMileageSimulation', false);
    a.syncSpeed = flag('Sync0x03SpeedWithMainSpeed', true);
    a.oilConsumption = flag('EnableOilConsumption', false);
    a.networkFluctuation = flag('EnableNetworkSignalFluctuation', false);
    a.gnssFluctuation = flag('EnableGNSSFluctuation', false);
    a.consumptionRate = '${data['oilconsumptionrate'] ?? 8}';
    final rows = data['customattachitems'];
    if (rows != null) {
      if (rows is! List || rows.length > 128) {
        throw const FormatException('自定义附加项最多 128 项');
      }
      for (final row in rows) {
        if (row is! Map) throw const FormatException('自定义附加项结构无效');
        final r = {
          for (final e in row.entries) '${e.key}'.toLowerCase(): e.value,
        };
        String text(String key) {
          final v = r[key] ?? '';
          if (v is! String) throw const FormatException('RAW 字段必须为 HEX 文本');
          return v;
        }

        a.custom.add(
          RawLocationAttachment(
            id: text('attachid'),
            length: text('attachlength'),
            data: text('attachdata'),
          ),
        );
      }
    }
    a.toJson();
    a.encode(number(a.values['Speed0x03']!, 65535, '记录速度') / 10);
    return a;
  }
}

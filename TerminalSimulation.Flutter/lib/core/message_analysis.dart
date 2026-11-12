import 'dart:convert';
import 'dart:typed_data';
import 'package:fast_gbk/fast_gbk.dart';
import 'bytes.dart';
import 'jt808.dart';

class AnalysisField {
  const AnalysisField(
    this.group,
    this.name,
    this.offset,
    this.length,
    this.type,
    this.raw,
    this.value,
  );
  final String group, name, type, raw, value;
  final int offset, length;
  List<String> get cells => [name, raw, type, '$offset', '$length', value];
  Map<String, Object> toJson() => {
    '分组': group,
    '字段': name,
    '偏移': offset,
    '长度': length,
    '类型': type,
    'HEX': raw,
    '结果': value,
  };
}

class MessageAnalysis {
  const MessageAnalysis(
    this.title,
    this.frame,
    this.logical,
    this.fields,
    this.warnings,
  );
  final String title, frame, logical;
  final List<AnalysisField> fields;
  final List<String> warnings;
  String get table => [
    '字段\t报文原数据\t数据类型\t起始位\t数据长度\t分析结果',
    ...fields.map(
      (f) =>
          f.cells.map((v) => v.replaceAll(RegExp(r'[\r\n\t]'), ' ')).join('\t'),
    ),
  ].join('\n');
  String get json => const JsonEncoder.withIndent('  ').convert({
    '报文': title,
    '线缆帧': frame,
    '反转义帧': logical,
    '提示': warnings,
    '字段': fields.map((f) => f.toJson()).toList(),
  });
}

const messageNames = <int, String>{
  0x0001: '终端通用应答',
  0x8001: '平台通用应答',
  0x0002: '终端心跳',
  0x0003: '终端注销',
  0x8003: '补传分包请求',
  0x0100: '终端注册',
  0x8100: '终端注册应答',
  0x0102: '终端鉴权',
  0x0104: '查询终端参数应答',
  0x8103: '设置终端参数',
  0x8104: '查询终端参数',
  0x8105: '终端控制',
  0x8106: '查询指定终端参数',
  0x8107: '查询终端属性',
  0x0107: '查询终端属性应答',
  0x0108: '终端升级结果',
  0x0200: '位置信息汇报',
  0x0201: '位置查询应答',
  0x8201: '位置信息查询',
  0x8202: '临时位置跟踪',
  0x8203: '人工确认报警',
  0x0704: '定位数据批量上传',
  0x8300: '文本信息下发',
  0x0301: '事件报告',
  0x0302: '提问应答',
  0x0303: '信息点播/取消',
  0x8400: '电话回拨',
  0x8500: '车辆控制',
  0x0500: '车辆控制应答',
  0x0900: '数据上行透传',
  0x8900: '数据下行透传',
  0x0901: '数据压缩上报',
  0x0a00: '终端 RSA 公钥',
  0x8a00: '平台 RSA 公钥',
  0x9101: '实时音视频请求',
  0x9102: '音视频传输控制',
  0x9105: '音视频状态通知',
  0x1003: '上传音视频属性',
  0x9201: '录像回放请求',
  0x9202: '录像回放控制',
  0x9205: '音视频资源查询',
  0x1205: '音视频资源列表',
};

/// Runs in a worker isolate. Offsets include the opening 7E in the unescaped
/// frame; each leaf owns its exact bytes, while bit views share their parent.
MessageAnalysis analyzeMessage(String input) {
  if (input.length > 262144) throw const FormatException('输入过长，最多 256 KiB 文本');
  final wire = unhex(input);
  if (wire.length > 65536) throw const FormatException('单帧不能超过 64 KiB');
  final packet = Jt808Codec.decode(
    wire,
  ); // Boundaries, escapes, checksum and length.
  final raw = <int>[0x7e];
  for (var i = 1; i < wire.length - 1; i++) {
    var b = wire[i];
    if (b == 0x7d) {
      b = wire[++i] == 1 ? 0x7d : 0x7e;
    }
    raw.add(b);
  }
  raw.add(0x7e);
  final fields = <AnalysisField>[], warnings = <String>[];
  final r = _AnalysisReader(Uint8List.fromList(raw), fields, offset: 1);
  void boundary(int offset) => fields.add(
    AnalysisField(
      offset == 0 ? '帧开始' : '帧结束',
      '边界标识',
      offset,
      1,
      'BYTE',
      '7E',
      '0x7E',
    ),
  );
  boundary(0);
  r.number(
    '消息ID',
    2,
    describe: (v) =>
        '0x${v.toRadixString(16).padLeft(4, '0').toUpperCase()} · ${messageNames[v] ?? '未知/自定义消息'}',
  );
  final propOffset = r.offset,
      prop = r.number(
        '消息体属性',
        2,
        describe: (v) =>
            '0x${v.toRadixString(16).padLeft(4, '0').toUpperCase()}',
      );
  r.bits('消息体长度', propOffset, 2, 'bit 0–9', '${prop & 1023} B');
  r.bits(
    '加密方式',
    propOffset,
    2,
    'bit 10–12',
    '${packet.encryption} · ${packet.encryption == 0
        ? '未加密'
        : packet.encryption == 1
        ? 'RSA'
        : '保留/自定义'}',
  );
  r.bits('分包标识', propOffset, 2, 'bit 13', packet.fragmented ? '分包' : '未分包');
  r.bits(
    '版本标识',
    propOffset,
    2,
    'bit 14',
    packet.version == ProtocolVersion.v2019 ? '2019' : '2013',
  );
  if (prop & 0x8000 != 0) warnings.add('消息体属性保留位 bit 15 非零');
  if (packet.version == ProtocolVersion.v2019) r.number('协议版本号', 1);
  r.text('终端手机号', packet.version == ProtocolVersion.v2019 ? 10 : 6, bcd: true);
  r.number('消息流水号', 2);
  if (packet.fragmented) {
    r.number('消息总包数', 2);
    r.number('包序号', 2);
  }
  final bodyEnd = r.offset + packet.body.length;
  r.group = '消息体';
  r.limit = bodyEnd;
  if (packet.fragmented || packet.encryption != 0) {
    warnings.add(
      packet.fragmented ? '当前为单个分包，未重组消息体；保留原始字节' : '加密消息体未解密；保留原始字节',
    );
    r.raw('消息体原始数据', r.remaining);
  } else {
    try {
      _body(packet, r);
    } on FormatException catch (e) {
      warnings.add('消息体解析未完成：${e.message}');
    }
    if (r.remaining > 0) {
      warnings.add('存在未解析的消息体字节，已保留原始数据');
      r.raw('未解析消息体数据', r.remaining);
    }
  }
  r.limit = raw.length;
  r.offset = bodyEnd;
  r.group = '帧结束';
  r.number(
    '校验码',
    1,
    describe: (v) =>
        '0x${v.toRadixString(16).padLeft(2, '0').toUpperCase()} · XOR 校验通过',
  );
  boundary(raw.length - 1);
  if (fields.any((f) => f.name == '未提供字段解码的消息体')) {
    warnings.add('此消息体未提供字段级解码，当前显示完整原始字节');
  }
  return MessageAnalysis(
    '0x${packet.id.toRadixString(16).padLeft(4, '0').toUpperCase()} · ${messageNames[packet.id] ?? '未知/自定义消息'}',
    hex(wire),
    hex(raw),
    fields,
    warnings,
  );
}

class _AnalysisReader {
  _AnalysisReader(this.bytes, this.fields, {this.offset = 0})
    : limit = bytes.length;
  final Uint8List bytes;
  final List<AnalysisField> fields;
  int offset, limit;
  String group = '消息头';
  int get remaining => limit - offset;
  Uint8List read(int n) {
    if (n < 0 || n > remaining) {
      throw FormatException('偏移 $offset 需要 $n 字节，剩余 $remaining 字节');
    }
    final data = Uint8List.sublistView(bytes, offset, offset + n);
    offset += n;
    return data;
  }

  void add(String name, int at, int n, String type, String value) => fields.add(
    AnalysisField(
      group,
      name,
      at,
      n,
      type,
      hex(Uint8List.sublistView(bytes, at, at + n)),
      value,
    ),
  );
  int number(String name, int n, {String Function(int)? describe}) {
    final at = offset, data = read(n);
    var v = 0;
    for (final b in data) {
      v = v << 8 | b;
    }
    add(
      name,
      at,
      n,
      n == 1
          ? 'BYTE'
          : n == 2
          ? 'WORD'
          : n == 4
          ? 'DWORD'
          : 'INTEGER',
      describe?.call(v) ?? '$v',
    );
    return v;
  }

  void bits(String name, int at, int n, String range, String value) =>
      add(name, at, n, range, value);
  String text(String name, int n, {bool bcd = false}) {
    final at = offset, data = read(n);
    final value = bcd
        ? data.map((v) => v.toRadixString(16).padLeft(2, '0')).join()
        : gbk.decode(
            data.takeWhile((v) => v != 0).toList(),
            allowMalformed: true,
          );
    add(name, at, n, bcd ? 'BCD' : 'GBK', value);
    return value;
  }

  void raw(String name, int n) {
    final at = offset;
    read(n);
    add(name, at, n, 'BYTES', '原始数据 · $n B');
  }
}

void _body(Jt808Packet p, _AnalysisReader r) {
  final modern = p.version == ProtocolVersion.v2019;
  switch (p.id) {
    case 0x0001:
    case 0x8001:
      r.number('应答流水号', 2);
      r.number(
        '应答消息ID',
        2,
        describe: (v) =>
            '0x${v.toRadixString(16).padLeft(4, '0')} · ${messageNames[v] ?? '未知'}',
      );
      r.number(
        '结果',
        1,
        describe: (v) =>
            '$v · ${{0: '成功/确认', 1: '失败', 2: '消息有误', 3: '不支持', 4: '报警处理确认'}[v] ?? '保留'}',
      );
    case 0x0002:
    case 0x0003:
    case 0x8104:
    case 0x8107:
    case 0x8201:
      break;
    case 0x0100:
      r.number('省域ID', 2);
      r.number('市县ID', 2);
      r.text('制造商ID', modern ? 11 : 5);
      r.text('终端型号', modern ? 30 : 20);
      r.text('终端ID', modern ? 30 : 7);
      r.number(
        '车牌颜色',
        1,
        describe: (v) =>
            '$v · ${{0: '未上牌', 1: '蓝色', 2: '黄色', 3: '黑色', 4: '白色', 5: '绿色', 9: '其他'}[v] ?? '自定义'}',
      );
      r.text('车牌/车辆标识', r.remaining);
    case 0x8100:
      r.number('应答流水号', 2);
      final result = r.number(
        '注册结果',
        1,
        describe: (v) =>
            '$v · ${{0: '成功', 1: '车辆已注册', 2: '数据库无此车辆', 3: '终端已注册', 4: '数据库无此终端'}[v] ?? '未知'}',
      );
      if (result == 0) r.text('鉴权码', r.remaining);
    case 0x0102:
      if (modern) {
        final n = r.number('鉴权码长度', 1);
        r.text('鉴权码', n);
        r.text('IMEI', 15);
        r.text('软件版本', 20);
      } else {
        r.text('鉴权码', r.remaining);
      }
    case 0x0200:
      _location(r);
    case 0x0201:
    case 0x0500:
      r.number('应答流水号', 2);
      _location(r);
    case 0x8103:
    case 0x0104:
      if (p.id == 0x0104) r.number('应答流水号', 2);
      final count = r.number('参数个数', 1);
      for (var i = 0; i < count; i++) {
        r.group = '消息体 / 参数 ${i + 1}';
        final id = r.number(
          '参数ID',
          4,
          describe: (v) => '0x${v.toRadixString(16).padLeft(8, '0')}',
        );
        final n = r.number('参数长度', 1);
        if ([
          0x10,
          0x12,
          0x13,
          0x17,
          0x18,
          0x19,
          0x40,
          0x41,
          0x42,
          0x43,
          0x44,
          0x48,
          0x49,
          0x83,
        ].contains(id)) {
          r.text('参数值', n);
        } else if (n == 1 || n == 2 || n == 4) {
          r.number('参数值', n);
        } else {
          r.raw('参数值', n);
        }
      }
    case 0x8106:
      final n = r.number('参数个数', 1);
      for (var i = 0; i < n; i++) {
        r.number(
          '参数ID ${i + 1}',
          4,
          describe: (v) => '0x${v.toRadixString(16).padLeft(8, '0')}',
        );
      }
    case 0x8105:
      r.number('命令字', 1);
      if (r.remaining > 0) r.text('命令参数', r.remaining);
    case 0x8202:
      r.number('时间间隔（秒）', 2);
      r.number('有效期（秒）', 4);
    case 0x8203:
      r.number('报警消息流水号', 2);
      r.number('人工确认报警类型', 4);
    case 0x8300:
      r.number('文本标志', 1);
      if (modern) r.number('文本类型', 1);
      r.text('文本内容', r.remaining);
    case 0x0900:
    case 0x8900:
      r.number('透传类型', 1);
      r.raw('透传内容', r.remaining);
    case 0x8003:
      r.number('原始消息流水号', 2);
      final n = r.number('重传包数', 1);
      for (var i = 0; i < n; i++) {
        r.number('重传包ID ${i + 1}', 2);
      }
    case 0x0107:
      r.number('终端类型', 2);
      r.text('制造商ID', modern ? 11 : 5);
      r.text('终端型号', modern ? 30 : 20);
      r.text('终端ID', modern ? 30 : 7);
      r.text('ICCID', 10, bcd: true);
      final hw = r.number('硬件版本长度', 1);
      r.text('硬件版本', hw);
      final sw = r.number('固件版本长度', 1);
      r.text('固件版本', sw);
      r.number('GNSS属性', 1);
      r.number('通信模块属性', 1);
    case 0x0108:
      r.number('升级类型', 1);
      r.number('升级结果', 1);
    case 0x0704:
      final n = r.number('位置项数', 2);
      r.number('位置数据类型', 1);
      for (var i = 0; i < n; i++) {
        r.group = '消息体 / 位置 ${i + 1}';
        final length = r.number('位置项长度', 2),
            end = r.offset + length,
            limit = r.limit;
        if (end > limit) throw const FormatException('批量位置项越界');
        r.limit = end;
        try {
          _location(r);
        } finally {
          r.limit = limit;
        }
      }
    case 0x9101:
    case 0x9201:
      final n = r.number('服务器地址长度', 1);
      r.text('服务器地址', n);
      r.number('TCP端口', 2);
      r.number('UDP端口', 2);
      r.number('逻辑通道', 1);
      r.number('数据类型', 1);
      r.number('码流类型', 1);
      if (p.id == 0x9201) {
        r.number('存储器类型', 1);
        r.number('回放方式', 1);
        r.number('快进/快退倍数', 1);
        r.text('开始时间', 6, bcd: true);
        r.text('结束时间', 6, bcd: true);
      }
    case 0x9102:
      r.number('逻辑通道', 1);
      r.number('控制指令', 1);
      r.number('关闭音视频类型', 1);
      r.number('切换码流类型', 1);
    case 0x9105:
      r.number('逻辑通道', 1);
      r.number('丢包率', 1);
    case 0x9202:
      r.number('逻辑通道', 1);
      r.number('回放控制', 1);
      r.number('倍数', 1);
      r.text('拖动回放时间', 6, bcd: true);
    case 0x0a00:
    case 0x8a00:
      r.number('RSA指数', 4);
      r.raw('RSA模数', 128);
    default:
      if (r.remaining > 0) r.raw('未提供字段解码的消息体', r.remaining);
  }
}

void _location(_AnalysisReader r) {
  final alarmAt = r.offset,
      alarm = r.number(
        '报警标志',
        4,
        describe: (v) =>
            '0x${v.toRadixString(16).padLeft(8, '0').toUpperCase()}',
      );
  const alarmNames = {
    0: '紧急报警',
    1: '超速报警',
    2: '疲劳驾驶',
    3: '危险预警',
    4: 'GNSS模块故障',
    5: 'GNSS天线未接',
    6: 'GNSS天线短路',
    7: '主电源欠压',
    8: '主电源掉电',
    9: '显示器故障',
    10: 'TTS模块故障',
    11: '摄像头故障',
    18: '当天累计驾驶超时',
    19: '超时停车',
    20: '进出区域',
    21: '进出路线',
    22: '路段行驶时间异常',
    23: '路线偏离',
    24: '车速传感器故障',
    25: '油量异常',
    26: '车辆被盗',
    27: '非法点火',
    28: '非法位移',
    29: '碰撞预警',
    30: '侧翻预警',
  };
  for (final e in alarmNames.entries) {
    if (alarm & (1 << e.key) != 0) {
      r.bits(e.value, alarmAt, 4, 'bit ${e.key}', '触发');
    }
  }
  final at = r.offset,
      status = r.number(
        '状态',
        4,
        describe: (v) =>
            '0x${v.toRadixString(16).padLeft(8, '0').toUpperCase()}',
      );
  for (final e in {
    0: ('ACC', '关闭', '开启'),
    1: ('定位', '未定位', '已定位'),
    2: ('纬度方向', '北纬', '南纬'),
    3: ('经度方向', '东经', '西经'),
    4: ('运营状态', '运营', '停运'),
    5: ('经纬度加密', '未加密', '已加密'),
    10: ('油路', '正常', '断开'),
    11: ('电路', '正常', '断开'),
    12: ('车门锁', '解锁', '加锁'),
  }.entries) {
    r.bits(
      e.value.$1,
      at,
      4,
      'bit ${e.key}',
      status & (1 << e.key) == 0 ? e.value.$2 : e.value.$3,
    );
  }
  r.number(
    '纬度',
    4,
    describe: (v) => '${v / 1e6 * (status & 4 == 0 ? 1 : -1)}°',
  );
  r.number(
    '经度',
    4,
    describe: (v) => '${v / 1e6 * (status & 8 == 0 ? 1 : -1)}°',
  );
  r.number('高程', 2, describe: (v) => '$v m');
  r.number('速度', 2, describe: (v) => '${v / 10} km/h');
  r.number('方向', 2, describe: (v) => '$v°');
  final time = r.text('时间 YYMMDDhhmmss', 6, bcd: true);
  if (!RegExp(r'^\d{12}$').hasMatch(time)) {
    throw const FormatException('时间包含无效 BCD');
  }
  while (r.remaining > 0) {
    final id = r.number(
          '附加项ID',
          1,
          describe: (v) => '0x${v.toRadixString(16).padLeft(2, '0')}',
        ),
        n = r.number('附加项长度', 1);
    final info = <int, (String, int, double, String)>{
      1: ('里程', 4, .1, 'km'),
      2: ('油量', 2, .1, 'L'),
      3: ('记录速度', 2, .1, 'km/h'),
      0x25: ('扩展车辆信号', 4, 1, ''),
      0x2a: ('IO状态', 2, 1, ''),
      0x30: ('无线通信信号强度', 1, 1, ''),
      0x31: ('GNSS卫星数', 1, 1, '颗'),
    }[id];
    if (info != null && n == info.$2) {
      r.number(info.$1, n, describe: (v) => '${v * info.$3} ${info.$4}'.trim());
    } else {
      r.raw('附加项值', n);
    }
  }
}

import 'dart:convert';
import 'dart:typed_data';
import 'package:flutter/material.dart';
import '../core/bytes.dart';
import '../core/location_attachments.dart';
import '../services/app_model.dart';
import '../services/document_files.dart';
import 'components.dart';

class LocationAttachmentEditor extends StatefulWidget {
  const LocationAttachmentEditor({super.key, required this.model});
  final AppModel model;
  @override
  State<LocationAttachmentEditor> createState() =>
      _LocationAttachmentEditorState();
}

class _LocationAttachmentEditorState extends State<LocationAttachmentEditor> {
  bool fileBusy = false;
  LocationAttachments get a => widget.model.locationAttachments;
  void change(VoidCallback work) {
    setState(work);
    widget.model.changed();
  }

  Future<void> fileAction(Future<void> Function() action) async {
    if (fileBusy) return;
    setState(() => fileBusy = true);
    try {
      await action();
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text('$e')));
      }
    } finally {
      if (mounted) setState(() => fileBusy = false);
    }
  }

  Future<void> importConfig() async {
    final bytes = await DocumentFiles.open(extensions: ['json']);
    if (bytes == null || !mounted) return;
    if (bytes.length > 256 * 1024) {
      throw const FormatException('附加项配置最大 256 KiB');
    }
    final data = jsonDecode(utf8.decode(bytes).replaceFirst('\uFEFF', ''));
    if (data is! Map<String, dynamic>) {
      throw const FormatException('配置必须为 JSON 对象');
    }
    final input = data['locationAttachments'] is Map<String, dynamic>
        ? data['locationAttachments'] as Map<String, dynamic>
        : data;
    if (!input.keys.any(
      (k) =>
          k.toLowerCase().startsWith('enable0x') ||
          k.toLowerCase() == 'customattachitems',
    )) {
      throw const FormatException('文件中未找到位置附加项配置');
    }
    final config = LocationAttachments.fromJson(input);
    final confirmed = await confirmAction(
      context,
      title: '导入位置附加项',
      message: '将替换当前标准附加项、模拟开关和自定义 RAW 列表。平台连接、位置与鉴权码不受影响。',
    );
    if (!mounted || !confirmed) return;
    change(() => widget.model.locationAttachments = config);
    widget.model.log('配置', '位置附加项已导入；点击保存配置可在重启后保留');
  }

  Future<void> exportConfig() async {
    widget.model.locationAttachmentBytes();
    final bytes = Uint8List.fromList(
      utf8.encode(const JsonEncoder.withIndent('  ').convert(a.toJson())),
    );
    if (await DocumentFiles.save(bytes, 'location-attachments.json')) {
      widget.model.log('配置', '位置附加项已导出');
    }
  }

  Future<void> editBits(LocationAttachmentSpec spec) async {
    final parsed = int.tryParse(a.values[spec.key]!);
    var bits = parsed != null && parsed >= 0 && parsed <= spec.maximum
        ? parsed
        : 0;
    final names = spec.id == 0x25 ? vehicleSignalNames : ioStatusNames;
    final result = await showDialog<int>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, update) => AlertDialog(
          title: Text('0x${spec.hexId} ${spec.name}'),
          content: SizedBox(
            width: 620,
            child: SingleChildScrollView(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  SelectableText(
                    '十进制 $bits · HEX ${bits.toRadixString(16).toUpperCase().padLeft(spec.width * 2, '0')}',
                  ),
                  const SizedBox(height: 12),
                  FieldGrid(
                    minWidth: 200,
                    children: [
                      for (var i = 0; i < names.length; i++)
                        CheckboxListTile(
                          contentPadding: EdgeInsets.zero,
                          controlAffinity: ListTileControlAffinity.leading,
                          title: Text('Bit $i · ${names[i]}'),
                          value: bits & (1 << i) != 0,
                          onChanged: (v) => update(
                            () => bits = v == true
                                ? bits | (1 << i)
                                : bits & ~(1 << i),
                          ),
                        ),
                    ],
                  ),
                ],
              ),
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('取消'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, bits),
              child: const Text('应用'),
            ),
          ],
        ),
      ),
    );
    if (result != null && mounted) change(() => a.values[spec.key] = '$result');
  }

  Widget numberField(
    String key,
    String label,
    int maximum, {
    bool enabled = true,
    String? unit,
  }) => EditField(
    key: ValueKey('${identityHashCode(a)}-$key'),
    label,
    value: a.values[key]!,
    enabled: enabled,
    numeric: true,
    helperText: unit == null ? '范围 0–$maximum' : '$unit · 范围 0–$maximum',
    validator: (value) {
      if (!enabled) return null;
      try {
        LocationAttachments.number(value ?? '', maximum, label);
        return null;
      } on FormatException catch (e) {
        return e.message;
      }
    },
    onChanged: (v) => a.values[key] = v,
  );

  Widget option(String title, bool selected, ValueChanged<bool> onChanged) =>
      CheckboxListTile(
        contentPadding: EdgeInsets.zero,
        controlAffinity: ListTileControlAffinity.leading,
        title: Text(title),
        value: selected,
        onChanged: (v) => change(() => onChanged(v ?? false)),
      );

  Widget standard(LocationAttachmentSpec s) {
    final active = a.enabled[s.id] == true;
    return DecoratedBox(
      decoration: BoxDecoration(
        border: Border.all(color: Theme.of(context).colorScheme.outlineVariant),
        borderRadius: BorderRadius.circular(16),
      ),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            CheckboxListTile(
              contentPadding: EdgeInsets.zero,
              controlAffinity: ListTileControlAffinity.leading,
              title: Text('0x${s.hexId} · ${s.name}'),
              value: active,
              onChanged: (v) => change(() => a.enabled[s.id] = v ?? false),
            ),
            const SizedBox(height: 12),
            if (s.id == 0x2b)
              FieldGrid(
                minWidth: 130,
                children: [
                  numberField(
                    'AnalogAD1',
                    'AD1 · 高 16 位',
                    65535,
                    enabled: active,
                  ),
                  numberField(
                    'AnalogAD0',
                    'AD0 · 低 16 位',
                    65535,
                    enabled: active,
                  ),
                ],
              )
            else if (s.id == 0x31 && widget.model.realLocation)
              EditField(
                'GNSS 卫星数',
                value: widget.model.hasDeviceFix
                    ? '${widget.model.reportGnssCount ?? '未提供'}'
                    : '等待定位',
                enabled: false,
                helperText: '仅使用系统提供的参与解算卫星数；网络定位不附带此项',
                onChanged: (_) {},
              ),
            if (s.id != 0x2b && !(s.id == 0x31 && widget.model.realLocation))
              numberField(
                s.key,
                s.name,
                s.maximum,
                enabled: active && !(s.id == 3 && a.syncSpeed),
                unit: s.unit,
              ),
            if (s.id == 1)
              option(
                '按主速度累计里程',
                a.mileageSimulation,
                (v) => a.mileageSimulation = v,
              ),
            if (s.id == 2) ...[
              option(
                '按主速度自动消耗油量',
                a.oilConsumption,
                (v) => a.oilConsumption = v,
              ),
              if (a.oilConsumption)
                Padding(
                  padding: const EdgeInsets.only(top: 12),
                  child: EditField(
                    '百公里油耗（L/100km）',
                    value: a.consumptionRate,
                    numeric: true,
                    helperText: '范围 0–1000',
                    onChanged: (v) => a.consumptionRate = v,
                    validator: (v) {
                      final n = double.tryParse(v ?? '');
                      return n != null && n.isFinite && n >= 0 && n <= 1000
                          ? null
                          : '请输入 0–1000 的数值';
                    },
                  ),
                ),
            ],
            if (s.id == 3)
              option('同步主速度（×10）', a.syncSpeed, (v) => a.syncSpeed = v),
            if (s.id == 0x25 || s.id == 0x2a)
              TextButton.icon(
                onPressed: active ? () => editBits(s) : null,
                icon: const Icon(Icons.tune),
                label: const Text('选择状态位'),
              ),
            if (s.id == 0x30)
              option(
                '随机波动（0–31）',
                a.networkFluctuation,
                (v) => a.networkFluctuation = v,
              ),
            if (s.id == 0x31 && !widget.model.realLocation)
              option(
                '随机波动（0–30）',
                a.gnssFluctuation,
                (v) => a.gnssFluctuation = v,
              ),
          ],
        ),
      ),
    );
  }

  Widget rawRow(RawLocationAttachment row, int index) => Padding(
    key: ObjectKey(row),
    padding: const EdgeInsets.only(bottom: 16),
    child: DecoratedBox(
      decoration: BoxDecoration(
        border: Border.all(color: Theme.of(context).colorScheme.outlineVariant),
        borderRadius: BorderRadius.circular(16),
      ),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(child: Text('RAW ${index + 1}')),
                IconButton(
                  tooltip: '删除此附加项',
                  onPressed: () => change(() => a.custom.remove(row)),
                  icon: const Icon(Icons.delete_outline),
                ),
              ],
            ),
            const SizedBox(height: 12),
            FieldGrid(
              minWidth: 180,
              children: [
                EditField(
                  'ID（HEX）',
                  value: row.id,
                  onChanged: (v) => row.id = v,
                ),
                EditField(
                  '长度（HEX，可留空）',
                  value: row.length,
                  onChanged: (v) => row.length = v,
                ),
              ],
            ),
            const SizedBox(height: 20),
            EditField(
              '数据内容（HEX）',
              value: row.data,
              lines: 2,
              onChanged: (v) => row.data = v,
            ),
            TextButton.icon(
              icon: const Icon(Icons.calculate_outlined),
              label: const Text('按数据填入长度'),
              onPressed: () => fileAction(() async {
                final bytes = RawLocationAttachment(data: row.data).encode();
                if (bytes.length > 255) {
                  throw const FormatException('单项 TLV 数据最多 255 字节');
                }
                if (mounted) {
                  change(
                    () => row.length = bytes.length
                        .toRadixString(16)
                        .toUpperCase()
                        .padLeft(2, '0'),
                  );
                }
              }),
            ),
          ],
        ),
      ),
    ),
  );

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      ExpansionTile(
        key: const PageStorageKey('location-standard-attachments'),
        tilePadding: EdgeInsets.zero,
        title: const Text('标准附加信息（0x0200）'),
        subtitle: Text('已启用 ${a.enabledCount} / 9 项'),
        children: [
          Padding(
            padding: const EdgeInsets.only(top: 16, bottom: 16),
            child: FieldGrid(
              minWidth: 300,
              children: locationAttachmentSpecs.map(standard).toList(),
            ),
          ),
        ],
      ),
      ExpansionTile(
        key: const PageStorageKey('location-raw-attachments'),
        tilePadding: EdgeInsets.zero,
        title: const Text('自定义附加信息（RAW HEX）'),
        subtitle: Text('${a.custom.length} 项 · 标准项之后按列表顺序拼接'),
        children: [
          const Padding(
            padding: EdgeInsets.symmetric(vertical: 12),
            child: Text(
              'ID、长度、数据均为独立 HEX 字节串，支持空格、-、|。长度留空时不补齐；重复 ID 和非标准长度原样发送，用于平台联调。无效 HEX 会阻止发送，不会跳过或改写。',
            ),
          ),
          for (var i = 0; i < a.custom.length; i++) rawRow(a.custom[i], i),
          Align(
            alignment: Alignment.centerLeft,
            child: OutlinedButton.icon(
              onPressed: a.custom.length >= 128
                  ? null
                  : () => change(() => a.custom.add(RawLocationAttachment())),
              icon: const Icon(Icons.add),
              label: const Text('添加附加项'),
            ),
          ),
          const SizedBox(height: 12),
        ],
      ),
      const SizedBox(height: 12),
      ActionGrid(
        children: [
          OutlinedButton.icon(
            onPressed: fileBusy
                ? null
                : () => fileAction(widget.model.saveSettings),
            icon: const Icon(Icons.save_outlined),
            label: const Text('保存配置'),
          ),
          OutlinedButton.icon(
            onPressed: fileBusy ? null : () => fileAction(importConfig),
            icon: const Icon(Icons.file_open_outlined),
            label: const Text('导入附加项'),
          ),
          OutlinedButton.icon(
            onPressed: fileBusy ? null : () => fileAction(exportConfig),
            icon: const Icon(Icons.file_download_outlined),
            label: const Text('导出附加项'),
          ),
          OutlinedButton.icon(
            onPressed: () {
              try {
                final bytes = widget.model.locationAttachmentBytes();
                showDialog<void>(
                  context: context,
                  builder: (context) => AlertDialog(
                    title: Text('附加数据 · ${bytes.length} B'),
                    content: SizedBox(
                      width: 620,
                      child: SingleChildScrollView(
                        child: SelectableText(
                          bytes.isEmpty ? '未启用附加项' : hex(bytes),
                          style: const TextStyle(fontFamily: 'Consolas'),
                        ),
                      ),
                    ),
                    actions: [
                      TextButton(
                        onPressed: () => Navigator.pop(context),
                        child: const Text('关闭'),
                      ),
                    ],
                  ),
                );
              } catch (e) {
                ScaffoldMessenger.of(
                  context,
                ).showSnackBar(SnackBar(content: Text('$e')));
              }
            },
            icon: const Icon(Icons.data_object),
            label: const Text('预览 HEX'),
          ),
        ],
      ),
    ],
  );
}

import 'package:flutter/material.dart';
import '../core/jt808.dart';
import '../services/app_model.dart';
import '../services/terminal_session.dart' as terminal;
import 'components.dart';
import 'location_attachment_editor.dart';
import 'device_position_controls.dart';
import '../services/mobile_runtime.dart';

class TerminalPage extends StatefulWidget {
  const TerminalPage({super.key, required this.model});
  final AppModel model;
  @override
  State<TerminalPage> createState() => _TerminalPageState();
}

class _TerminalPageState extends State<TerminalPage> {
  int heartbeatSeconds = 30, reportMilliseconds = 1000;
  String error = '',
      manufacturer = 'CTS',
      deviceModel = 'TerminalSimulation',
      terminalId = '0000001',
      plate = '测试车辆',
      province = '0',
      city = '0',
      plateColor = '1';
  final auth = TextEditingController();
  @override
  void initState() {
    super.initState();
    widget.model.addListener(_sync);
    auth.text = widget.model.authCode;
  }

  void _sync() {
    if (auth.text != widget.model.authCode) auth.text = widget.model.authCode;
  }

  @override
  void dispose() {
    widget.model.removeListener(_sync);
    auth.dispose();
    super.dispose();
  }

  Future<void> act(Future<void> Function() action) async {
    try {
      await action();
      if (mounted) setState(() => error = '');
    } catch (e) {
      widget.model.log('错误', '$e');
      if (mounted) setState(() => error = '$e');
    }
  }

  void _identity() {
    widget.model.controller.identity = TerminalIdentity(
      province: int.parse(province),
      city: int.parse(city),
      manufacturer: manufacturer,
      model: deviceModel,
      terminalId: terminalId,
      plateColor: int.parse(plateColor),
      plate: plate,
    );
  }

  @override
  Widget build(BuildContext context) {
    final m = widget.model,
        connected = m.session.state == terminal.ConnectionState.connected,
        busy = m.session.state == terminal.ConnectionState.connecting;
    return PageBody(
      title: '终端工作台',
      description: '配置终端身份，连接平台并观察实时通信。',
      children: [
        if (error.isNotEmpty)
          MaterialBanner(
            content: Text(error),
            actions: [
              TextButton(
                onPressed: () => setState(() => error = ''),
                child: const Text('关闭'),
              ),
            ],
          ),
        Section(
          '平台连接',
          subtitle:
              '${m.host}:${m.port} · ${busy
                  ? '连接中'
                  : connected
                  ? '已连接'
                  : '未连接'}',
          trailing: Icon(
            connected ? Icons.link : Icons.link_off,
            color: connected ? Theme.of(context).colorScheme.primary : null,
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              ExpansionTile(
                key: const PageStorageKey('platform-connection-settings'),
                tilePadding: EdgeInsets.zero,
                onExpansionChanged: (expanded) {
                  if (!expanded) FocusScope.of(context).unfocus();
                  setState(() {});
                },
                title: const Text('连接配置'),
                subtitle: Text(
                  '${m.phone} · JT808 ${m.version == ProtocolVersion.v2019 ? '2019' : '2013'}',
                ),
                children: [
                  Padding(
                    padding: EdgeInsets.only(
                      top: MediaQuery.textScalerOf(context).scale(12) + 4,
                      bottom: 16,
                    ),
                    child: FieldGrid(
                      children: [
                        EditField(
                          '服务器',
                          value: m.host,
                          enabled: !connected && !busy,
                          onChanged: (v) => m.host = v,
                        ),
                        EditField(
                          '端口',
                          enabled: !connected && !busy,
                          value: '${m.port}',
                          numeric: true,
                          onChanged: (v) => m.port = int.tryParse(v) ?? 0,
                        ),
                        EditField(
                          '终端手机号',
                          enabled: !connected && !busy,
                          value: m.phone,
                          onChanged: (v) => m.phone = v,
                        ),
                        DropdownButtonFormField<ProtocolVersion>(
                          isExpanded: true,
                          initialValue: m.version,
                          decoration: const InputDecoration(labelText: '协议版本'),
                          items: ProtocolVersion.values
                              .map(
                                (v) => DropdownMenuItem(
                                  value: v,
                                  child: Text(
                                    v == ProtocolVersion.v2019
                                        ? 'JT808 · 2019'
                                        : 'JT808 · 2013',
                                  ),
                                ),
                              )
                              .toList(),
                          onChanged: connected || busy
                              ? null
                              : (v) {
                                  m.version = v!;
                                  m.changed();
                                },
                        ),
                      ],
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 16),
              Wrap(
                spacing: 10,
                runSpacing: 10,
                children: [
                  FilledButton.icon(
                    onPressed: busy
                        ? null
                        : () => act(
                            connected || m.reconnecting
                                ? m.disconnect
                                : m.connect,
                          ),
                    icon: Icon(connected ? Icons.link_off : Icons.link),
                    label: Text(
                      busy
                          ? '连接中…'
                          : connected
                          ? '断开连接'
                          : m.reconnecting
                          ? '取消重连'
                          : '连接平台',
                    ),
                  ),
                  if (busy)
                    OutlinedButton(
                      onPressed: () => act(m.disconnect),
                      child: const Text('取消连接'),
                    ),
                  OutlinedButton.icon(
                    onPressed: () => act(m.saveSettings),
                    icon: const Icon(Icons.save_outlined),
                    label: const Text('保存配置'),
                  ),
                ],
              ),
              if (MobileRuntime.supported) ...[
                const SizedBox(height: 12),
                SwitchListTile.adaptive(
                  contentPadding: EdgeInsets.zero,
                  title: const Text('后台保持连接'),
                  subtitle: Text(
                    m.backgroundMessage.isEmpty
                        ? '连接期间显示后台任务通知，断线后自动重连。'
                        : m.backgroundMessage,
                  ),
                  value: m.backgroundConnection,
                  onChanged: (v) => act(() => m.setBackgroundConnection(v)),
                ),
                if (MobileRuntime.backgroundSettingsSupported)
                  TextButton.icon(
                    onPressed: () => act(m.runtime.openBackgroundSettings),
                    icon: const Icon(Icons.battery_saver_outlined),
                    label: const Text('系统后台设置'),
                  ),
              ],
            ],
          ),
        ),
        Section(
          '身份与信令',
          subtitle: m.terminalStatus,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              ExpansionTile(
                tilePadding: EdgeInsets.zero,
                title: const Text('注册身份'),
                children: [
                  Padding(
                    padding: EdgeInsets.fromLTRB(
                      0,
                      MediaQuery.textScalerOf(context).scale(12) + 4,
                      0,
                      20,
                    ),
                    child: FieldGrid(
                      children: [
                        EditField(
                          '省域 ID',
                          value: province,
                          numeric: true,
                          onChanged: (v) => province = v,
                        ),
                        EditField(
                          '市县 ID',
                          value: city,
                          numeric: true,
                          onChanged: (v) => city = v,
                        ),
                        EditField(
                          '制造商 ID',
                          value: manufacturer,
                          onChanged: (v) => manufacturer = v,
                        ),
                        EditField(
                          '终端型号',
                          value: deviceModel,
                          onChanged: (v) => deviceModel = v,
                        ),
                        EditField(
                          '终端 ID',
                          value: terminalId,
                          onChanged: (v) => terminalId = v,
                        ),
                        EditField(
                          '车牌',
                          value: plate,
                          onChanged: (v) => plate = v,
                        ),
                        EditField(
                          '车牌颜色',
                          value: plateColor,
                          numeric: true,
                          onChanged: (v) => plateColor = v,
                        ),
                      ],
                    ),
                  ),
                ],
              ),
              AppTextField(
                controller: auth,
                obscureText: false,
                autocorrect: false,
                enableSuggestions: false,
                decoration: const InputDecoration(labelText: '鉴权码（仅本次会话）'),
                onChanged: (v) => m.authCode = v,
              ),
              const SizedBox(height: 16),
              Wrap(
                spacing: 10,
                runSpacing: 10,
                children: [
                  FilledButton(
                    onPressed: connected
                        ? () => act(() async {
                            _identity();
                            await m.controller.register();
                          })
                        : null,
                    child: const Text('终端注册'),
                  ),
                  OutlinedButton(
                    onPressed: connected
                        ? () => act(() async {
                            _identity();
                            await m.controller.authenticate(auth.text);
                          })
                        : null,
                    child: const Text('终端鉴权'),
                  ),
                  OutlinedButton(
                    onPressed: connected
                        ? () => act(() async {
                            await m.session.send(2);
                          })
                        : null,
                    child: const Text('发送心跳'),
                  ),
                  OutlinedButton(
                    onPressed: connected
                        ? () => act(() async {
                            await m.session.send(3);
                          })
                        : null,
                    child: const Text('终端注销'),
                  ),
                ],
              ),
              const SizedBox(height: 16),
              FieldGrid(
                children: [
                  EditField(
                    '心跳间隔（秒）',
                    value: '$heartbeatSeconds',
                    numeric: true,
                    onChanged: (v) => heartbeatSeconds = int.tryParse(v) ?? 0,
                  ),
                  SwitchListTile(
                    contentPadding: EdgeInsets.zero,
                    title: const Text('周期心跳'),
                    value: m.heartbeat,
                    onChanged: connected
                        ? (v) => act(() async {
                            m.toggleHeartbeat(heartbeatSeconds);
                          })
                        : null,
                  ),
                ],
              ),
            ],
          ),
        ),
        Section(
          '位置汇报',
          subtitle: '编辑位置参数，发送单次或周期汇报。',
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              DevicePositionControls(model: m),
              FieldGrid(
                children: [
                  EditField(
                    '纬度',
                    enabled: !m.realLocation,
                    value: m.latitude.toStringAsFixed(6),
                    numeric: true,
                    onChanged: (v) =>
                        m.latitude = double.tryParse(v) ?? double.nan,
                  ),
                  EditField(
                    '经度',
                    enabled: !m.realLocation,
                    value: m.longitude.toStringAsFixed(6),
                    numeric: true,
                    onChanged: (v) =>
                        m.longitude = double.tryParse(v) ?? double.nan,
                  ),
                  EditField(
                    '速度（km/h）',
                    enabled: !m.realLocation,
                    value: '${m.speed}',
                    numeric: true,
                    onChanged: (v) =>
                        m.speed = double.tryParse(v) ?? double.nan,
                  ),
                  EditField(
                    '高程（m）',
                    enabled: !m.realLocation,
                    value: '${m.altitude}',
                    numeric: true,
                    onChanged: (v) => m.altitude = int.tryParse(v) ?? -1,
                  ),
                  EditField(
                    '方向（0–359）',
                    enabled: !m.realLocation,
                    value: '${m.direction}',
                    numeric: true,
                    onChanged: (v) => m.direction = int.tryParse(v) ?? -1,
                  ),
                  EditField(
                    '报警标志（HEX）',
                    value: m.alarm.toRadixString(16),
                    onChanged: (v) =>
                        m.alarm = int.tryParse(v, radix: 16) ?? -1,
                  ),
                  EditField(
                    '状态标志（HEX）',
                    value: m.status.toRadixString(16),
                    onChanged: (v) =>
                        m.status = int.tryParse(v, radix: 16) ?? -1,
                  ),
                  EditField(
                    '周期汇报间隔（ms）',
                    value: '$reportMilliseconds',
                    numeric: true,
                    onChanged: (v) => reportMilliseconds = int.tryParse(v) ?? 0,
                  ),
                ],
              ),
              const SizedBox(height: 16),
              LocationAttachmentEditor(model: m),
              const SizedBox(height: 12),
              ActionGrid(
                children: [
                  FilledButton.icon(
                    onPressed: connected
                        ? () => act(() async {
                            await m.controller.report();
                          })
                        : null,
                    icon: const Icon(Icons.near_me_outlined),
                    label: const Text('上报位置'),
                  ),
                  FilledButton.tonalIcon(
                    label: Text(m.reporting ? '停止周期汇报' : '周期汇报'),
                    icon: Icon(
                      m.reporting ? Icons.stop_circle_outlined : Icons.update,
                    ),
                    onPressed: connected
                        ? () => act(() async {
                            m.toggleReports(reportMilliseconds);
                          })
                        : null,
                  ),
                ],
              ),
            ],
          ),
        ),
        Section(
          '会话流量',
          child: FieldGrid(
            children: [
              Text(
                '发送  ${m.sentBytes} B',
                style: Theme.of(context).textTheme.titleLarge,
              ),
              Text(
                '接收  ${m.receivedBytes} B',
                style: Theme.of(context).textTheme.titleLarge,
              ),
            ],
          ),
        ),
      ],
    );
  }
}

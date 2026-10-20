import 'dart:async';
import 'package:flutter/material.dart';
import '../services/app_model.dart';
import 'components.dart';

class VideoPage extends StatefulWidget {
  const VideoPage({super.key, required this.model});
  final AppModel model;
  @override
  State<VideoPage> createState() => _VideoPageState();
}

class _VideoPageState extends State<VideoPage> {
  late final sessions = widget.model.videoSessions;
  final path = TextEditingController(),
      host = TextEditingController(text: '127.0.0.1'),
      port = TextEditingController(text: '1078'),
      fps = TextEditingController(text: '25');
  late final timer = Timer.periodic(const Duration(milliseconds: 300), (_) {
    if (mounted) setState(() {});
  });
  int channel = 1;
  String error = '';
  @override
  void initState() {
    super.initState();
    timer;
  }

  @override
  void dispose() {
    timer.cancel();
    for (final c in [path, host, port, fps]) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> _start() async {
    final session = sessions[channel - 1];
    try {
      await session.stop();
      session.sim = widget.model.session.phone;
      widget.model.videoSources[channel] = (
        path: path.text.trim(),
        fps: double.parse(fps.text),
      );
      await session.start(
        host: host.text.trim(),
        port: int.parse(port.text),
        path: path.text.trim(),
        fps: double.parse(fps.text),
      );
      if (mounted) setState(() => error = '');
    } catch (e) {
      if (mounted) setState(() => error = '$e');
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('JT1078 通道')),
    body: PageBody(
      title: '音视频推流',
      description: '通过 TCP 推送 H.264 Annex-B 裸流。切换页面后继续推流；离开应用前可停止全部通道。',
      children: [
        if (error.isNotEmpty)
          Text(
            error,
            style: TextStyle(color: Theme.of(context).colorScheme.error),
          ),
        Section(
          '推流源',
          child: Column(
            children: [
              TextField(
                controller: path,
                decoration: const InputDecoration(labelText: 'H.264 文件路径'),
              ),
              const SizedBox(height: 16),
              FieldGrid(
                children: [
                  TextField(
                    controller: host,
                    decoration: const InputDecoration(labelText: '媒体服务器'),
                  ),
                  TextField(
                    controller: port,
                    decoration: const InputDecoration(labelText: 'TCP 端口'),
                  ),
                  TextField(
                    controller: fps,
                    decoration: const InputDecoration(labelText: '帧率'),
                  ),
                  DropdownButtonFormField<int>(
                    isExpanded: true,
                    initialValue: channel,
                    decoration: const InputDecoration(labelText: '逻辑通道'),
                    items: List.generate(
                      36,
                      (i) => DropdownMenuItem(
                        value: i + 1,
                        child: Text('通道 ${i + 1}'),
                      ),
                    ),
                    onChanged: (v) => setState(() => channel = v!),
                  ),
                ],
              ),
              const SizedBox(height: 16),
              Wrap(
                spacing: 10,
                runSpacing: 10,
                children: [
                  FilledButton(
                    onPressed: sessions[channel - 1].connecting ? null : _start,
                    child: const Text('开始 / 重启通道'),
                  ),
                  OutlinedButton(
                    onPressed: () => sessions[channel - 1].stop(),
                    child: const Text('停止通道'),
                  ),
                  OutlinedButton(
                    onPressed: () async {
                      await Future.wait(sessions.map((s) => s.stop()));
                    },
                    child: const Text('停止全部'),
                  ),
                ],
              ),
            ],
          ),
        ),
        Section(
          '36 路逻辑通道',
          child: FieldGrid(
            minWidth: 180,
            children: sessions
                .map(
                  (s) => Container(
                    padding: const EdgeInsets.all(14),
                    decoration: BoxDecoration(
                      color: Theme.of(context).colorScheme.surfaceContainerLow,
                      borderRadius: BorderRadius.circular(12),
                    ),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'CH ${s.channel.toString().padLeft(2, '0')}',
                          style: const TextStyle(fontWeight: FontWeight.bold),
                        ),
                        const SizedBox(height: 8),
                        Text(
                          s.connecting
                              ? '连接中'
                              : s.running
                              ? '推流中'
                              : '空闲',
                        ),
                        Text('${s.bytesSent} B'),
                      ],
                    ),
                  ),
                )
                .toList(),
          ),
        ),
      ],
    ),
  );
}

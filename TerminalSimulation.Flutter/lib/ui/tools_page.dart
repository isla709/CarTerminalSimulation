import 'package:flutter/material.dart';
import '../services/app_model.dart';
import '../services/http_workspace.dart';
import 'components.dart';
import 'video_page.dart';
import 'message_analyzer_page.dart';
import 'http_workbench_page.dart';
import 'passthrough_workbench_page.dart';

class ToolsPage extends StatefulWidget {
  const ToolsPage({super.key, required this.model});
  final AppModel model;
  @override
  State<ToolsPage> createState() => _ToolsPageState();
}

class _ToolsPageState extends State<ToolsPage> {
  final analyzerSession = MessageAnalyzerSession(),
      passthroughDraft = PassthroughDraft();
  late final httpWorkspace = HttpWorkspace(
    widget.model.storage,
    onEvent: (text) => widget.model.log('HTTP', text),
  );
  bool opening = false;
  Future<void> open(Widget page) async {
    if (opening) return;
    opening = true;
    try {
      await Navigator.of(
        context,
      ).push(MaterialPageRoute<void>(builder: (_) => page));
    } finally {
      opening = false;
    }
  }

  Widget entry(
    String title,
    String description,
    String label,
    IconData icon,
    Widget Function() page,
  ) => Section(
    title,
    subtitle: description,
    child: Align(
      alignment: Alignment.centerLeft,
      child: FilledButton.icon(
        onPressed: () => open(page()),
        icon: Icon(icon),
        label: Text(label),
      ),
    ),
  );
  @override
  Widget build(BuildContext context) => PageBody(
    title: '协议与调试',
    description: '分析 JT808 报文、调试 HTTP 请求与收发透传数据。',
    children: [
      entry(
        'JT808 报文分析',
        '表格与树状解析、字段原始数据、字节偏移和结果复制。',
        '打开报文分析工作台',
        Icons.manage_search,
        () => MessageAnalyzerPage(session: analyzerSession),
      ),
      entry(
        'HTTP 调试',
        '请求工作空间、查询参数与请求头、正文编辑、响应和原始字节。',
        '打开 HTTP 工作台',
        Icons.http,
        () => HttpWorkbenchPage(workspace: httpWorkspace),
      ),
      entry(
        '数据透传',
        'JT808 上下行透传、文本下发、多编码查看和原始字节记录。',
        '打开透传工作台',
        Icons.swap_horiz,
        () => PassthroughWorkbenchPage(
          model: widget.model,
          draft: passthroughDraft,
        ),
      ),
      entry(
        '音视频',
        'JT1078 通道配置与推流控制。',
        '打开 JT1078 工作台',
        Icons.video_camera_back_outlined,
        () => VideoPage(model: widget.model),
      ),
    ],
  );
}

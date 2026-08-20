using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using JT808.Protocol;
using JT808.Protocol.Enums;
using JT808.Protocol.Extensions;
using JT808.Protocol.MessageBody;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TerminalSimulation.Wpf.ViewModels
{
    public partial class MainViewModel
    {
        [RelayCommand]
        private void ClearLogs()
        {
            LogMessages.Clear();
            PassthroughMessages.Clear();
        }

        [RelayCommand]
        private void AnalyzeMessage()
        {
            try
            {
                AnalyzerResultTree.Clear();
                if (string.IsNullOrWhiteSpace(AnalyzerInputHex)) return;
                
                string hex = AnalyzerInputHex.Replace(" ", "").Replace("\r", "").Replace("\n", "");
                byte[] data = Convert.FromHexString(hex);
                
                string json = _protocolManager.Analyze(data);
                
                using (var doc = JsonDocument.Parse(json))
                {
                    var rootNode = ParseJsonElement("JT808 Package", doc.RootElement);
                    AnalyzerResultTree.Add(rootNode);
                    
                    AnalyzerResultTable.Clear();
                    int offset = 0;
                    TraverseJsonForTable("JT808 Package", doc.RootElement, ref offset);
                }
            }
            catch (Exception ex)
            {
                AnalyzerResultTree.Add(new AnalyzerNode { Name = "解析错误", Value = ex.Message });
            }
        }
        
        private AnalyzerNode ParseJsonElement(string name, JsonElement element)
        {
            var node = new AnalyzerNode { Name = TranslateKey(name) };
            
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    node.Children.Add(ParseJsonElement(prop.Name, prop.Value));
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var item in element.EnumerateArray())
                {
                    node.Children.Add(ParseJsonElement($"[{i}]", item));
                    i++;
                }
            }
            else
            {
                string rawValue = element.ToString() ?? "";
                node.Value = TranslateValue(name, rawValue);
            }
            
            return node;
        }

        private string TranslateKey(string key)
        {
            if (key.Contains("消息Id", StringComparison.OrdinalIgnoreCase)) return key.Replace("消息Id", "消息ID ", StringComparison.OrdinalIgnoreCase);
            if (key.Contains("车牌颜色")) return key.Replace("车牌颜色", "车牌颜色 ");
            
            return key switch
            {
                "MsgId" => "消息ID",
                "MsgNum" => "消息流水号",
                "TerminalPhoneNo" => "终端手机号",
                "Header" => "消息头",
                "MessageBodyProperty" => "消息体属性",
                "VersionFlag" => "版本标识",
                "Encrypt" => "加密方式",
                "DataLength" => "数据长度",
                "TerminalId" => "终端ID",
                "PlateColor" => "车牌颜色",
                "Bodies" => "消息体",
                "CheckCode" => "校验码",
                "JT808 Package" => "JT808 报文",
                _ => key
            };
        }

        private string TranslateMsgId(ushort msgId)
        {
            string hex = msgId.ToString("X4");
            string desc = hex switch
            {
                "0001" => "终端通用应答",
                "8001" => "平台通用应答",
                "0002" => "终端心跳",
                "8003" => "补传分包请求",
                "0100" => "终端注册",
                "8100" => "终端注册应答",
                "0102" => "终端鉴权",
                "0104" => "查询终端参数应答",
                "8103" => "设置终端参数",
                "8104" => "查询终端参数",
                "8105" => "终端控制",
                "8106" => "查询指定终端参数",
                "8107" => "查询终端属性",
                "0107" => "查询终端属性应答",
                "0108" => "终端升级结果通知",
                "0200" => "位置信息汇报",
                "0201" => "位置信息查询应答",
                "8201" => "位置信息查询",
                "8202" => "临时位置跟踪控制",
                "8203" => "人工确认报警消息",
                "8300" => "文本信息下发",
                "8301" => "事件设置",
                "0301" => "事件报告",
                "8302" => "提问下发",
                "0302" => "提问应答",
                "8303" => "信息点播菜单设置",
                "0303" => "信息点播/取消",
                "8304" => "信息服务",
                "8400" => "电话回拨",
                "8401" => "设置电话本",
                "8500" => "车辆控制",
                "0500" => "车辆控制应答",
                "8600" => "设置多边形区域",
                "8601" => "删除多边形区域",
                "8602" => "设置矩形区域",
                "8603" => "删除矩形区域",
                "8604" => "设置圆形区域",
                "8605" => "删除圆形区域",
                "8606" => "设置路线",
                "8607" => "删除路线",
                "8800" => "多媒体数据上传应答",
                "0800" => "多媒体事件信息上传",
                "0801" => "多媒体数据上传",
                "8801" => "摄像头立即拍摄命令",
                "0805" => "摄像头立即拍摄命令应答",
                "8802" => "存储多媒体数据检索",
                "0802" => "存储多媒体数据检索应答",
                "8803" => "存储多媒体数据上传",
                "8804" => "录音开始命令",
                "0900" => "数据上行透传",
                "8900" => "数据下行透传",
                "0901" => "数据压缩上报",
                "0A00" => "终端RSA公钥",
                "8A00" => "平台RSA公钥",
                _ => ""
            };
            return desc;
        }

        private string TranslateValue(string key, string value)
        {
            if (key.Contains("消息Id", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out int msgId))
            {
                string desc = TranslateMsgId((ushort)msgId);
                return $"{msgId} {desc}".Trim();
            }
            else if (key.Contains("车牌颜色") && int.TryParse(value, out int colorId))
            {
                string colorDesc = colorId switch
                {
                    1 => "蓝色",
                    2 => "黄色",
                    3 => "黑色",
                    4 => "白色",
                    5 => "绿色",
                    9 => "其他",
                    _ => "未指定"
                };
                return $"{colorId} {colorDesc}";
            }

            return value;
        }

        private void TraverseJsonForTable(string name, JsonElement element, ref int offset)
        {
            if (name == "JT808 Package")
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in element.EnumerateObject())
                    {
                        TraverseJsonForTable(prop.Name, prop.Value, ref offset);
                    }
                }
                return;
            }

            string hexData = "";
            string field = name;
            var match = System.Text.RegularExpressions.Regex.Match(name, @"^\[([0-9A-Fa-f]+|bit[0-9~]+)\](.*)$");
            if (match.Success)
            {
                hexData = match.Groups[1].Value;
                field = match.Groups[2].Value.Trim();
            }

            field = TranslateKey(field).Replace(" ", "");
            string rawValue = element.ValueKind != JsonValueKind.Object && element.ValueKind != JsonValueKind.Array ? element.ToString() ?? "" : "";
            string result = TranslateValue(name, rawValue);

            string offsetStr = "";
            string lengthStr = "";
            string dataType = "";

            if (!string.IsNullOrEmpty(hexData))
            {
                if (hexData.StartsWith("bit"))
                {
                    offsetStr = "-";
                    lengthStr = "bit";
                    dataType = "BIT";
                }
                else
                {
                    offsetStr = offset.ToString();
                    int len = hexData.Length / 2;
                    lengthStr = len.ToString();
                    dataType = len switch
                    {
                        1 => "BYTE",
                        2 => "WORD",
                        4 => "DWORD",
                        _ => "BYTES"
                    };
                    offset += len;
                }
            }
            
            if (!string.IsNullOrEmpty(hexData) || (!string.IsNullOrEmpty(rawValue) && element.ValueKind != JsonValueKind.Object && element.ValueKind != JsonValueKind.Array))
            {
                if (element.ValueKind != JsonValueKind.Object && element.ValueKind != JsonValueKind.Array)
                {
                    AnalyzerResultTable.Add(new AnalyzerTableRow
                    {
                        Index = AnalyzerResultTable.Count,
                        Field = field,
                        HexData = hexData,
                        DataType = dataType,
                        OffsetStr = offsetStr,
                        LengthStr = lengthStr,
                        Result = result
                    });
                }
            }

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    TraverseJsonForTable(prop.Name, prop.Value, ref offset);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var item in element.EnumerateArray())
                {
                    TraverseJsonForTable($"[{i}]", item, ref offset);
                    i++;
                }
            }
        }

        private void Log(string direction, string message)
        {
            _appLogger.Info(direction, message, _networkClient.ConnectionId);

            if (Application.Current != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    bool isRaw = message.StartsWith("RAW: ");
                    string rawHex = "";
                    string displayMsg = message;

                    if (isRaw)
                    {
                        rawHex = message.Substring(5).Trim();
                        displayMsg = message; // Keep RAW: prefix for visual clarity, or you can strip it
                    }

                    var item = new LogMessageItem
                    {
                        TimestampStr = $"[{DateTime.Now:HH:mm:ss.fff}]",
                        DirectionStr = $"[{direction}]",
                        Message = displayMsg,
                        HasRaw = isRaw,
                        RawData = rawHex
                    };

                    LogMessages.Add(item);

                    // Keep only the last 2000 log items to prevent memory issues
                    if (LogMessages.Count > 2000)
                    {
                        LogMessages.RemoveAt(0);
                    }
                });
            }
        }

        private void RemoveErrorProperties(JsonNode node)
        {
            if (node is JsonObject jObj)
            {
                jObj.Remove("解析外部部未知附加信息报错");
                jObj.Remove("解析异常");
                foreach (var kvp in jObj.ToArray())
                {
                    if (kvp.Value != null) RemoveErrorProperties(kvp.Value);
                }
            }
            else if (node is JsonArray jArr)
            {
                foreach (var item in jArr)
                {
                    if (item != null) RemoveErrorProperties(item);
                }
            }
        }

        private void NetworkClient_OnDataReceived(byte[] data)
        {
            if (!_protocolQueue.Writer.TryWrite(data.ToArray()))
            {
                Log("网络", "协议处理队列已关闭，收到的报文已忽略");
            }
        }

        private async Task ProcessProtocolQueueAsync(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var data in _protocolQueue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    await ProcessNetworkDataAsync(data).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Log("协议", $"串行处理任务异常: {ex.Message}");
            }
        }

        private async Task ProcessNetworkDataAsync(byte[] data)
        {
            string hexStr = data.ToHexString();
            Log("接收", $"RAW: {hexStr}");

            try
            {
                var package = _protocolManager.Deserialize(data);
                string desc = TranslateMsgId(package.Header.MsgId);
                if (!string.IsNullOrEmpty(desc))
                {
                    Log("接收", $"{desc}(0x{package.Header.MsgId:X4})");
                }

                string analysis = _protocolManager.Analyze(data);
                
                // 格式化解析出的 JSON 以提高可读性，包含中文支持
                try
                {
                    var options = new System.Text.Json.JsonSerializerOptions 
                    { 
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    };
                    var jNode = JsonNode.Parse(analysis);
                    if (jNode != null)
                    {
                        RemoveErrorProperties(jNode);
                        analysis = jNode.ToJsonString(options);
                    }
                }
                catch (JsonException) { } // 分析器也可能返回普通文本

                Log("接收解析", $"\n{analysis}");

                // 自动获取注册应答 (0x8100) 中的鉴权码
                if (package.Header.MsgId == 0x8100 && package.Bodies is JT808_0x8100 registerResponse)
                {
                    if (registerResponse.JT808TerminalRegisterResult == JT808TerminalRegisterResult.success)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            AuthCode = registerResponse.Code;
                            Log("系统", $"已自动获取鉴权码: {AuthCode}");
                            TerminalStatusText = "已注册，待鉴权";
                            TerminalStatusColor = "#2196F3"; // Blue
                        });
                    }
                    else
                    {
                        Log("系统", $"注册失败，错误码: {registerResponse.JT808TerminalRegisterResult}");
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            TerminalStatusText = $"注册失败: {registerResponse.JT808TerminalRegisterResult}";
                            TerminalStatusColor = "#F44336"; // Red
                        });
                    }
                }
                // 拦截查询终端属性 (0x8107)
                if (package.Header.MsgId == 0x8107)
                {
                    try
                        {
                            var header = new JT808Header
                            {
                                MsgId = 0x0107,
                                TerminalPhoneNo = TerminalPhoneNo,
                                MsgNum = 1,
                            };

                            var iccid = string.IsNullOrWhiteSpace(SimNumber) ? "" : SimNumber.Trim();
                            if (iccid.Length > 20) iccid = iccid[..20];

                            var body = new JT808_0x0107
                            {
                                TerminalType = 0,
                                MakerId = ManufacturerId,
                                TerminalModel = TerminalModel,
                                TerminalId = TerminalId,
                                Terminal_SIM_ICCID = iccid.PadLeft(20, '0'),
                                Terminal_Hardware_Version_Num = HardwareVersion,
                                Terminal_Firmware_Version_Num = UseAppVersionAsFirmwareVersion ? AppVersionInfo.FullVersion : FirmwareVersion,
                                GNSSModule = 1,
                                CommunicationModule = 1
                            };

                            var replyPackage = new JT808Package
                            {
                                Header = header,
                                Bodies = body
                            };

                            var version = UseJT808_2019 ? JT808Version.JTT2019 : JT808Version.JTT2013;
                            byte[] replyData = _protocolManager.Serialize(replyPackage, version);
                            await _networkClient.SendAsync(replyData);
                            Log("发送", $"自动应答 0x0107 查询终端属性");
                        }
                        catch (Exception ex)
                        {
                            Log("系统", $"发送 0x0107 应答异常: {ex.Message}");
                        }
                }
                
                // 拦截平台通用应答 (0x8001)
                if (package.Header.MsgId == 0x8001 && package.Bodies is JT808_0x8001 platformResponse)
                {
                    // 判断是否为终端鉴权 (0x0102) 的应答
                    if (platformResponse.AckMsgId == 0x0102)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (platformResponse.JT808PlatformResult == JT808.Protocol.Enums.JT808PlatformResult.succeed)
                            {
                                TerminalStatusText = "鉴权成功";
                                TerminalStatusColor = "#4CAF50"; // Green
                                Log("系统", "鉴权成功！");
                            }
                            else
                            {
                                TerminalStatusText = $"鉴权失败: {platformResponse.JT808PlatformResult}";
                                TerminalStatusColor = "#F44336"; // Red
                                Log("系统", $"鉴权失败: {platformResponse.JT808PlatformResult}");
                            }
                        });
                    }
                }
                
                // 拦截下行透传报文 (0x8900)
                if (package.Header.MsgId == 0x8900 && package.Bodies is JT808_0x8900 ptDown)
                {
                    // If serial port is open, write data to it
                    bool writeToSerialSuccess = false;
                    if (_serialPortService.IsOpen)
                    {
                        try
                        {
                            _serialPortService.Write(ptDown.PassthroughData);
                            writeToSerialSuccess = true;
                        }
                        catch (Exception ex)
                        {
                            Log("系统", $"接收数据写入串口失败: {ex.Message}");
                        }
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        PassthroughMessages.Add(new PassthroughMessage
                        {
                            IsFromServer = true,
                            Time = DateTime.Now.ToString("HH:mm:ss"),
                            TypeHex = ptDown.PassthroughType.ToString("X2"),
                            RawData = ptDown.PassthroughData,
                            Content = DecodePassthroughData(ptDown.PassthroughData, ChatEncodingIndex),
                            Label = writeToSerialSuccess ? "[平台 -> 串口]" : "[平台 -> 终端]"
                        });
                        TrimOldest(PassthroughMessages, MaxPassthroughMessages);
                    });

                    // 通用应答
                    await SendTerminalGeneralResponseAsync(package.Header.MsgId, package.Header.MsgNum, JT808.Protocol.Enums.JT808TerminalResult.Success);
                }
                // 拦截文本信息下发 (0x8300)
                else if (package.Header.MsgId == 0x8300 && package.Bodies is JT808_0x8300 textDown)
                {
                    // 保存原始字节用于后续重编码切换
                    byte[] rawBytes;
                    try
                    {
                        rawBytes = TextDownlinkEncodingIndex == 1
                            ? System.Text.Encoding.GetEncoding("GBK").GetBytes(textDown.TextInfo)
                            : System.Text.Encoding.UTF8.GetBytes(textDown.TextInfo ?? "");
                    }
                    catch
                    {
                        rawBytes = System.Text.Encoding.UTF8.GetBytes(textDown.TextInfo ?? "");
                    }

                    var msg = new TextDownlinkMessage
                    {
                        Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        Content = textDown.TextInfo ?? string.Empty,
                        Flag = textDown.TextFlag,
                        RawBytes = rawBytes
                    };

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        TextDownlinkMessages.Add(msg);
                        TrimOldest(TextDownlinkMessages, MaxTextDownlinkMessages);
                    });

                    // TTS语音播报
                    if (msg.IsTTS && EnableTTSPlayback)
                    {
                        string voiceName = SelectedTTSVoice;
                        string textToSpeak = textDown.TextInfo ?? string.Empty;
                        try { await _ttsService.SpeakAsync(textToSpeak, voiceName); }
                        catch (Exception ex) { Log("系统", $"TTS语音播报失败: {ex.Message}"); }
                    }

                    // 回复通用应答
                    await SendTerminalGeneralResponseAsync(package.Header.MsgId, package.Header.MsgNum, JT808.Protocol.Enums.JT808TerminalResult.Success);
                }
                // 拦截查询终端参数 (0x8104)
                else if (package.Header.MsgId == 0x8104)
                {
                    var replyBody = new JT808_0x0104
                    {
                        MsgNum = package.Header.MsgNum,
                        ParamList = new System.Collections.Generic.List<JT808_0x8103_BodyBase>()
                    };
                    
                    replyBody.ParamList.Add(new JT808_0x8103_0x0081 { ParamValue = ParseProvinceId(ProvinceIdInput, 11) });
                    replyBody.ParamList.Add(new JT808_0x8103_0x0082 { ParamValue = ParseCityId(CityIdInput, 1101) });
                    replyBody.ParamList.Add(new JT808_0x8103_0x0083 { ParamValue = PlateNo ?? "沪A88888" });
                    replyBody.ParamList.Add(new JT808_0x8103_0x0084 { ParamValue = PlateColor });

                    // 0x0075: 音视频参数
                    replyBody.ParamList.Add(new JT808.Protocol.Extensions.JT1078.MessageBody.JT808_0x8103_0x0075 
                    { 
                        RTS_EncodeMode = 0,
                        RTS_Resolution = 5,
                        RTS_KF_Interval = 250,
                        RTS_Target_FPS = 25,
                        RTS_Target_CodeRate = 0,
                        StreamStore_EncodeMode = 0,
                        StreamStore_Resolution = 5,
                        StreamStore_KF_Interval = 250,
                        StreamStore_Target_FPS = 25,
                        StreamStore_Target_CodeRate = 0,
                        OSD = 1,
                        AudioOutputEnabled = 0
                    });

                    // 0x0076: 音视频通道列表设置
                    var avChannels = new System.Collections.Generic.List<JT808.Protocol.Extensions.JT1078.MessageBody.JT808_0x8103_0x0076_AVChannelRefTable>();
                    foreach (var c in VideoChannels)
                    {
                        avChannels.Add(new JT808.Protocol.Extensions.JT1078.MessageBody.JT808_0x8103_0x0076_AVChannelRefTable 
                        {
                            PhysicalChannelNo = c.LogicalChannelNo,
                            LogicChannelNo = c.LogicalChannelNo,
                            ChannelType = 0, // 0:音视频
                            IsConnectCloudPlat = 0
                        });
                    }
                    replyBody.ParamList.Add(new JT808.Protocol.Extensions.JT1078.MessageBody.JT808_0x8103_0x0076 
                    {
                        AVChannelTotal = (byte)avChannels.Count,
                        AudioChannelTotal = 0,
                        VudioChannelTotal = (byte)avChannels.Count,
                        AVChannelRefTables = avChannels
                    });

                    // 0x0077: 单独视频通道参数设置
                    var signalChannels = new System.Collections.Generic.List<JT808.Protocol.Extensions.JT1078.MessageBody.JT808_0x8103_0x0077_SignalChannel>();
                    foreach (var c in VideoChannels)
                    {
                        signalChannels.Add(new JT808.Protocol.Extensions.JT1078.MessageBody.JT808_0x8103_0x0077_SignalChannel
                        {
                            LogicChannelNo = c.LogicalChannelNo,
                            RTS_EncodeMode = 0,
                            RTS_Resolution = 5,
                            RTS_KF_Interval = 250,
                            RTS_Target_FPS = 25,
                            RTS_Target_CodeRate = 0,
                            StreamStore_EncodeMode = 0,
                            StreamStore_Resolution = 5,
                            StreamStore_KF_Interval = 250,
                            StreamStore_Target_FPS = 25,
                            StreamStore_Target_CodeRate = 0,
                            OSD = 1
                        });
                    }
                    replyBody.ParamList.Add(new JT808.Protocol.Extensions.JT1078.MessageBody.JT808_0x8103_0x0077 
                    {
                        NeedSetChannelTotal = (byte)signalChannels.Count,
                        SignalChannels = signalChannels
                    });
                    
                    var replyPackage = new JT808Package
                    {
                        Header = new JT808Header
                        {
                            MsgId = 0x0104,
                            MsgNum = 0, // Using 0 as default or we can keep track of SN
                            TerminalPhoneNo = TerminalPhoneNo,
                        },
                        Bodies = replyBody
                    };
                    
                    try
                        {
                            var version = UseJT808_2019 ? JT808Version.JTT2019 : JT808Version.JTT2013;
                            byte[] replyData = _protocolManager.Serialize(replyPackage, version);
                            await _networkClient.SendAsync(replyData);
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                Log("发送", $"查询终端参数应答(0x0104)，响应流水号: {package.Header.MsgNum}，参数个数: {replyBody.ParamList.Count}");
                                Log("发送", $"RAW: {replyData.ToHexString()}");
                                try { Log("解析", _protocolManager.Analyze(replyData)); }
                                catch (Exception ex) { _appLogger.Error("协议", "分析自动应答失败", ex, _networkClient.ConnectionId, messageId: 0x0104); }
                            });
                        }
                        catch (Exception ex)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                Log("发送异常", $"0x0104序列化失败: {ex.Message}");
                            });
                        }
                }
                // 拦截音视频传输请求 (0x9101)
                else if (package.Header.MsgId == 0x9101)
                {
                    try
                    {
                        var body = package.Bodies as JT808.Protocol.Extensions.JT1078.MessageBody.JT808_0x9101;
                        if (body != null)
                        {
                            string ip = body.ServerIp;
                            int port = body.TcpPort > 0 ? body.TcpPort : body.UdpPort;
                            byte channel = body.ChannelNo;
                            
                            Log("音视频", $"收到 0x9101 实时传输请求: 通道={channel}, IP={ip}:{port}, 数据类型={body.DataType}");

                            var videoItem = VideoChannels.FirstOrDefault(c => c.LogicalChannelNo == channel);
                            if (videoItem != null)
                            {
                                await StartVideoPushingAsync(videoItem, ip, port, body.DataType);
                            }
                        }

                        await SendTerminalGeneralResponseAsync(package.Header.MsgId, package.Header.MsgNum, JT808.Protocol.Enums.JT808TerminalResult.Success);
                    }
                    catch (Exception ex)
                    {
                        Log("异常", $"解析 0x9101 失败: {ex.Message}");
                    }
                }
                // 拦截音视频传输控制 (0x9102)
                else if (package.Header.MsgId == 0x9102)
                {
                    try
                    {
                        var body = package.Bodies as JT808.Protocol.Extensions.JT1078.MessageBody.JT808_0x9102;
                        if (body != null)
                        {
                            byte channel = body.ChannelNo;
                            int ctrlCmd = body.ControlCmd;
                            
                            Log("音视频控制", $"收到 0x9102 传输控制: 通道={channel}, 命令={ctrlCmd}");

                            if (ctrlCmd == 0) // 0表示关闭音视频传输
                            {
                                if (channel == 0)
                                {
                                    // 通道号为0表示操作所有通道
                                    foreach (var ch in VideoChannels)
                                    {
                                        ch.StopPushing();
                                    }
                                }
                                else
                                {
                                    var videoItem = VideoChannels.FirstOrDefault(c => c.LogicalChannelNo == channel);
                                    if (videoItem != null)
                                    {
                                        videoItem.StopPushing();
                                    }
                                }
                            }
                        }

                        await SendTerminalGeneralResponseAsync(package.Header.MsgId, package.Header.MsgNum, JT808.Protocol.Enums.JT808TerminalResult.Success);
                    }
                    catch (Exception ex)
                    {
                        Log("异常", $"解析 0x9102 失败: {ex.Message}");
                    }
                }
                // 拦截其他需要通用应答的下行指令
                else if (package.Header.MsgId != 0x8100 && package.Header.MsgId != 0x8001 && package.Header.MsgId.ToString("X4").StartsWith("8"))
                {
                    // 大部分8开头的消息（除了8100注册应答、8001平台通用应答等特定消息）默认回复终端通用应答
                    // 在这里做一个保守的默认回复机制
                    await SendTerminalGeneralResponseAsync(package.Header.MsgId, package.Header.MsgNum, JT808.Protocol.Enums.JT808TerminalResult.Success);
                }
            }
            catch (Exception ex)
            {
                Log("解析异常", ex.Message);
            }
        }

        private async Task StartVideoPushingAsync(VideoChannelItem videoItem, string ip, int port, int dataType)
        {
            try
            {
                await videoItem.StartPushingAsync(ip, port, TerminalPhoneNo, dataType, AudioCodecIndex);
            }
            catch (Exception ex)
            {
                Log("音视频", $"通道 {videoItem.LogicalChannelNo} 推流失败: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task ConnectAsync()
        {
            if (IsConnected) return;

            var addr = $"{ServerIp}:{ServerPort}";
            int existingIndex = ServerAddressHistory.IndexOf(addr);
            if (existingIndex < 0)
            {
                ServerAddressHistory.Insert(0, addr);
                if (ServerAddressHistory.Count > 10) ServerAddressHistory.RemoveAt(ServerAddressHistory.Count - 1);
            }
            else if (existingIndex > 0)
            {
                ServerAddressHistory.Move(existingIndex, 0);
            }
            ServerAddressInput = addr; // Ensure the UI maintains the text
            
            SaveConfigDebounced();

            ConsoleLogger.LogAction("连接服务器", $"IP={ServerIp}, 端口={ServerPort}");
            try
            {
                Log("系统", $"正在连接 {ServerIp}:{ServerPort}...");
                await _networkClient.ConnectAsync(ServerIp, ServerPort);
                IsConnected = true;
                TerminalStatusText = "已连接 (未注册)";
                TerminalStatusColor = "#FF9800"; // Orange
                Log("系统", "连接成功！");
                StartHeartbeatLoop();
            }
            catch (Exception ex)
            {
                Log("系统", $"连接失败：{ex.Message}");
            }
        }

        [RelayCommand]
        private async Task DisconnectAsync()
        {
            ConsoleLogger.LogAction("断开连接", "正在断开与服务器的连接");
            await _networkClient.DisconnectAsync();
            IsConnected = false;
            TerminalStatusText = "未连接";
            TerminalStatusColor = "Gray";
            _heartbeatCts?.Cancel();
        }

        internal static byte[] AppendRawBytesToJT808Package(byte[] data, byte[] rawAttach)
        {
            if (rawAttach == null || rawAttach.Length == 0) return data;

            // 1. Remove 7E markers
            if (data[0] != 0x7E || data[data.Length - 1] != 0x7E) return data;
            var escaped = new byte[data.Length - 2];
            Array.Copy(data, 1, escaped, 0, data.Length - 2);

            // 2. Unescape
            var unescapedList = new System.Collections.Generic.List<byte>();
            for (int i = 0; i < escaped.Length; i++)
            {
                if (escaped[i] == 0x7D && i + 1 < escaped.Length)
                {
                    if (escaped[i + 1] == 0x01) { unescapedList.Add(0x7D); i++; }
                    else if (escaped[i + 1] == 0x02) { unescapedList.Add(0x7E); i++; }
                    else unescapedList.Add(escaped[i]);
                }
                else
                {
                    unescapedList.Add(escaped[i]);
                }
            }
            var unescaped = unescapedList.ToArray();

            // 3. Extract parts
            // Header (unknown length due to variable phone length in 2019, but we know body length)
            ushort msgProps = (ushort)((unescaped[2] << 8) | unescaped[3]);
            int oldBodyLength = msgProps & 0x03FF;
            int newBodyLength = oldBodyLength + rawAttach.Length;
            if (newBodyLength > 0x03FF)
            {
                throw new InvalidOperationException($"JT808 消息体长度 {newBodyLength} 超过 1023 字节，必须使用分包发送。");
            }
            
            // Update Body Length in properties
            msgProps = (ushort)((msgProps & ~0x03FF) | newBodyLength);
            unescaped[2] = (byte)(msgProps >> 8);
            unescaped[3] = (byte)(msgProps & 0xFF);

            // Insert raw bytes before checksum
            var newUnescaped = new byte[unescaped.Length + rawAttach.Length];
            // Copy everything except the old checksum
            Array.Copy(unescaped, 0, newUnescaped, 0, unescaped.Length - 1);
            // Insert rawAttach
            Array.Copy(rawAttach, 0, newUnescaped, unescaped.Length - 1, rawAttach.Length);

            // 4. Recalculate checksum
            byte checksum = 0;
            for (int i = 0; i < newUnescaped.Length - 1; i++)
            {
                checksum ^= newUnescaped[i];
            }
            newUnescaped[newUnescaped.Length - 1] = checksum;

            // 5. Escape
            var newEscapedList = new System.Collections.Generic.List<byte>();
            newEscapedList.Add(0x7E); // Start
            for (int i = 0; i < newUnescaped.Length; i++)
            {
                if (newUnescaped[i] == 0x7E)
                {
                    newEscapedList.Add(0x7D);
                    newEscapedList.Add(0x02);
                }
                else if (newUnescaped[i] == 0x7D)
                {
                    newEscapedList.Add(0x7D);
                    newEscapedList.Add(0x01);
                }
                else
                {
                    newEscapedList.Add(newUnescaped[i]);
                }
            }
            newEscapedList.Add(0x7E); // End

            return newEscapedList.ToArray();
        }

        private async Task SendPackageAsync<T>(JT808Package package, byte[]? rawAppendBytes = null) where T : JT808Bodies
        {
            if (!IsConnected)
            {
                Log("系统", "请先连接服务器！");
                return;
            }

            try
            {
                JT808Version version = UseJT808_2019 ? JT808Version.JTT2019 : JT808Version.JTT2013;
                byte[] data = _protocolManager.Serialize<T>(package, version);
                
                if (rawAppendBytes != null && rawAppendBytes.Length > 0)
                {
                    data = AppendRawBytesToJT808Package(data, rawAppendBytes);
                }
                
                Log("发送", $"RAW: {data.ToHexString()}");
                string analysis = _protocolManager.Analyze(data);
                try
                {
                    var options = new System.Text.Json.JsonSerializerOptions 
                    { 
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    };
                    var jNode = JsonNode.Parse(analysis);
                    if (jNode != null)
                    {
                        RemoveErrorProperties(jNode);
                        analysis = jNode.ToJsonString(options);
                    }
                }
                catch (JsonException) { }

                Log("发送解析", $"\n{analysis}");

                await _networkClient.SendAsync(data);
            }
            catch (Exception ex)
            {
                Log("系统", $"发送异常: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task RegisterAsync()
        {
            ConsoleLogger.LogAction("终端注册", $"手机号={TerminalPhoneNo}, 省域={ProvinceIdInput}, 市县={CityIdInput}, 制造商ID={ManufacturerId}, 终端型号={TerminalModel}, 终端ID={TerminalId}, 车牌号={PlateNo}, 车牌颜色={PlateColor}");
            var header = new JT808Header
            {
                MsgId = 0x0100,
                TerminalPhoneNo = TerminalPhoneNo,
                MsgNum = 1,
            };

            var body = new JT808_0x0100
            {
                AreaID = ParseProvinceId(ProvinceIdInput, 0),
                CityOrCountyId = ParseCityId(CityIdInput, 0),
                MakerId = ManufacturerId,
                TerminalId = TerminalId,
                TerminalModel = TerminalModel,
                PlateColor = PlateColor,
                PlateNo = PlateNo
            };

            var package = new JT808Package
            {
                Header = header,
                Bodies = body
            };

            await SendPackageAsync<JT808_0x0100>(package);
        }

        [RelayCommand]
        private async Task AuthAsync()
        {
            ConsoleLogger.LogAction("终端鉴权", $"手机号={TerminalPhoneNo}, 鉴权码={AuthCode}");
            if (string.IsNullOrEmpty(AuthCode))
            {
                Log("系统", "鉴权码不能为空！请先注册获取或手动输入。");
                return;
            }

            var header = new JT808Header
            {
                MsgId = 0x0102,
                TerminalPhoneNo = TerminalPhoneNo,
                MsgNum = 2,
            };

            var body = new JT808_0x0102
            {
                Code = AuthCode,
                IMEI = "123456789012345",
                SoftwareVersion = "V1.0.0"
            };

            var package = new JT808Package
            {
                Header = header,
                Bodies = body
            };

            await SendPackageAsync<JT808_0x0102>(package);
        }

        [RelayCommand]
        private async Task ReportLocationAsync()
        {
            var snapshot = CaptureLocationReportSnapshot();
            ConsoleLogger.LogAction("发送位置汇报", $"手机号={snapshot.TerminalPhoneNo}, 经度={snapshot.Longitude}, 纬度={snapshot.Latitude}, 速度={snapshot.Speed}, 方向={snapshot.Direction}, 高程={snapshot.Altitude}, 报警标志=0x{snapshot.AlarmFlag:X8}, 状态标志=0x{snapshot.StatusFlag:X8}");

            var header = new JT808Header
            {
                MsgId = 0x0200,
                TerminalPhoneNo = snapshot.TerminalPhoneNo,
                MsgNum = 3,
            };

            var body = new JT808_0x0200
            {
                AlarmFlag = snapshot.AlarmFlag,
                StatusFlag = snapshot.StatusFlag,
                Lat = (int)(snapshot.Latitude * 1000000),
                Lng = (int)(snapshot.Longitude * 1000000),
                Altitude = (ushort)snapshot.Altitude,
                Speed = (ushort)(snapshot.Speed * 10),
                Direction = (ushort)snapshot.Direction,
                GPSTime = DateTime.Now,
                UnknownLocationAttachData = new Dictionary<ushort, byte[]>()
            };

            // 标准附加信息注入 (直接转为RAW字节追加，避免依赖底层库的序列化兼容性问题)
            var standardAttachBytes = new System.Collections.Generic.List<byte>();
            if (snapshot.Enable0x01)
            {
                standardAttachBytes.Add(0x01); standardAttachBytes.Add(0x04);
                standardAttachBytes.AddRange(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder((int)snapshot.Mileage0x01)));
            }
            if (snapshot.Enable0x02)
            {
                standardAttachBytes.Add(0x02); standardAttachBytes.Add(0x02);
                standardAttachBytes.AddRange(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder((short)snapshot.Oil0x02)));
            }
            if (snapshot.Enable0x03)
            {
                standardAttachBytes.Add(0x03); standardAttachBytes.Add(0x02);
                standardAttachBytes.AddRange(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder((short)snapshot.Speed0x03)));
            }
            if (snapshot.Enable0x04)
            {
                standardAttachBytes.Add(0x04); standardAttachBytes.Add(0x02);
                standardAttachBytes.AddRange(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder((short)snapshot.AlarmEventId0x04)));
            }
            if (snapshot.Enable0x25)
            {
                standardAttachBytes.Add(0x25); standardAttachBytes.Add(0x04);
                standardAttachBytes.AddRange(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder((int)snapshot.ExtVehicleSignal0x25)));
            }
            if (snapshot.Enable0x2A)
            {
                standardAttachBytes.Add(0x2A); standardAttachBytes.Add(0x02);
                standardAttachBytes.AddRange(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder((short)snapshot.IOStatus0x2A)));
            }
            if (snapshot.Enable0x2B)
            {
                standardAttachBytes.Add(0x2B); standardAttachBytes.Add(0x04);
                int analog = (snapshot.AnalogAD1 << 16) | snapshot.AnalogAD0;
                standardAttachBytes.AddRange(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(analog)));
            }
            if (snapshot.Enable0x30)
            {
                standardAttachBytes.Add(0x30); standardAttachBytes.Add(0x01);
                standardAttachBytes.Add(snapshot.NetworkSignal0x30);
            }
            if (snapshot.Enable0x31)
            {
                standardAttachBytes.Add(0x31); standardAttachBytes.Add(0x01);
                standardAttachBytes.Add(snapshot.GNSSCount0x31);
            }

            // 自定义 Hex 透传 (格式：ID|Length|Data 或 ID|Data，支持逗号分隔多个)
            var rawAppendBytesList = new System.Collections.Generic.List<byte>();
            Log("系统", $"开始处理位置汇报，当前配置附加项数量: {snapshot.CustomAttachItems.Count}");
            foreach (var attach in snapshot.CustomAttachItems)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(attach.AttachId))
                    {
                        string idStr = attach.AttachId.Replace(" ", "").Replace("-", "").Replace("|", "");
                        if (!string.IsNullOrEmpty(idStr)) rawAppendBytesList.AddRange(idStr.ToHexBytes());
                    }
                    
                    if (!string.IsNullOrWhiteSpace(attach.AttachLength))
                    {
                        string lenStr = attach.AttachLength.Replace(" ", "").Replace("-", "").Replace("|", "");
                        if (!string.IsNullOrEmpty(lenStr)) rawAppendBytesList.AddRange(lenStr.ToHexBytes());
                    }
                    
                    if (!string.IsNullOrWhiteSpace(attach.AttachData))
                    {
                        string dataStr = attach.AttachData.Replace(" ", "").Replace("-", "").Replace("|", "");
                        if (!string.IsNullOrEmpty(dataStr)) rawAppendBytesList.AddRange(dataStr.ToHexBytes());
                    }
                }
                catch (Exception ex)
                {
                    Log("系统", $"自定义附加字段[{attach.AttachId}]解析失败: {ex.Message}");
                }
            }
            
            TriggerOnLocationReporting(rawAppendBytesList);
            
            if (standardAttachBytes.Count > 0)
            {
                rawAppendBytesList.InsertRange(0, standardAttachBytes);
            }

            Log("系统", $"最终拼接的附加数据(RAW Hex): {(rawAppendBytesList.Count > 0 ? rawAppendBytesList.ToArray().ToHexString() : "无")}");

            var package = new JT808Package
            {
                Header = header,
                Bodies = body
            };

            await SendPackageAsync<JT808_0x0200>(package, rawAppendBytesList.Count > 0 ? rawAppendBytesList.ToArray() : null);
        }

        private async Task SendTerminalGeneralResponseAsync(ushort replyMsgId, ushort replyMsgNum, JT808.Protocol.Enums.JT808TerminalResult result)
        {
            try
            {
                var header = new JT808Header
                {
                    MsgId = 0x0001,
                    TerminalPhoneNo = TerminalPhoneNo,
                    MsgNum = 0 // Will be set in SendPackageAsync if needed, or explicitly increment if managing sequence here
                };

                var body = new JT808_0x0001
                {
                    ReplyMsgId = replyMsgId,
                    ReplyMsgNum = replyMsgNum,
                    TerminalResult = result
                };

                var package = new JT808Package
                {
                    Header = header,
                    Bodies = body
                };

                Log("系统", $"发送终端通用应答(0x0001)，响应消息ID: 0x{replyMsgId:X4}，流水号: {replyMsgNum}");
                await SendPackageAsync<JT808_0x0001>(package);
            }
            catch (Exception ex)
            {
                Log("系统", $"发送通用应答失败: {ex.Message}");
            }
        }

    }
}

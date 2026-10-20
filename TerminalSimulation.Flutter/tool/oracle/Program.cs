using System.Text;
using System.Text.Json;
using JT808.Protocol;
using JT808.Protocol.Enums;
using JT808.Protocol.MessageBody;
using JT808.Protocol.Extensions.JT1078.MessageBody;
using TerminalSimulation.Protocol;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var manager = new JT808Manager();
var samples = new List<object>();
foreach (var version in new[] { JT808Version.JTT2013, JT808Version.JTT2019 })
{
    void Sample(string name, ushort id, ushort sequence, JT808Bodies? body)
    {
        var header = new JT808Header { MsgId = id, TerminalPhoneNo = "013800000001", MsgNum = sequence };
        var packet = new JT808Package { Header = header, Bodies = body };
        var bytes = manager.Serialize(packet, version);
        samples.Add(new { name, version = version == JT808Version.JTT2013 ? "v2013" : "v2019", id, sequence, phone = "013800000001", hex = Convert.ToHexString(bytes) });
    }
    Sample("heartbeat", 2, 1, null);
    Sample("register", 0x100, 2, new JT808_0x0100 { AreaID = 11, CityOrCountyId = 1101, MakerId = "CTS", TerminalModel = "TerminalSimulation", TerminalId = "0000001", PlateColor = 1, PlateNo = "京A00001" });
    Sample("auth", 0x102, 3, new JT808_0x0102 { Code = "AUTH", IMEI = "123456789012345", SoftwareVersion = "V1.0.0" });
    Sample("location", 0x200, 4, new JT808_0x0200 { AlarmFlag = 4, StatusFlag = 3, Lat = 39900000, Lng = 116400000, Altitude = 30, Speed = 605, Direction = 90, GPSTime = new DateTime(2026, 10, 19, 12, 34, 56) });
    Sample("attributes", 0x107, 5, new JT808_0x0107 { TerminalType = 0, MakerId = "CTS", TerminalModel = "TerminalSimulation", TerminalId = "0000001", Terminal_SIM_ICCID = "89860000000000000000", Terminal_Hardware_Version_Num = "1.0", Terminal_Firmware_Version_Num = "V1.0.0", GNSSModule = 1, CommunicationModule = 1 });
    var parameters = new JT808_0x0104 { MsgNum = 100, ParamList = new List<JT808_0x8103_BodyBase>() };
    parameters.ParamList.Add(new JT808_0x8103_0x0081 { ParamValue = 11 });
    parameters.ParamList.Add(new JT808_0x8103_0x0082 { ParamValue = 1101 });
    parameters.ParamList.Add(new JT808_0x8103_0x0083 { ParamValue = "京A00001" });
    parameters.ParamList.Add(new JT808_0x8103_0x0084 { ParamValue = 1 });
    parameters.ParamList.Add(new JT808_0x8103_0x0075 { RTS_EncodeMode=0, RTS_Resolution=5, RTS_KF_Interval=250, RTS_Target_FPS=25, RTS_Target_CodeRate=0, StreamStore_EncodeMode=0, StreamStore_Resolution=5, StreamStore_KF_Interval=250, StreamStore_Target_FPS=25, StreamStore_Target_CodeRate=0, OSD=1, AudioOutputEnabled=0 });
    parameters.ParamList.Add(new JT808_0x8103_0x0076 { AVChannelTotal=36, AudioChannelTotal=0, VudioChannelTotal=36, AVChannelRefTables=Enumerable.Range(1,36).Select(i=>new JT808_0x8103_0x0076_AVChannelRefTable { PhysicalChannelNo=(byte)i, LogicChannelNo=(byte)i, ChannelType=0, IsConnectCloudPlat=0 }).ToList() });
    parameters.ParamList.Add(new JT808_0x8103_0x0077 { NeedSetChannelTotal=36, SignalChannels=Enumerable.Range(1,36).Select(i=>new JT808_0x8103_0x0077_SignalChannel { LogicChannelNo=(byte)i, RTS_EncodeMode=0, RTS_Resolution=5, RTS_KF_Interval=250, RTS_Target_FPS=25, RTS_Target_CodeRate=0, StreamStore_EncodeMode=0, StreamStore_Resolution=5, StreamStore_KF_Interval=250, StreamStore_Target_FPS=25, StreamStore_Target_CodeRate=0, OSD=1 }).ToList() });
    Sample("parameters", 0x104, 6, parameters);
}
if (args.Length != 1) throw new ArgumentException("Supply output fixture path.");
File.WriteAllText(args[0], JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
Console.WriteLine($"Wrote {samples.Count} original CTS serializer samples.");
var avSamples = new List<object>();
foreach (var (name, label2, label3) in new[] { ("video", 226, 0), ("audio", 134, 48) })
{
    var packet = new JT1078.Protocol.JT1078Package {
        Label1 = new JT1078.Protocol.JT1078Label1(0x80),
        Label2 = new JT1078.Protocol.JT1078Label2((byte)label2),
        Label3 = new JT1078.Protocol.JT1078Label3((byte)label3),
        SIM = "013800000001", LogicChannelNumber = 1, SN = 65535,
        Timestamp = 1792368000000, LastIFrameInterval = 80, LastFrameInterval = 40,
        Bodies = new byte[] { 0, 0, 0, 1, 0x65, 0x88 }
    };
    avSamples.Add(new { name, hex = Convert.ToHexString(JT1078.Protocol.JT1078Serializer.Serialize(packet)) });
}
File.WriteAllText(Path.Combine(Path.GetDirectoryName(args[0])!, "jt1078-csharp.json"), JsonSerializer.Serialize(avSamples, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));

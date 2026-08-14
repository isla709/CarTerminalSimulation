using System;
using System.Collections.Generic;
using JT808.Protocol;
using JT808.Protocol.Enums;
using JT808.Protocol.Extensions;
using JT808.Protocol.Interfaces;
using JT808.Protocol.MessageBody;
using JT808.Protocol.Extensions.JT1078;
using Microsoft.Extensions.DependencyInjection;

namespace TerminalSimulation.Protocol
{
    public class JT808Manager
    {
        private readonly IJT808Config _config;
        private readonly JT808Serializer _serializer;

        public JT808Manager()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddJT808Configure().AddJT1078Configure();
            var serviceProvider = services.BuildServiceProvider();
            _config = serviceProvider.GetRequiredService<IJT808Config>();
            _serializer = _config.GetSerializer();
        }

        public System.Text.Encoding Encoding
        {
            get => _config.Encoding;
            set => _config.Encoding = value;
        }

        public byte[] Serialize<T>(JT808Package package, JT808Version version = JT808Version.JTT2013) where T : JT808Bodies
        {
            package.Header.MessageBodyProperty.VersionFlag = version == JT808Version.JTT2019;
            return _serializer.Serialize(package, version);
        }

        public byte[] Serialize(JT808Package package, JT808Version version = JT808Version.JTT2013)
        {
            package.Header.MessageBodyProperty.VersionFlag = version == JT808Version.JTT2019;
            return _serializer.Serialize(package, version);
        }

        public JT808Package Deserialize(byte[] bytes)
        {
            return _serializer.Deserialize(bytes);
        }
        
        public string Analyze(byte[] bytes)
        {
             string json = _serializer.Analyze(bytes);
             try
             {
                 var node = System.Text.Json.Nodes.JsonNode.Parse(json);
                 if (node != null)
                 {
                     ModifyIOStatusNode(node);
                     var options = new System.Text.Json.JsonSerializerOptions 
                     { 
                         WriteIndented = true,
                         Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                     };
                     return node.ToJsonString(options);
                 }
             }
             catch (System.Text.Json.JsonException) { }
             return json;
        }

        private void ModifyIOStatusNode(System.Text.Json.Nodes.JsonNode node)
        {
            if (node is System.Text.Json.Nodes.JsonObject obj)
            {
                if (obj.TryGetPropertyValue("IO状态位对象信息", out var ioStatusNode) && ioStatusNode is System.Text.Json.Nodes.JsonObject ioStatusObj)
                {
                    if (ioStatusObj.TryGetPropertyValue("值", out var valNode) && valNode != null)
                    {
                        string binStr = valNode.GetValue<string>();
                        if (binStr != null && binStr.Length == 16)
                        {
                            bool hasSignal = false;
                            for (int bit = 2; bit <= 15; bit++)
                            {
                                if (binStr[15 - bit] == '1')
                                {
                                    hasSignal = true;
                                    break;
                                }
                            }

                            if (hasSignal)
                            {
                                ioStatusObj.Remove("bit2~15");
                                for (int bit = 15; bit >= 2; bit--)
                                {
                                    char c = binStr[15 - bit];
                                    string status = c == '1' ? "有信号" : "无信号";
                                    ioStatusObj.Add($"bit{bit}(GPIO{bit})", status);
                                }
                            }
                        }
                    }
                }
                foreach (var prop in obj)
                {
                    if (prop.Value != null)
                    {
                        ModifyIOStatusNode(prop.Value);
                    }
                }
            }
            else if (node is System.Text.Json.Nodes.JsonArray arr)
            {
                foreach (var item in arr)
                {
                    if (item != null)
                    {
                        ModifyIOStatusNode(item);
                    }
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using JT808.Protocol;
using JT808.Protocol.Enums;
using JT808.Protocol.Extensions;
using JT808.Protocol.Interfaces;
using JT808.Protocol.MessageBody;
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
            services.AddJT808Configure();
            var serviceProvider = services.BuildServiceProvider();
            _config = serviceProvider.GetRequiredService<IJT808Config>();
            _serializer = _config.GetSerializer();
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
             return _serializer.Analyze(bytes);
        }
    }
}

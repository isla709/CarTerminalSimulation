using System;
using System.Collections.Generic;
using JT808.Protocol;
using JT808.Protocol.MessageBody;
using JT808.Protocol.Extensions;
using Microsoft.Extensions.DependencyInjection;

class Program
{
    static void Main()
    {
        IServiceCollection services = new ServiceCollection();
        services.AddJT808Configure();
        var sp = services.BuildServiceProvider();
        var config = sp.GetRequiredService<IJT808Config>();
        var serializer = config.GetSerializer();

        var msg = new JT808_0x0200
        {
            Lat = 1000000,
            Lng = 1000000,
            BasicLocationAttachData = new Dictionary<byte, JT808_0x0200_BodyBase>
            {
                { 0x01, new JT808_0x0200_0x01 { Mileage = 12345 } }
            }
        };
        
        var package = new JT808Package
        {
            Header = new JT808Header { MsgId = 0x0200, TerminalPhoneNo = "123456789012" },
            Bodies = msg
        };

        var bytes = serializer.Serialize(package);
        Console.WriteLine("Serialized bytes: " + bytes.ToHexString());
    }
}

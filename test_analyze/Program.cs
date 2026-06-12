using System;
using JT808.Protocol.MessageBody;

class P {
    static void Main() {
        Console.WriteLine("0x0081:");
        foreach(var p in typeof(JT808_0x8103_0x0081).GetProperties()) Console.WriteLine(p.Name + " : " + p.PropertyType.Name);
        Console.WriteLine("0x0082:");
        foreach(var p in typeof(JT808_0x8103_0x0082).GetProperties()) Console.WriteLine(p.Name + " : " + p.PropertyType.Name);
        Console.WriteLine("0x0083:");
        foreach(var p in typeof(JT808_0x8103_0x0083).GetProperties()) Console.WriteLine(p.Name + " : " + p.PropertyType.Name);
        Console.WriteLine("0x0084:");
        foreach(var p in typeof(JT808_0x8103_0x0084).GetProperties()) Console.WriteLine(p.Name + " : " + p.PropertyType.Name);
    }
}

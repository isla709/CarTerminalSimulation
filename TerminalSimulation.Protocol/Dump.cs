using System;
using System.Reflection;
using JT808.Protocol.MessageBody;

class Program
{
    static void Main()
    {
        var type = typeof(JT808_0x0200_CustomBodyBase);
        Console.WriteLine("Type: " + type.FullName);
        foreach (var prop in type.GetProperties())
        {
            Console.WriteLine($"Prop: {prop.Name} (Virtual: {prop.GetMethod?.IsVirtual})");
        }
        foreach (var method in type.GetMethods())
        {
            if (method.DeclaringType == type)
                Console.WriteLine($"Method: {method.Name}");
        }
        
        var enumType = typeof(JT808.Protocol.Enums.JT808Version);
        Console.WriteLine("JT808Version Enum: " + (enumType != null));
    }
}

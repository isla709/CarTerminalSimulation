using System;
using JT808.Protocol.Enums;

namespace DumpApp
{
    class Program
    {
        static void Main(string[] args)
        {
            foreach(var name in Enum.GetNames(typeof(JT808TerminalResult)))
            {
                Console.WriteLine(name);
            }
        }
    }
}

using System;
using System.IO;
using System.Collections.Generic;

class Program {
    static void Main() {
        string path = @"E:\Project\CarTerminalSimulation\TerminalSimulation.Wpf\bin\Debug\net8.0-windows\win-x64\h264\Rainbow 6 Siege 02-14-2026 23-27-39-532_1.h264";
        byte[] data = new byte[1024 * 1024]; // Read first 1MB
        using (var fs = File.OpenRead(path)) {
            fs.Read(data, 0, data.Length);
        }

        var nalus = SplitNalus(data);
        for(int i=0; i<Math.Min(100, nalus.Count); i++) {
            var nalu = nalus[i];
            int type = nalu.Length > 4 ? nalu[4] & 0x1F : 0;
            Console.WriteLine($"NALU {i}: Length={nalu.Length}, Type={type}");
        }
    }

    static List<byte[]> SplitNalus(byte[] data)
    {
        var nalus = new List<byte[]>();
        int i = 0;
        int lastNaluStart = -1;
        int lastNaluStartCodeLength = 0;

        while (i < data.Length - 2)
        {
            if (data[i] == 0x00 && data[i + 1] == 0x00 && data[i + 2] == 0x01)
            {
                bool isFourByte = (i > 0 && data[i - 1] == 0x00);
                int startCodeOffset = isFourByte ? i - 1 : i;
                int startCodeLen = isFourByte ? 4 : 3;

                if (lastNaluStart != -1)
                {
                    int payloadStart = lastNaluStart + lastNaluStartCodeLength;
                    int payloadLength = startCodeOffset - payloadStart;
                    if (payloadLength > 0)
                    {
                        byte[] nalu = new byte[4 + payloadLength];
                        nalu[0] = 0x00; nalu[1] = 0x00; nalu[2] = 0x00; nalu[3] = 0x01;
                        Array.Copy(data, payloadStart, nalu, 4, payloadLength);
                        nalus.Add(nalu);
                    }
                }

                lastNaluStart = startCodeOffset;
                lastNaluStartCodeLength = startCodeLen;
                i += 3;
            }
            else
            {
                i++;
            }
        }
        return nalus;
    }
}

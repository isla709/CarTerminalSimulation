using TerminalSimulation.Wpf.ViewModels;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class Jt808BodyLengthTests
{
    [Fact]
    public void AppendRawBytes_AllowsExactly1023BytesAndUpdatesChecksum()
    {
        var frame = CreateEmptyFrame();
        var result = MainViewModel.AppendRawBytesToJT808Package(frame, Enumerable.Repeat((byte)0x11, 1023).ToArray());
        var unescaped = Unescape(result);
        Assert.Equal(1023, ((unescaped[2] << 8) | unescaped[3]) & 0x03FF);
        Assert.Equal(unescaped[^1], unescaped[..^1].Aggregate((byte)0, (value, item) => (byte)(value ^ item)));
    }

    [Fact]
    public void AppendRawBytes_RejectsBodyLargerThan1023Bytes()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MainViewModel.AppendRawBytesToJT808Package(CreateEmptyFrame(), new byte[1024]));
        Assert.Contains("必须使用分包发送", exception.Message);
    }

    private static byte[] CreateEmptyFrame()
    {
        var content = new byte[] { 0x02, 0x00, 0x00, 0x00, 0, 0, 0, 0, 0, 0, 1 };
        var checksum = content.Aggregate((byte)0, (value, item) => (byte)(value ^ item));
        return new byte[] { 0x7E }.Concat(content).Append(checksum).Append((byte)0x7E).ToArray();
    }

    private static byte[] Unescape(byte[] frame)
    {
        var result = new List<byte>();
        for (var index = 1; index < frame.Length - 1; index++)
        {
            if (frame[index] == 0x7D && index + 1 < frame.Length - 1)
            {
                result.Add(frame[++index] == 0x02 ? (byte)0x7E : (byte)0x7D);
            }
            else result.Add(frame[index]);
        }
        return result.ToArray();
    }
}

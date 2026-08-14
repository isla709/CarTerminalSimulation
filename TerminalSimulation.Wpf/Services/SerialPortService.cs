using System.IO.Ports;

namespace TerminalSimulation.Wpf.Services;

internal sealed class SerialPortService : ISerialPortService
{
    private SerialPort? _port;
    public bool IsOpen => _port?.IsOpen == true;
    public event Action<byte[]>? DataReceived;
    public IReadOnlyList<string> GetPortNames() => SerialPort.GetPortNames().OrderBy(name => name).ToArray();

    public void Open(string portName, int baudRate)
    {
        Close();
        _port = new SerialPort(portName, baudRate);
        _port.DataReceived += OnDataReceived;
        _port.Open();
    }

    public void Write(byte[] data)
    {
        if (!IsOpen) throw new InvalidOperationException("串口未打开");
        _port!.Write(data, 0, data.Length);
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        var port = _port;
        if (port?.IsOpen != true) return;
        var count = port.BytesToRead;
        if (count <= 0) return;
        var buffer = new byte[count];
        var read = port.Read(buffer, 0, count);
        if (read != buffer.Length) Array.Resize(ref buffer, read);
        DataReceived?.Invoke(buffer);
    }

    public void Close()
    {
        var port = Interlocked.Exchange(ref _port, null);
        if (port == null) return;
        port.DataReceived -= OnDataReceived;
        if (port.IsOpen) port.Close();
        port.Dispose();
    }

    public void Dispose() => Close();
}

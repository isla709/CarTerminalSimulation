using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace TerminalSimulation.Avalonia
{
    public static class ConsoleLogger
    {
        private static bool _isConsoleActive = false;
        private static readonly object _lock = new object();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        public static void Setup(string[] args)
        {
            bool hasConsoleArg = args.Contains("--console") || args.Contains("-c") || args.Contains("--verbose") || args.Contains("-v");
            
            // Try to attach to the parent process's console (e.g. started from CMD or PowerShell)
            if (AttachConsole(-1))
            {
                _isConsoleActive = true;
            }
            // If failed to attach, but user explicitly requested console window, allocate a new one
            else if (hasConsoleArg)
            {
                if (AllocConsole())
                {
                    _isConsoleActive = true;
                }
            }

            if (_isConsoleActive)
            {
                try
                {
                    // Redirect standard output to the attached console
                    var stdOut = Console.OpenStandardOutput();
                    if (stdOut != Stream.Null)
                    {
                        var writer = new StreamWriter(stdOut, System.Text.Encoding.UTF8) { AutoFlush = true };
                        Console.SetOut(writer);
                    }

                    // Redirect standard error to the attached console
                    var stdErr = Console.OpenStandardError();
                    if (stdErr != Stream.Null)
                    {
                        var errorWriter = new StreamWriter(stdErr, System.Text.Encoding.UTF8) { AutoFlush = true };
                        Console.SetError(errorWriter);
                    }
                }
                catch
                {
                    // Ignore redirection errors
                }

                Console.WriteLine();
                Console.WriteLine("================================================================================");
                Console.WriteLine($"[TerminalSimulation] 终端模拟系统详细控制台日志启动. 时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                Console.WriteLine("支持参数: -c, --console, -v, --verbose 启动独立控制台窗口");
                Console.WriteLine("================================================================================");
            }
        }

        public static void LogInfo(string message)
        {
            WriteLog("INFO", message);
        }

        public static void LogAction(string action, string details)
        {
            WriteLog("ACTION", $"【操作】{action} | 详情: {details}");
        }

        public static void LogNetwork(string direction, byte[] rawData, string parsedDetails)
        {
            string hexData = rawData != null && rawData.Length > 0 ? BitConverter.ToString(rawData).Replace("-", " ") : "";
            string msg = $"【{direction}】";
            if (!string.IsNullOrEmpty(hexData))
            {
                msg += $" RAW ({rawData!.Length} B): {hexData}\n";
            }
            msg += $"解析详情:\n{parsedDetails}";
            WriteLog(direction, msg);
        }

        public static void LogDebug(string component, string message)
        {
            WriteLog("DEBUG", $"[{component}] {message}");
        }

        public static void LogError(string component, string message, Exception? ex = null)
        {
            string errMsg = $"[{component}] 错误: {message}";
            if (ex != null)
            {
                errMsg += $"\n[异常堆栈] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
            }
            WriteLog("ERROR", errMsg);
        }

        private static void WriteLog(string level, string message)
        {
            if (!_isConsoleActive) return;

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string prefix = $"[{timestamp}] [{level}] ";

            lock (_lock)
            {
                var originalColor = Console.ForegroundColor;
                switch (level)
                {
                    case "INFO":
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        break;
                    case "ACTION":
                        Console.ForegroundColor = ConsoleColor.Green;
                        break;
                    case "SEND":
                    case "发送":
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        break;
                    case "RECEIVE":
                    case "接收":
                    case "接收解析":
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        break;
                    case "DEBUG":
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        break;
                    case "ERROR":
                        Console.ForegroundColor = ConsoleColor.Red;
                        break;
                    default:
                        Console.ForegroundColor = ConsoleColor.Gray;
                        break;
                }

                Console.Write(prefix);
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine(message);
                Console.ForegroundColor = originalColor;
            }
        }
    }
}

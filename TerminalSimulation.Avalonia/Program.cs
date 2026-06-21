using Avalonia;
using Avalonia.WebView.Desktop;

namespace TerminalSimulation.Avalonia;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .UseDesktopWebView()
            .LogToTrace();
}

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace TerminalSimulation.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ConsoleLogger.Setup(desktop.Args ?? Array.Empty<string>());

            var splash = new SplashWindow();
            splash.Show();

            await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CarTerminalSim_LibVLC_x64");
                    if (!System.IO.File.Exists(System.IO.Path.Combine(tempDir, "libvlc.dll")))
                    {
                        if (!System.IO.Directory.Exists(tempDir))
                            System.IO.Directory.CreateDirectory(tempDir);

                        using var stream = System.Reflection.Assembly.GetExecutingAssembly()
                            .GetManifestResourceStream("libvlc.zip");
                        if (stream != null)
                        {
                            using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
                            System.IO.Compression.ZipFileExtensions.ExtractToDirectory(archive, tempDir, true);
                        }
                        else
                        {
                            Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                var msgBox = new Window
                                {
                                    Title = "错误",
                                    Content = "Cannot find libvlc.zip.",
                                    Width = 300,
                                    Height = 150
                                };
                                msgBox.Show();
                            });
                        }
                    }
                    LibVLCSharp.Shared.Core.Initialize(tempDir);
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var msgBox = new Window
                        {
                            Title = "警告",
                            Content = $"初始化视频组件失败: {ex.Message}\n如果不需要视频功能可忽略此错误。",
                            Width = 400,
                            Height = 200
                        };
                        msgBox.Show();
                    });
                }
            });

            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            splash.Close();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

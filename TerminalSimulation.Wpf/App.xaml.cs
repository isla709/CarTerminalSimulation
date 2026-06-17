using System.Configuration;
using System.Data;
using System.Windows;

namespace TerminalSimulation.Wpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        ConsoleLogger.Setup(e.Args);
        base.OnStartup(e);

        var splash = new SplashWindow();
        splash.Show();

        await System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CarTerminalSim_LibVLC_x64");
                if (!System.IO.File.Exists(System.IO.Path.Combine(tempDir, "libvlc.dll")))
                {
                    if (!System.IO.Directory.Exists(tempDir)) System.IO.Directory.CreateDirectory(tempDir);
                    using var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("libvlc.zip");
                    if (stream != null)
                    {
                        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
                        System.IO.Compression.ZipFileExtensions.ExtractToDirectory(archive, tempDir, true);
                    }
                    else
                    {
                        Dispatcher.Invoke(() => MessageBox.Show("Cannot find libvlc.zip."));
                    }
                }
                LibVLCSharp.Shared.Core.Initialize(tempDir);
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => MessageBox.Show($"初始化视频组件失败: {ex.Message}\n如果不需要视频功能可忽略此错误。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning));
            }
        });

        var mainWindow = new MainWindow();
        Application.Current.MainWindow = mainWindow;
        mainWindow.Show();
        splash.Close();
    }
}


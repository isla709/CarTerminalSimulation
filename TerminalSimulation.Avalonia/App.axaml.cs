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
              if (System.OperatingSystem.IsWindows())
            {
                try
                {
                    // Version the extraction folder so an application update cannot
                    // reuse an older, partially compatible native VLC installation.
                    string tempDir = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(),
                        "CarTerminalSim_LibVLC_x64_3.0.23.1");
                    string libVlcPath = System.IO.Path.Combine(tempDir, "libvlc.dll");
                    string pluginsPath = System.IO.Path.Combine(tempDir, "plugins");
                    if (!System.IO.File.Exists(libVlcPath) ||
                        !System.IO.Directory.Exists(pluginsPath))
                    {
                        if (System.IO.Directory.Exists(tempDir))
                        {
                            System.IO.Directory.Delete(tempDir, recursive: true);
                        }
                        System.IO.Directory.CreateDirectory(tempDir);
                        using var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("libvlc.zip");
                        if (stream == null)
                        {
                            throw new System.IO.FileNotFoundException("Embedded libvlc.zip was not found.");
                        }
                        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
                        System.IO.Compression.ZipFileExtensions.ExtractToDirectory(archive, tempDir, true);
                    }
                    LibVLCSharp.Shared.Core.Initialize(tempDir);
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load embedded LibVLC: {ex.Message}");
                    LibVLCSharp.Shared.Core.Initialize();
                }
            }
            else
            {
                LibVLCSharp.Shared.Core.Initialize();
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

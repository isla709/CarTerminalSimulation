using System.Configuration;
using System.Data;
using System.Windows;

namespace TerminalSimulation.Wpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"初始化视频组件失败: {ex.Message}\n如果不需要视频功能可忽略此错误。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}


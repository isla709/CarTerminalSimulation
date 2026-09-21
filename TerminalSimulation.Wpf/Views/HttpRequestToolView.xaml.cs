using System.Windows.Controls;
using TerminalSimulation.Wpf.ViewModels.Utilities;

namespace TerminalSimulation.Wpf.Views;

public partial class HttpRequestToolView : UserControl
{
    public HttpRequestToolView()
    {
        InitializeComponent();
        DataContext = new HttpRequestToolViewModel();
    }
}

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TerminalSimulation.Plugins.XunjieCloud.Avalonia.Views
{
    public partial class XunjieCloudStreamView : UserControl
    {
        public XunjieCloudStreamView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
